// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis.Razor;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;

namespace Microsoft.VisualStudio.Editor.Razor
{
    internal class RazorDirectiveAttributeCompletionSource : IAsyncCompletionSource
    {
        // Internal for testing
        internal static readonly object DescriptionKey = new object();
        // Hardcoding the Guid here to avoid a reference to Microsoft.VisualStudio.ImageCatalog.dll
        // that is not present in Visual Studio for Mac
        internal static readonly Guid ImageCatalogGuid = new Guid("{ae27a6b0-e345-4288-96df-5eaf394ee369}");
        internal static readonly ImageElement DirectiveAttributeImageGlyph = new ImageElement(
            new ImageId(ImageCatalogGuid, 3564), // KnownImageIds.Type = 3564
            "Razor Directive Attribute.");
        internal static readonly ImmutableArray<CompletionFilter> DirectiveAttributeCompletionFilters = new[] {
            new CompletionFilter("Razor Directive Attrsibute", "r", DirectiveAttributeImageGlyph)
        }.ToImmutableArray();

        // Internal for testing
        internal readonly VisualStudioRazorParser _parser;
        private readonly RazorCompletionFactsService _completionFactsService;
        private readonly ICompletionBroker _completionBroker;
        private readonly ForegroundDispatcher _foregroundDispatcher;

        public RazorDirectiveAttributeCompletionSource(
            ForegroundDispatcher foregroundDispatcher,
            VisualStudioRazorParser parser,
            RazorCompletionFactsService completionFactsService,
            ICompletionBroker completionBroker)
        {
            if (foregroundDispatcher == null)
            {
                throw new ArgumentNullException(nameof(foregroundDispatcher));
            }

            if (parser == null)
            {
                throw new ArgumentNullException(nameof(parser));
            }

            if (completionFactsService == null)
            {
                throw new ArgumentNullException(nameof(completionFactsService));
            }

            _foregroundDispatcher = foregroundDispatcher;
            _parser = parser;
            _completionFactsService = completionFactsService;
            _completionBroker = completionBroker;
        }

        public async Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger, SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            _foregroundDispatcher.AssertBackgroundThread();

            try
            {
                var syntaxTree = await _parser.GetLatestSyntaxTreeAsync(triggerLocation.Snapshot, token);
                var location = new SourceSpan(triggerLocation.Position, 0);
                var razorCompletionItems = _completionFactsService.GetCompletionItems(syntaxTree, location);

                if (razorCompletionItems.Count > 0)
                {
                    // We're providing completion items while a legacy completion session is active.
                    var activeSessions = _completionBroker.GetSessions(session.TextView);
                    foreach (var activeSession in activeSessions)
                    {
                        if (activeSession.Properties.ContainsProperty(nameof(IAsyncCompletionSession)))
                        {
                            continue;
                        }

                        // Legacy completion is also active, we need to dismiss it.

                        _ = Task.Factory.StartNew(
                            () => activeSession.Dismiss(),
                            CancellationToken.None,
                            TaskCreationOptions.None,
                            _foregroundDispatcher.ForegroundScheduler);
                    }
                }
                else
                {
                    return CompletionContext.Empty;
                }

                var completionItems = new List<CompletionItem>();
                var completionItemKinds = new HashSet<RazorCompletionItemKind>();
                foreach (var razorCompletionItem in razorCompletionItems)
                {
                    if (razorCompletionItem.Kind != RazorCompletionItemKind.DirectiveAttribute &&
                        razorCompletionItem.Kind != RazorCompletionItemKind.DirectiveParameter)
                    {
                        // Don't support any other types of completion kinds other than directive attributes.
                        continue;
                    }

                    var completionItem = new CompletionItem(
                        displayText: razorCompletionItem.DisplayText,
                        filterText: razorCompletionItem.DisplayText,
                        insertText: razorCompletionItem.InsertText,
                        source: this,
                        icon: DirectiveAttributeImageGlyph,
                        filters: DirectiveAttributeCompletionFilters,
                        suffix: string.Empty,
                        sortText: razorCompletionItem.DisplayText,
                        attributeIcons: ImmutableArray<ImageElement>.Empty);
                    completionItem.Properties.AddProperty(DescriptionKey, razorCompletionItem.Description);
                    completionItems.Add(completionItem);
                    completionItemKinds.Add(razorCompletionItem.Kind);
                }

                session.Properties.SetCompletionItemKinds(completionItemKinds);
                var context = new CompletionContext(completionItems.ToImmutableArray());
                return context;
            }
            catch (OperationCanceledException)
            {
                return CompletionContext.Empty;
            }
        }

