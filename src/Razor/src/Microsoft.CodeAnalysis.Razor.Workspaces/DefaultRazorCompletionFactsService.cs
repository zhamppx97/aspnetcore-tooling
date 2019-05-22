// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Legacy;
using Microsoft.AspNetCore.Razor.Language.Syntax;
using RazorSyntaxNode = Microsoft.AspNetCore.Razor.Language.Syntax.SyntaxNode;

namespace Microsoft.CodeAnalysis.Razor
{
    [Shared]
    [Export(typeof(RazorCompletionFactsService))]
    internal class DefaultRazorCompletionFactsService : RazorCompletionFactsService
    {
        private static readonly IEnumerable<DirectiveDescriptor> DefaultDirectives = new[]
        {
            CSharpCodeParser.AddTagHelperDirectiveDescriptor,
            CSharpCodeParser.RemoveTagHelperDirectiveDescriptor,
            CSharpCodeParser.TagHelperPrefixDirectiveDescriptor,
        };

        public override IReadOnlyList<RazorCompletionItem> GetCompletionItems(RazorSyntaxTree syntaxTree, SourceSpan location)
        {
            var completionItems = new List<RazorCompletionItem>();

            if (AtDirectiveCompletionPoint(syntaxTree, location))
            {
                var directiveCompletions = GetDirectiveCompletionItems(syntaxTree);
                completionItems.AddRange(directiveCompletions);
            }

            if (AtTransitionedDirectiveAttributeCompletionPoint(syntaxTree, location))
            {
                var completions = GetTransitionedDirectiveAttributeCompletionItems();
                completionItems.AddRange(completions);
            }

            if (AtDirectiveAttributeParameterCompletionPoint(syntaxTree, location))
            {
                var completions = GetDirectiveAttributeParameterCompletionItems();
                completionItems.AddRange(completions);
            }

            return completionItems;
        }

        // Internal for testing
        internal static bool AtDirectiveCompletionPoint(RazorSyntaxTree syntaxTree, SourceSpan location)
        {
            if (syntaxTree == null)
            {
                return false;
            }

            var change = new SourceChange(location, string.Empty);
            var owner = syntaxTree.Root.LocateOwner(change);

            if (owner == null)
            {
                return false;
            }

            // Do not provide IntelliSense for explicit expressions. Explicit expressions will usually look like:
            // [@] [(] [DateTime.Now] [)]
            var implicitExpression = owner.FirstAncestorOrSelf<CSharpImplicitExpressionSyntax>();
            if (implicitExpression == null)
            {
                return false;
            }

            if (owner.ChildNodes().Any(n => !n.IsToken || !IsDirectiveCompletableToken((AspNetCore.Razor.Language.Syntax.SyntaxToken)n)))
            {
                // Implicit expression contains invalid directive tokens
                return false;
            }

            if (implicitExpression.FirstAncestorOrSelf<RazorDirectiveSyntax>() != null)
            {
                // Implicit expression is nested in a directive
                return false;
            }

            if (implicitExpression.FirstAncestorOrSelf<CSharpStatementSyntax>() != null)
            {
                // Implicit expression is nested in a statement
                return false;
            }

            if (implicitExpression.FirstAncestorOrSelf<MarkupElementSyntax>() != null)
            {
                // Implicit expression is nested in an HTML element
                return false;
            }

            if (implicitExpression.FirstAncestorOrSelf<MarkupTagHelperElementSyntax>() != null)
            {
                // Implicit expression is nested in a TagHelper
                return false;
            }

            return true;
        }

        internal static bool AtTransitionedDirectiveAttributeCompletionPoint(RazorSyntaxTree syntaxTree, SourceSpan location)
        {
            if (syntaxTree == null)
            {
                return false;
            }

            if (!FileKinds.IsComponent(syntaxTree.Options.FileKind))
            {
                // Directive attributes are only supported in components
                return false;
            }

            var change = new SourceChange(location, string.Empty);
            var owner = syntaxTree.Root.LocateOwner(change);

            if (owner == null)
            {
                return false;
            }

            // Do not provide IntelliSense for explicit expressions. Explicit expressions will usually look like:
            // [@] [(] [DateTime.Now] [)]
            //var isImplicitExpression = owner.FirstAncestorOrSelf<CSharpImplicitExpressionSyntax>() != null;
            //if (isImplicitExpression &&
            //    owner.ChildNodes().All(n => n.IsToken && IsDirectiveCompletableToken((AspNetCore.Razor.Language.Syntax.SyntaxToken)n)))
            //{
            //    return true;
            //}

            if (!TryGetSelectedAttribute(owner, out var selectedAttributeName))
            {
                return false;
            }

            var nameString = selectedAttributeName.GetContent();
            var nameSpan = selectedAttributeName.Span;

            var relativeColonIndex = nameString.IndexOf(':');
            if (relativeColonIndex != -1)
            {
                // There's a parameter in the attribute, we need to adjust the nameSpan to only be the prefixed portion of the attribute

                var nameStart = selectedAttributeName.Span.Start;
                var absoluteColonIndex = selectedAttributeName.Span.Start + relativeColonIndex;
                nameSpan = new TextSpan(nameStart, absoluteColonIndex - nameStart);
            }

            if (!nameSpan.IntersectsWith(location.AbsoluteIndex))
            {
                return false;
            }

            if (!nameString.StartsWith("#"))
            {
                // TODO: Remove once we can properly identify directive attributes in the TryGetSelectedAttribute
                return false;
            }

            // We're at a valid completion point:
            //
            // @|
            // @bin|d
            // @bind|
            // @bind|=""

            return true;
        }

