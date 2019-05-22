// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Legacy;
using Microsoft.AspNetCore.Razor.Language.Syntax;
using Microsoft.VisualStudio.Editor.Razor;
using RazorSyntaxNode = Microsoft.AspNetCore.Razor.Language.Syntax.SyntaxNode;
using RazorSyntaxToken = Microsoft.AspNetCore.Razor.Language.Syntax.SyntaxToken;
using RazorSyntaxList = Microsoft.AspNetCore.Razor.Language.Syntax.SyntaxList<Microsoft.AspNetCore.Razor.Language.Syntax.SyntaxNode>;
using System.Text;
using System.Diagnostics;

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
        private readonly TagHelperCompletionService _completionService;
        private readonly TagHelperFactsService _tagHelperFactsService;

        [ImportingConstructor]
        public DefaultRazorCompletionFactsService(TagHelperCompletionService completionService, TagHelperFactsService tagHelperFactsService)
        {
            if (completionService is null)
            {
                throw new ArgumentNullException(nameof(completionService));
            }

            if (tagHelperFactsService is null)
            {
                throw new ArgumentNullException(nameof(tagHelperFactsService));
            }

            _completionService = completionService;
            _tagHelperFactsService = tagHelperFactsService;
        }

        public override IReadOnlyList<RazorCompletionItem> GetCompletionItems(RazorCodeDocument codeDocument, SourceSpan location)
        {
            var completionItems = new List<RazorCompletionItem>();
            var syntaxTree = codeDocument.GetSyntaxTree();

            if (AtDirectiveCompletionPoint(syntaxTree, location))
            {
                var directiveCompletions = GetDirectiveCompletionItems(syntaxTree);
                completionItems.AddRange(directiveCompletions);
            }

            if (TryGetDirectiveAttributeCompletions(codeDocument, location, out var directiveAttributeCompletions))
            {
                completionItems.AddRange(directiveAttributeCompletions);
            }

            if (TryGetDirectiveAttributeParameterCompletions(codeDocument, location, out var directiveAttributeParameterCompletions))
            {
                completionItems.AddRange(directiveAttributeParameterCompletions);
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

        internal bool TryGetDirectiveAttributeCompletions(RazorCodeDocument codeDocument, SourceSpan location, out IReadOnlyList<RazorCompletionItem> completions)
        {
            if (codeDocument is null)
            {
                throw new ArgumentNullException(nameof(codeDocument));
            }

            var syntaxTree = codeDocument.GetSyntaxTree();
            if (syntaxTree == null)
            {
                completions = null;
                return false;
            }

            if (!FileKinds.IsComponent(syntaxTree.Options.FileKind))
            {
                // Directive attributes are only supported in components
                completions = null;
                return false;
            }

            var change = new SourceChange(location, string.Empty);
            var owner = syntaxTree.Root.LocateOwner(change);

            if (owner == null)
            {
                completions = null;
                return false;
            }

            if (!TryGetAttributeInfo(owner, out var containingTagNameToken, out var selectedAttributeName, out var attributeNodes))
            {
                completions = null;
                return false;
            }

            var nameString = selectedAttributeName.GetContent();
            var nameSpan = selectedAttributeName.Span;

            var relativeColonIndex = nameString.IndexOf(':');
            if (relativeColonIndex != -1)
            {
                // There's a parameter in the attribute, we need to adjust the name to only be the prefixed portion of the attribute

                var nameStart = selectedAttributeName.Span.Start;
                var absoluteColonIndex = selectedAttributeName.Span.Start + relativeColonIndex;
                nameString = nameString.Substring(0, relativeColonIndex);
                nameSpan = new TextSpan(nameStart, absoluteColonIndex - nameStart);
            }

            if (!nameSpan.IntersectsWith(location.AbsoluteIndex))
            {
                completions = null;
                return false;
            }

            if (!nameString.StartsWith("b"))
            {
                // TODO: Remove once we can properly identify directive attributes in the TryGetSelectedAttribute
                completions = null;
                return false;
            }

            var stringifiedAttributes = StringifyAttributes(attributeNodes);
            completions = GetAttributeCompletions(owner, containingTagNameToken.Content, nameString, stringifiedAttributes, codeDocument);

            return true;
        }

        private IReadOnlyList<RazorCompletionItem> GetAttributeCompletions(
            RazorSyntaxNode containingAttribute,
            string containingTagName,
            string selectedAttributeName,
            IEnumerable<KeyValuePair<string, string>> attributes,
            RazorCodeDocument codeDocument)
        {
            var ancestors = containingAttribute.Parent.Ancestors();
            var tagHelperDocumentContext = codeDocument.GetTagHelperContext();
            var (ancestorTagName, ancestorIsTagHelper) = GetNearestAncestorTagInfo(ancestors);
            var attributeCompletionContext = new AttributeCompletionContext(
                tagHelperDocumentContext,
                existingCompletions: Enumerable.Empty<string>(),
                containingTagName,
                selectedAttributeName,
                attributes,
                ancestorTagName,
                ancestorIsTagHelper,
                inHTMLSchema: null);

            var completionItems = new List<RazorCompletionItem>();
            var completionResult = _completionService.GetAttributeCompletions(attributeCompletionContext);
            foreach (var completion in completionResult.Completions)
            {
                var razorCompletionItem = new RazorCompletionItem(
                    completion.Key,
                    completion.Key,
                    description: string.Empty,
                    RazorCompletionItemKind.TagHelperAttribute);
                razorCompletionItem.SetAssociatedBoundAttributes(completion.Value);

                completionItems.Add(razorCompletionItem);
            }

            return completionItems;
        }

        internal bool TryGetDirectiveAttributeParameterCompletions(RazorCodeDocument codeDocument, SourceSpan location, out IReadOnlyList<RazorCompletionItem> completions)
        {
            if (codeDocument is null)
            {
                throw new ArgumentNullException(nameof(codeDocument));
            }

            var syntaxTree = codeDocument.GetSyntaxTree();
            if (syntaxTree == null)
            {
                completions = null;
                return false;
            }

            if (!FileKinds.IsComponent(syntaxTree.Options.FileKind))
            {
                // Directive attributes are only supported in components
                completions = null;
                return false;
            }

            var change = new SourceChange(location, string.Empty);
            var owner = syntaxTree.Root.LocateOwner(change);

            if (owner == null)
            {
                completions = null;
                return false;
            }

            if (!TryGetAttributeInfo(owner, out var containingTagNameToken, out var selectedAttributeName, out var attributeNodes))
            {
                completions = null;
                return false;
            }

            var fullNameString = selectedAttributeName.GetContent();

            var relativeColonIndex = fullNameString.IndexOf(':');
            if (relativeColonIndex == -1)
            {
                // There's no parameter portion to the current attribute
                completions = null;
                return false;
            }

            // There's a parameter in the attribute, we need to adjust the name to only be the prefixed portion of the attribute

            var afterColonRelativeIndex = relativeColonIndex + 1;
            var afterColonAbsoluteIndex = selectedAttributeName.Span.Start + afterColonRelativeIndex;
            var parameterSpan = new TextSpan(afterColonAbsoluteIndex, selectedAttributeName.EndPosition - afterColonAbsoluteIndex);

            if (!parameterSpan.IntersectsWith(location.AbsoluteIndex))
            {
                completions = null;
                return false;
            }

            if (!fullNameString.StartsWith("b"))
            {
                // TODO: Remove once we can properly identify directive attributes in the TryGetSelectedAttribute
                completions = null;
                return false;
            }

            var stringifiedAttributes = StringifyAttributes(attributeNodes);
            completions = GetAttributeParameterCompletions(owner, containingTagNameToken.Content, fullNameString, stringifiedAttributes, codeDocument);

            return true;
        }

        private IReadOnlyList<RazorCompletionItem> GetAttributeParameterCompletions(
            RazorSyntaxNode containingAttribute,
            string containingTagName,
            string selectedAttributeName,
            IEnumerable<KeyValuePair<string, string>> attributes,
            RazorCodeDocument codeDocument)
        {
            var ancestors = containingAttribute.Parent.Ancestors();
            var tagHelperDocumentContext = codeDocument.GetTagHelperContext();
            var (ancestorTagName, _) = GetNearestAncestorTagInfo(ancestors);

            if (!TagHelperMatchingConventions.TryGetBoundAttributeParameter(selectedAttributeName, out var boundAttributeName, out _))
            {
                return Array.Empty<RazorCompletionItem>();
            }

            var descriptorsForTag = _tagHelperFactsService.GetTagHelpersGivenTag(tagHelperDocumentContext, containingTagName, ancestorTagName);
            if (descriptorsForTag.Count == 0)
            {
                // If the current tag has no possible descriptors then we can't have any additional attributes.
                return Array.Empty<RazorCompletionItem>();
            }

            // Attribute parameters are case sensitive when matching
            var attributeCompletions = new Dictionary<string, Dictionary<BoundAttributeParameterDescriptor, TagHelperDescriptor>>(StringComparer.Ordinal);

            foreach (var descriptor in descriptorsForTag)
            {
                for (var i = 0; i < descriptor.BoundAttributes.Count; i++)
                {
                    var attributeDescriptor = descriptor.BoundAttributes[i];
                    var boundAttributeParameters = attributeDescriptor.BoundAttributeParameters;
                    if (boundAttributeParameters.Count == 0)
                    {
                        continue;
                    }

                    if (TagHelperMatchingConventions.CanSatisfyBoundAttribute(boundAttributeName, attributeDescriptor))
                    {
                        var possibleParameters = boundAttributeParameters.ToList();
                        for (var j = possibleParameters.Count - 1; j >= 0; j--)
                        {
                            var parameterDescriptor = possibleParameters[j];

                            if (attributes.Any(kvp => TagHelperMatchingConventions.SatisfiesBoundAttributeWithParameter(kvp.Key, attributeDescriptor, parameterDescriptor)))
                            {
                                // There's already an existing attribute that satisfies this parameter, don't show it in the completion list.
                                continue;
                            }

                            if (!attributeCompletions.TryGetValue(parameterDescriptor.Name, out var parameterMappings))
                            {
                                parameterMappings = new Dictionary<BoundAttributeParameterDescriptor, TagHelperDescriptor>();
                                attributeCompletions[parameterDescriptor.Name] = parameterMappings;
                            }

                            parameterMappings[parameterDescriptor] = descriptor;
                        }
                    }
                }
            }

            var completionItems = new List<RazorCompletionItem>();
            foreach (var completion in attributeCompletions)
            {
                var razorCompletionItem = new RazorCompletionItem(
                    completion.Key,
                    completion.Key,
                    description: string.Empty,
                    RazorCompletionItemKind.TagHelperAttributeParameter);
                razorCompletionItem.SetAssociatedBoundAttributeParameters(completion.Value);

                completionItems.Add(razorCompletionItem);
            }

            return completionItems;
        }

        // Internal for testing
        internal static (string ancestorTagName, bool ancestorIsTagHelper) GetNearestAncestorTagInfo(IEnumerable<RazorSyntaxNode> ancestors)
        {
            foreach (var ancestor in ancestors)
            {
                if (ancestor is MarkupElementSyntax element)
                {
                    // It's possible for start tag to be null in malformed cases.
                    var name = element.StartTag?.Name?.Content ?? string.Empty;
                    return (name, ancestorIsTagHelper: false);
                }
                else if (ancestor is MarkupTagHelperElementSyntax tagHelperElement)
                {
                    // It's possible for start tag to be null in malformed cases.
                    var name = tagHelperElement.StartTag?.Name?.Content ?? string.Empty;
                    return (name, ancestorIsTagHelper: true);
                }
            }

            return (ancestorTagName: null, ancestorIsTagHelper: false);
        }

        // Internal for testing
        internal static bool IsDirectiveCompletableToken(AspNetCore.Razor.Language.Syntax.SyntaxToken token)
        {
            return token.Kind == SyntaxKind.Identifier ||
                // Marker symbol
                token.Kind == SyntaxKind.Marker;
        }

        // Internal for testing
        internal static IEnumerable<KeyValuePair<string, string>> StringifyAttributes(RazorSyntaxList attributes)
        {
            var stringifiedAttributes = new List<KeyValuePair<string, string>>();

            for (var i = 0; i < attributes.Count; i++)
            {
                var attribute = attributes[i];
                if (attribute is MarkupTagHelperAttributeSyntax tagHelperAttribute)
                {
                    var name = tagHelperAttribute.Name.GetContent();
                    var value = tagHelperAttribute.Value?.GetContent() ?? string.Empty;
                    stringifiedAttributes.Add(new KeyValuePair<string, string>(name, value));
                }
                else if (attribute is MarkupMinimizedTagHelperAttributeSyntax minimizedTagHelperAttribute)
                {
                    var name = minimizedTagHelperAttribute.Name.GetContent();
                    stringifiedAttributes.Add(new KeyValuePair<string, string>(name, string.Empty));
                }
                else if (attribute is MarkupAttributeBlockSyntax markupAttribute)
                {
                    var name = markupAttribute.Name.GetContent();
                    var value = markupAttribute.Value?.GetContent() ?? string.Empty;
                    stringifiedAttributes.Add(new KeyValuePair<string, string>(name, value));
                }
                else if (attribute is MarkupMinimizedAttributeBlockSyntax minimizedMarkupAttribute)
                {
                    var name = minimizedMarkupAttribute.Name.GetContent();
                    stringifiedAttributes.Add(new KeyValuePair<string, string>(name, string.Empty));
                }
            }

            return stringifiedAttributes;
        }

        private static bool TryGetSelectedAttribute(RazorSyntaxNode attribute, out MarkupTextLiteralSyntax selectedAttributeName)
        {
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

        private static bool TryGetAttributeInfo(RazorSyntaxNode leafNode, out RazorSyntaxToken containingTagNameToken, out MarkupTextLiteralSyntax selectedAttributeNameSyntax, out RazorSyntaxList attributeNodes)
        {
            var attribute = leafNode.Parent;
            if ((attribute is MarkupMiscAttributeContentSyntax ||
                attribute is MarkupMinimizedAttributeBlockSyntax ||
                attribute is MarkupAttributeBlockSyntax ||
                attribute is MarkupTagHelperAttributeSyntax ||
                attribute is MarkupMinimizedTagHelperAttributeSyntax) &&
                TryGetSelectedAttribute(attribute, out selectedAttributeNameSyntax) &&
                TryGetElementInfo(attribute.Parent, out containingTagNameToken, out attributeNodes))
            {
                return true;
            }

            containingTagNameToken = null;
            selectedAttributeNameSyntax = null;
            attributeNodes = default;
            return false;
        }

        private static bool TryGetElementInfo(RazorSyntaxNode element, out RazorSyntaxToken containingTagNameToken, out RazorSyntaxList attributeNodes)
        {
            if (element is MarkupStartTagSyntax startTag)
            {
                containingTagNameToken = startTag.Name;
                attributeNodes = startTag.Attributes;
                return true;
            }

            if (element is MarkupTagHelperStartTagSyntax startTagHelper)
            {
                containingTagNameToken = startTagHelper.Name;
                attributeNodes = startTagHelper.Attributes;
                return true;
            }

            containingTagNameToken = null;
            attributeNodes = default;
            return false;
        }
    }
}
