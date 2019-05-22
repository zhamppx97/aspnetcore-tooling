// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Razor.Language;

namespace Microsoft.CodeAnalysis.Razor
{
    internal static class RazorCompletionItemExtensions
    {
        private readonly static object BoundAttributesKey = new object();
        private readonly static object BoundAttributeParametersKey = new object();

        public static void SetAssociatedBoundAttributeParameters(this RazorCompletionItem completionItem, IReadOnlyDictionary<BoundAttributeParameterDescriptor, TagHelperDescriptor> boundAttributeMappings)
        {
            if (completionItem is null)
            {
                throw new ArgumentNullException(nameof(completionItem));
            }

            completionItem.Items[BoundAttributeParametersKey] = boundAttributeMappings;
        }

        public static bool TryGetAssociatedBoundAttributeParameters(this RazorCompletionItem completionItem, out IReadOnlyDictionary<BoundAttributeParameterDescriptor, TagHelperDescriptor> boundAttributeMappings)
        {
            if (completionItem is null)
            {
                throw new ArgumentNullException(nameof(completionItem));
            }

            boundAttributeMappings = completionItem.Items[BoundAttributeParametersKey] as IReadOnlyDictionary<BoundAttributeParameterDescriptor, TagHelperDescriptor>;

            return boundAttributeMappings != null;
        }

        public static void SetAssociatedBoundAttributes(this RazorCompletionItem completionItem, IEnumerable<BoundAttributeDescriptor> boundAttributes)
        {
            if (completionItem is null)
            {
                throw new ArgumentNullException(nameof(completionItem));
            }

            completionItem.Items[BoundAttributesKey] = boundAttributes;
        }

        public static bool TryGetAssociatedBoundAttributes(this RazorCompletionItem completionItem, out IEnumerable<BoundAttributeDescriptor> boundAttributes)
        {
            if (completionItem is null)
            {
                throw new ArgumentNullException(nameof(completionItem));
            }

            boundAttributes = completionItem.Items[BoundAttributesKey] as IEnumerable<BoundAttributeDescriptor>;

            return boundAttributes != null;
        }
    }
}