        private static bool AtDirectiveAttributeParameterCompletionPoint(RazorSyntaxTree syntaxTree, SourceSpan location)
        {
            if (syntaxTree == null)
            {
                return false;
            }

            if (!FileKinds.IsComponent(syntaxTree.Options.FileKind))
            {
                // Directive attributes are only supported in components
                return false;
            }

            var change = new SourceChange(location, string.Empty);
            var owner = syntaxTree.Root.LocateOwner(change);

            if (owner == null)
            {
                return false;
            }

            if (!TryGetSelectedAttribute(owner, out var selectedAttributeName))
            {
                return false;
            }

            if (!selectedAttributeName.Span.IntersectsWith(location.AbsoluteIndex))
            {
                return false;
            }

            var nameString = selectedAttributeName.GetContent();
            var relativeColonIndex = nameString.IndexOf(':');
            if (relativeColonIndex == -1)
            {
                return false;
            }

            var nameEnd = selectedAttributeName.Span.End;
            var absoluteColonIndex = selectedAttributeName.Span.Start + relativeColonIndex;
            var parameterSpan = new TextSpan(absoluteColonIndex, nameEnd - absoluteColonIndex);
            if (!parameterSpan.IntersectsWith(location.AbsoluteIndex))
            {
                return false;
            }

            return true;
        }

        // Internal for testing
        internal static List<RazorCompletionItem> GetDirectiveCompletionItems(RazorSyntaxTree syntaxTree)
        {
            var defaultDirectives = FileKinds.IsComponent(syntaxTree.Options.FileKind) ? Array.Empty<DirectiveDescriptor>() : DefaultDirectives;
            var directives = syntaxTree.Options.Directives.Concat(defaultDirectives);
            var completionItems = new List<RazorCompletionItem>();
            foreach (var directive in directives)
            {
                var completionDisplayText = directive.DisplayName ?? directive.Directive;
                var completionItem = new RazorCompletionItem(
                    completionDisplayText,
                    directive.Directive,
                    directive.Description,
                    RazorCompletionItemKind.Directive);
                completionItems.Add(completionItem);
            }

            return completionItems;
        }

        // Internal for testing
        internal static List<RazorCompletionItem> GetTransitionedDirectiveAttributeCompletionItems(/* TODO ADD STUFF */)
        {
            var completionItems = new List<RazorCompletionItem>();
            var completionDisplayText = "#bind";
            var completionItem = new RazorCompletionItem(
                completionDisplayText,
                "#bind",
                "TEST BIND STUFF",
                RazorCompletionItemKind.DirectiveAttribute);
            completionItems.Add(completionItem);

            return completionItems;
        }

        private static List<RazorCompletionItem> GetDirectiveAttributeParameterCompletionItems(/* TODO ADD STUFF */)
        {
            var completionItems = new List<RazorCompletionItem>();
            var completionDisplayText = "format";
            var completionItem = new RazorCompletionItem(
                completionDisplayText,
                "format",
                "TEST FORMAT STUFF",
                RazorCompletionItemKind.DirectiveParameter);
            completionItems.Add(completionItem);

            return completionItems;
        }

        // Internal for testing
        internal static bool IsDirectiveCompletableToken(AspNetCore.Razor.Language.Syntax.SyntaxToken token)
        {
            return token.Kind == SyntaxKind.Identifier ||
                // Marker symbol
                token.Kind == SyntaxKind.Marker;
        }

        private static bool TryGetSelectedAttribute(RazorSyntaxNode owner, out MarkupTextLiteralSyntax selectedAttributeName)
        {
            var attribute = owner.Parent;
            switch (attribute)
            {
                case MarkupMinimizedAttributeBlockSyntax minimizedMarkupAttribute:
                    selectedAttributeName = minimizedMarkupAttribute.Name;
                    return true;
                case MarkupAttributeBlockSyntax markupAttribute:
                    selectedAttributeName = markupAttribute.Name;
                    return true;
                case MarkupMinimizedTagHelperAttributeSyntax minimizedTagHelperAttribute:
                    selectedAttributeName = minimizedTagHelperAttribute.Name;
                    return true;
                case MarkupTagHelperAttributeSyntax tagHelperAttribute:
                    selectedAttributeName = tagHelperAttribute.Name;
                    return true;
            }

            selectedAttributeName = null;
            return false;
        }
    }
}
