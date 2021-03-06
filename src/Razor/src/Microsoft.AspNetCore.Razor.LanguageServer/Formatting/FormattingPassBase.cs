﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Legacy;
using Microsoft.AspNetCore.Razor.Language.Syntax;
using Microsoft.AspNetCore.Razor.LanguageServer.Common;
using Microsoft.CodeAnalysis.Text;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using TextSpan = Microsoft.CodeAnalysis.Text.TextSpan;

namespace Microsoft.AspNetCore.Razor.LanguageServer.Formatting
{
    internal abstract class FormattingPassBase : IFormattingPass
    {
        protected static readonly int DefaultOrder = 1000;

        public FormattingPassBase(
            RazorDocumentMappingService documentMappingService,
            FilePathNormalizer filePathNormalizer,
            ClientNotifierServiceBase server)
        {
            if (documentMappingService is null)
            {
                throw new ArgumentNullException(nameof(documentMappingService));
            }

            if (filePathNormalizer is null)
            {
                throw new ArgumentNullException(nameof(filePathNormalizer));
            }

            if (server is null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            DocumentMappingService = documentMappingService;
            CSharpFormatter = new CSharpFormatter(documentMappingService, server, filePathNormalizer);
            HtmlFormatter = new HtmlFormatter(server, filePathNormalizer);
        }

        public abstract bool IsValidationPass { get; }

        public virtual int Order => DefaultOrder;

        protected RazorDocumentMappingService DocumentMappingService { get; }

        protected CSharpFormatter CSharpFormatter { get; }

        protected HtmlFormatter HtmlFormatter { get; }

        public virtual Task<FormattingResult> ExecuteAsync(FormattingContext context, FormattingResult result, CancellationToken cancellationToken)
        {
            return Task.FromResult(Execute(context, result));
        }

        public virtual FormattingResult Execute(FormattingContext context, FormattingResult result)
        {
            return result;
        }

        protected TextEdit[] RemapTextEdits(RazorCodeDocument codeDocument, TextEdit[] projectedTextEdits, RazorLanguageKind projectedKind)
        {
            if (codeDocument is null)
            {
                throw new ArgumentNullException(nameof(codeDocument));
            }

            if (projectedTextEdits is null)
            {
                throw new ArgumentNullException(nameof(projectedTextEdits));
            }

            if (projectedKind != RazorLanguageKind.CSharp)
            {
                // Non C# projections map directly to Razor. No need to remap.
                return projectedTextEdits;
            }

            var edits = new List<TextEdit>();
            for (var i = 0; i < projectedTextEdits.Length; i++)
            {
                var projectedRange = projectedTextEdits[i].Range;
                if (codeDocument.IsUnsupported() ||
                    !DocumentMappingService.TryMapFromProjectedDocumentRange(codeDocument, projectedRange, out var originalRange))
                {
                    // Can't map range. Discard this edit.
                    continue;
                }

                var edit = new TextEdit()
                {
                    Range = originalRange,
                    NewText = projectedTextEdits[i].NewText
                };

                edits.Add(edit);
            }

            return edits.ToArray();
        }

        protected static TextEdit[] NormalizeTextEdits(SourceText originalText, TextEdit[] edits)
        {
            if (originalText is null)
            {
                throw new ArgumentNullException(nameof(originalText));
            }

            if (edits is null)
            {
                throw new ArgumentNullException(nameof(edits));
            }

            var changes = edits.Select(e => e.AsTextChange(originalText));
            var changedText = originalText.WithChanges(changes);
            var cleanChanges = SourceTextDiffer.GetMinimalTextChanges(originalText, changedText, lineDiffOnly: false);
            var cleanEdits = cleanChanges.Select(c => c.AsTextEdit(originalText)).ToArray();
            return cleanEdits;
        }

        // Returns the minimal TextSpan that encompasses all the differences between the old and the new text.
        protected static void TrackEncompassingChange(SourceText oldText, IEnumerable<TextChange> changes, out TextSpan spanBeforeChange, out TextSpan spanAfterChange)
        {
            if (oldText is null)
            {
                throw new ArgumentNullException(nameof(oldText));
            }

            if (changes is null)
            {
                throw new ArgumentNullException(nameof(changes));
            }

            var newText = oldText.WithChanges(changes);
            var affectedRange = newText.GetEncompassingTextChangeRange(oldText);

            spanBeforeChange = affectedRange.Span;
            spanAfterChange = new TextSpan(spanBeforeChange.Start, affectedRange.NewLength);
        }

        protected List<TextChange> AdjustIndentation(FormattingContext context, CancellationToken cancellationToken, Range range = null)
        {
            // In this method, the goal is to make final adjustments to the indentation of each line.
            // We will take into account the following,
            // 1. The indentation due to nested C# structures
            // 2. The indentation due to Razor and HTML constructs

            var text = context.SourceText;
            range ??= TextSpan.FromBounds(0, text.Length).AsRange(text);

            // First, let's build an understanding of the desired C# indentation at the beginning and end of each source mapping.
            var sourceMappingIndentations = new SortedDictionary<int, int>();
            foreach (var mapping in context.CodeDocument.GetCSharpDocument().SourceMappings)
            {
                var mappingSpan = new TextSpan(mapping.OriginalSpan.AbsoluteIndex, mapping.OriginalSpan.Length);
                var mappingRange = mappingSpan.AsRange(context.SourceText);
                if (!ShouldFormat(context, mappingRange.Start, allowImplicitStatements: true))
                {
                    // We don't care about this range as this can potentially lead to incorrect scopes.
                    continue;
                }

                var startIndentation = CSharpFormatter.GetCSharpIndentation(context, mapping.GeneratedSpan.AbsoluteIndex, cancellationToken);
                sourceMappingIndentations[mapping.OriginalSpan.AbsoluteIndex] = startIndentation;

                var endIndentation = CSharpFormatter.GetCSharpIndentation(context, mapping.GeneratedSpan.AbsoluteIndex + mapping.GeneratedSpan.Length + 1, cancellationToken);
                sourceMappingIndentations[mapping.OriginalSpan.AbsoluteIndex + mapping.OriginalSpan.Length + 1] = endIndentation;
            }

            var sourceMappingIndentationScopes = sourceMappingIndentations.Keys.ToArray();

            // Now, let's combine the C# desired indentation with the Razor and HTML indentation for each line.
            var newIndentations = new Dictionary<int, int>();
            for (var i = range.Start.Line; i <= range.End.Line; i++)
            {
                if (context.Indentations[i].EmptyOrWhitespaceLine)
                {
                    // We should remove whitespace on empty lines.
                    newIndentations[i] = 0;
                    continue;
                }

                var line = context.SourceText.Lines[i];
                var lineStart = line.Start;
                int csharpDesiredIndentation;
                if (DocumentMappingService.TryMapToProjectedDocumentPosition(context.CodeDocument, lineStart, out _, out var projectedLineStart))
                {
                    // We were able to map this line to C# directly.
                    csharpDesiredIndentation = CSharpFormatter.GetCSharpIndentation(context, projectedLineStart, cancellationToken);
                }
                else
                {
                    // Couldn't remap. This is probably a non-C# location.
                    // Use SourceMapping indentations to locate the C# scope of this line.
                    // E.g,
                    //
                    // @if (true) {
                    //   <div>
                    //  |</div>
                    // }
                    //
                    // We can't find a direct mapping at |, but we can infer its base indentation from the
                    // indentation of the latest source mapping prior to this line.
                    // We use binary search to find that spot.

                    var index = Array.BinarySearch(sourceMappingIndentationScopes, lineStart);
                    if (index < 0)
                    {
                        // Couldn't find the exact value. Find the index of the element to the left of the searched value.
                        index = (~index) - 1;
                    }

                    // This will now be set to the same value as the end of the closest source mapping.
                    csharpDesiredIndentation = index < 0 ? 0 : sourceMappingIndentations[sourceMappingIndentationScopes[index]];
                }

                // Now let's use that information to figure out the effective C# indentation.
                // This should be based on context.
                // For instance, lines inside @code/@functions block should be reduced one level
                // and lines inside @{} should be reduced by two levels.

                var minCSharpIndentation = context.GetIndentationOffsetForLevel(context.Indentations[i].MinCSharpIndentLevel);
                if (csharpDesiredIndentation < minCSharpIndentation)
                {
                    // CSharp formatter doesn't want to indent this. Let's not touch it.
                    continue;
                }

                var effectiveCSharpDesiredIndentation = csharpDesiredIndentation - minCSharpIndentation;
                var razorDesiredIndentation = context.GetIndentationOffsetForLevel(context.Indentations[i].IndentationLevel);
                if (!context.Indentations[i].StartsInCSharpContext)
                {
                    // This is a non-C# line.
                    if (context.IsFormatOnType)
                    {
                        // HTML formatter doesn't run in the case of format on type.
                        // Let's stick with our syntax understanding of HTML to figure out the desired indentation.
                    }
                    else
                    {
                        // Given that the HTML formatter ran before this, we can assume
                        // HTML is already correctly formatted. So we can use the existing indentation as is.
                        razorDesiredIndentation = context.Indentations[i].ExistingIndentation;
                    }
                }
                var effectiveDesiredIndentation = razorDesiredIndentation + effectiveCSharpDesiredIndentation;

                // This will now contain the indentation we ultimately want to apply to this line.
                newIndentations[i] = effectiveDesiredIndentation;
            }

            // Now that we have collected all the indentations for each line, let's convert them to text edits.
            var changes = new List<TextChange>();
            foreach (var item in newIndentations)
            {
                var line = item.Key;
                var indentation = item.Value;
                Debug.Assert(indentation >= 0, "Negative indentation. This is unexpected.");

                var existingIndentationLength = context.Indentations[line].ExistingIndentation;
                var spanToReplace = new TextSpan(context.SourceText.Lines[line].Start, existingIndentationLength);
                var effectiveDesiredIndentation = context.GetIndentationString(indentation);
                changes.Add(new TextChange(spanToReplace, effectiveDesiredIndentation));
            }

            return changes;
        }

        protected List<TextChange> CleanupDocument(FormattingContext context, Range range = null)
        {
            var text = context.SourceText;
            range ??= TextSpan.FromBounds(0, text.Length).AsRange(text);
            var csharpDocument = context.CodeDocument.GetCSharpDocument();

            var changes = new List<TextChange>();
            foreach (var mapping in csharpDocument.SourceMappings)
            {
                var mappingSpan = new TextSpan(mapping.OriginalSpan.AbsoluteIndex, mapping.OriginalSpan.Length);
                var mappingRange = mappingSpan.AsRange(text);
                if (!range.LineOverlapsWith(mappingRange))
                {
                    // We don't care about this range. It didn't change.
                    continue;
                }

                CleanupSourceMappingStart(context, mappingRange, changes);

                CleanupSourceMappingEnd(context, mappingRange, changes);
            }

            return changes;
        }

        private void CleanupSourceMappingStart(FormattingContext context, Range sourceMappingRange, List<TextChange> changes)
        {
            //
            // We look through every source mapping that intersects with the affected range and
            // bring the first line to its own line and adjust its indentation,
            //
            // E.g,
            //
            // @{   public int x = 0;
            // }
            //
            // becomes,
            //
            // @{
            //    public int x  = 0;
            // }
            // 

            if (!ShouldFormat(context, sourceMappingRange.Start, allowImplicitStatements: false))
            {
                // We don't want to run cleanup on this range.
                return;
            }

            // @{
            //     if (true)
            //     {
            //         <div></div>|
            //
            //              |}
            // }
            // We want to return the length of the range marked by |...|
            //
            var text = context.SourceText;
            var sourceMappingSpan = sourceMappingRange.AsTextSpan(text);
            var whitespaceLength = text.GetFirstNonWhitespaceOffset(sourceMappingSpan);
            if (whitespaceLength == null)
            {
                // There was no content here. Skip.
                return;
            }

            var spanToReplace = new TextSpan(sourceMappingSpan.Start, whitespaceLength.Value);
            if (!context.TryGetIndentationLevel(spanToReplace.End, out var contentIndentLevel))
            {
                // Can't find the correct indentation for this content. Leave it alone.
                return;
            }

            // At this point, `contentIndentLevel` should contain the correct indentation level for `}` in the above example.
            var replacement = context.NewLineString + context.GetIndentationLevelString(contentIndentLevel);

            // After the below change the above example should look like,
            // @{
            //     if (true)
            //     {
            //         <div></div>
            //     }
            // }
            var change = new TextChange(spanToReplace, replacement);
            changes.Add(change);
        }

        private void CleanupSourceMappingEnd(FormattingContext context, Range sourceMappingRange, List<TextChange> changes)
        {
            //
            // We look through every source mapping that intersects with the affected range and
            // bring the content after the last line to its own line and adjust its indentation,
            //
            // E.g,
            //
            // @{
            //     if (true)
            //     {  <div></div>
            //     }
            // }
            //
            // becomes,
            //
            // @{
            //    if (true)
            //    {
            //        </div></div>
            //    }
            // }
            //

            var text = context.SourceText;
            var sourceMappingSpan = sourceMappingRange.AsTextSpan(text);
            var mappingEndLineIndex = sourceMappingRange.End.Line;
            if (!context.Indentations[mappingEndLineIndex].StartsInCSharpContext)
            {
                // For corner cases like (Position marked with |),
                // It is already in a separate line. It doesn't need cleaning up.
                // @{
                //     if (true}
                //     {
                //         |<div></div>
                //     }
                // }
                //
                return;
            }

            if (!ShouldFormat(context, sourceMappingRange.End, allowImplicitStatements: false))
            {
                // We don't want to run cleanup on this range.
                return;
            }

            var contentStartOffset = text.Lines[mappingEndLineIndex].GetFirstNonWhitespaceOffset(sourceMappingRange.End.Character);
            if (contentStartOffset == null)
            {
                // There is no content after the end of this source mapping. No need to clean up.
                return;
            }

            var spanToReplace = new TextSpan(sourceMappingSpan.End, 0);
            if (!context.TryGetIndentationLevel(spanToReplace.End, out var contentIndentLevel))
            {
                // Can't find the correct indentation for this content. Leave it alone.
                return;
            }

            // At this point, `contentIndentLevel` should contain the correct indentation level for `}` in the above example.
            var replacement = context.NewLineString + context.GetIndentationLevelString(contentIndentLevel);

            // After the below change the above example should look like,
            // @{
            //     if (true)
            //     {
            //         <div></div>
            //     }
            // }
            var change = new TextChange(spanToReplace, replacement);
            changes.Add(change);
        }

        protected bool ShouldFormat(FormattingContext context, Position position, bool allowImplicitStatements)
        {
            // We should be called with start positions of various C# SourceMappings.
            if (position.Character == 0)
            {
                // The mapping starts at 0. It can't be anything special but pure C#. Let's format it.
                return true;
            }

            var sourceText = context.SourceText;
            var absoluteIndex = sourceText.Lines[(int)position.Line].Start + (int)position.Character;
            if (IsImplicitStatementStart() && !allowImplicitStatements)
            {
                return false;
            }

            var syntaxTree = context.CodeDocument.GetSyntaxTree();
            var change = new SourceChange(absoluteIndex, 0, string.Empty);
            var owner = syntaxTree.Root.LocateOwner(change);
            if (owner == null)
            {
                // Can't determine owner of this position. Optimistically allow formatting.
                return true;
            }

            if (IsInHtmlTag() ||
                IsInSingleLineDirective() ||
                IsImplicitOrExplicitExpression())
            {
                return false;
            }

            return true;

            bool IsImplicitStatementStart()
            {
                // We will return true if the position points to the start of the C# portion of an implicit statement.
                // `@|for(...)` - true
                // `@|if(...)` - true
                // `@{|...` - false
                // `@code {|...` - false
                //

                var previousCharIndex = absoluteIndex - 1;
                var previousChar = sourceText[previousCharIndex];
                if (previousChar != '@')
                {
                    // Not an implicit statement.
                    return false;
                }

                // This is an implicit statement if the previous '@' is not C# (meaning it shouldn't have a projected mapping).
                return !DocumentMappingService.TryMapToProjectedDocumentPosition(context.CodeDocument, previousCharIndex, out _, out _);
            }

            bool IsInHtmlTag()
            {
                // E.g, (| is position)
                //
                // `<p csharpattr="|Variable">` - true
                //
                return owner.AncestorsAndSelf().Any(
                    n => n is MarkupStartTagSyntax || n is MarkupTagHelperStartTagSyntax || n is MarkupEndTagSyntax || n is MarkupTagHelperEndTagSyntax);
            }

            bool IsInSingleLineDirective()
            {
                // E.g, (| is position)
                //
                // `@inject |SomeType SomeName` - true
                //
                // Note: @using directives don't have a descriptor associated with them, hence the extra null check.
                //
                return owner.AncestorsAndSelf().Any(
                    n => n is RazorDirectiveSyntax directive && (directive.DirectiveDescriptor == null || directive.DirectiveDescriptor.Kind == DirectiveKind.SingleLine));
            }

            bool IsImplicitOrExplicitExpression()
            {
                // E.g, (| is position)
                //
                // `@|foo` - true
                // `@(|foo)` - true
                //
                return owner.AncestorsAndSelf().Any(n => n is CSharpImplicitExpressionSyntax || n is CSharpExplicitExpressionSyntax);
            }
        }
    }
}