        public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
        {
            if (!item.Properties.TryGetProperty<string>(DescriptionKey, out var directiveDescription))
            {
                directiveDescription = string.Empty;
            }

            return Task.FromResult<object>(directiveDescription);
        }

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            // We can't retrieve the correct SyntaxTree/CodeDocument at this time because this extension point is synchronous so we need 
            // to make our "do we participate in completion" decision without one. We'll look to see if what we're operating on potentially 
            // looks like a directive attribute. We care about things that look like an expressions to provide directive attribute 
            // completions. Basically anything starting with a transition (@).

            var snapshot = triggerLocation.Snapshot;
            if (snapshot.Length == 0)
            {
                // Empty document, can not provide completions.
                return CompletionStartData.DoesNotParticipateInCompletion;
            }

            if (triggerLocation.Position == 0)
            {
                // Completion triggered at beginning of document, can't possibly be an attribute.
                return CompletionStartData.DoesNotParticipateInCompletion;
            }

            var leftEnd = triggerLocation.Position - 1;
            for (; leftEnd >= 0; leftEnd--)
            {
                var currentCharacter = snapshot[leftEnd];

                if (char.IsWhiteSpace(currentCharacter))
                {
                    // Valid left end attribute delimiter
                    leftEnd++;
                    break;
                }
                else if (IsInvalidAttributeDelimiter(currentCharacter))
                {
                    return CompletionStartData.DoesNotParticipateInCompletion;
                }
            }

            if (leftEnd >= snapshot.Length)
            {
                // Left part of the trigger is at the very end of the document without a possible transition
                return CompletionStartData.DoesNotParticipateInCompletion;
            }

            var leftMostCharacter = snapshot[leftEnd];
            if (leftMostCharacter != '#') // TODO: Change back to an @
            {
                // The left side of our simple expression should always be a Razor transition
                return CompletionStartData.DoesNotParticipateInCompletion;
            }

            var rightEnd = triggerLocation.Position;
            for (; rightEnd < snapshot.Length; rightEnd++)
            {
                var currentCharacter = snapshot[rightEnd];

                if (char.IsWhiteSpace(currentCharacter) || currentCharacter == '=')
                {
                    // Valid right end attribute delimiter
                    break;
                }
                else if (IsInvalidAttributeDelimiter(currentCharacter))
                {
                    return CompletionStartData.DoesNotParticipateInCompletion;
                }
            }

            var parameterDelimiter = -1;
            for (var i = leftEnd; i < rightEnd; i++)
            {
                if (snapshot[i] == ':')
                {
                    parameterDelimiter = i;
                }
            }

            if (parameterDelimiter != -1)
            {
                // There's a parameter delimiter in the expression that we've triggered on. We need to decide which side will
                // be the applicable to span.

                if (triggerLocation.Position <= parameterDelimiter)
                {
                    // The trigger location falls on the left hand side of the directive attribute parameter delimiter (:)
                    //
                    // <InputSelect |@bind-foo|:something
                    rightEnd = parameterDelimiter;
                }
                else
                {
                    // The trigger location falls on the right hand side of the directive attribute parameter delimiter (:)
                    //
                    // <InputSelect @bind-foo:|something|
                    leftEnd = parameterDelimiter + 1;
                }
            }
            else
            {
                // Do nothing, our directive attribute does not have parameters and left->right encompass the attribute
                //
                // <InputSelect |@bind-foo|
            }

            var applicableSpanLength = rightEnd - leftEnd;
            var applicableToSpan = new SnapshotSpan(triggerLocation.Snapshot, leftEnd, applicableSpanLength);

            return new CompletionStartData(CompletionParticipation.ProvidesItems, applicableToSpan);
        }

        private static bool IsInvalidAttributeDelimiter(char currentCharacter)
        {
            return currentCharacter == '<' || currentCharacter == '>' || currentCharacter == '\'' || currentCharacter == '"';
        }
    }
}
