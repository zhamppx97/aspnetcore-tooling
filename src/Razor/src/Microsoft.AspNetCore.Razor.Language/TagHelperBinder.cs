// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.AspNetCore.Razor.Language
{
    /// <summary>
    /// Enables retrieval of <see cref="TagHelperBinding"/>'s.
    /// </summary>
    internal class TagHelperBinder
    {
        private IDictionary<string, HashSet<TagHelperDescriptor>> _registrationsWithPrefix;
        private IDictionary<string, HashSet<TagHelperDescriptor>> _registrationsWithoutPrefix;
        private readonly string _tagHelperPrefix;

        /// <summary>
        /// Instantiates a new instance of the <see cref="TagHelperBinder"/>.
        /// </summary>
        /// <param name="tagHelperPrefix">The tag helper prefix being used by the document.</param>
        /// <param name="descriptors">The descriptors that the <see cref="TagHelperBinder"/> will pull from.</param>
        public TagHelperBinder(string tagHelperPrefix, IEnumerable<TagHelperDescriptor> descriptors)
        {
            _tagHelperPrefix = tagHelperPrefix;

            _registrationsWithPrefix = new Dictionary<string, HashSet<TagHelperDescriptor>>(StringComparer.OrdinalIgnoreCase);
            _registrationsWithoutPrefix = new Dictionary<string, HashSet<TagHelperDescriptor>>(StringComparer.OrdinalIgnoreCase);

            // Populate our registrations
            foreach (var descriptor in descriptors)
            {
                Register(descriptor);
            }
        }

        /// <summary>
        /// Gets all tag helpers that match the given HTML tag criteria.
        /// </summary>
        /// <param name="tagName">The name of the HTML tag to match. Providing a '*' tag name
        /// retrieves catch-all <see cref="TagHelperDescriptor"/>s (descriptors that target every tag).</param>
        /// <param name="attributes">Attributes on the HTML tag.</param>
        /// <param name="parentTagName">The parent tag name of the given <paramref name="tagName"/> tag.</param>
        /// <param name="parentIsTagHelper">Is the parent tag of the given <paramref name="tagName"/> tag a tag helper.</param>
        /// <returns><see cref="TagHelperDescriptor"/>s that apply to the given HTML tag criteria.
        /// Will return <c>null</c> if no <see cref="TagHelperDescriptor"/>s are a match.</returns>
        public TagHelperBinding GetBinding(
            string tagName,
            IReadOnlyList<KeyValuePair<string, string>> attributes,
            string parentTagName,
            bool parentIsTagHelper)
        {
            if (tagName == null)
            {
                throw new ArgumentNullException(nameof(tagName));
            }

            if (attributes == null)
            {
                throw new ArgumentNullException(nameof(attributes));
            }

            var descriptors = new HashSet<TagHelperDescriptor>(TagHelperDescriptorComparer.Default);

            // Matches for tag name (ignoring the prefix)
            if (_registrationsWithoutPrefix.TryGetValue(tagName, out var matches))
            {
                descriptors.UnionWith(matches);
            }

            // Matches for tag name (ignoring the prefix)
            if (_tagHelperPrefix != null && _registrationsWithPrefix.TryGetValue(tagName, out matches))
            {
                descriptors.UnionWith(matches);
            }

            // Matches for catch all (ignoring the prefix)
            if (_registrationsWithoutPrefix.TryGetValue(TagHelperMatchingConventions.ElementCatchAllName, out matches))
            {
                descriptors.UnionWith(matches);
            }

            // Matches for catch all (with prefix)
            if (_tagHelperPrefix != null && _registrationsWithPrefix.TryGetValue(_tagHelperPrefix + TagHelperMatchingConventions.ElementCatchAllName, out matches))
            {
                descriptors.UnionWith(matches);
            }


            string tagNameWithoutPrefix = null;
            if (_tagHelperPrefix != null && 
                tagName.StartsWith(_tagHelperPrefix) && 
                tagName.Length > _tagHelperPrefix.Length)
            {
                tagNameWithoutPrefix = tagName.Substring(_tagHelperPrefix.Length);
            }

            string parentTagNameWithoutPrefix = null;
            if (_tagHelperPrefix != null && 
                parentIsTagHelper && 
                parentTagName.StartsWith(_tagHelperPrefix) && 
                parentTagName.Length > _tagHelperPrefix.Length)
            {
                parentTagNameWithoutPrefix = parentTagName.Substring(_tagHelperPrefix.Length);
            }

            Dictionary<TagHelperDescriptor, IReadOnlyList<TagMatchingRuleDescriptor>> applicableDescriptorMappings = null;
            foreach (var descriptor in descriptors)
            {
                string tagNameForComparison;
                string parentTagNameForComparison;
                if (descriptor.IgnoresTagHelperPrefix() || _tagHelperPrefix == null)
                {
                    tagNameForComparison = tagName;
                    parentTagNameForComparison = parentTagName;
                }
                else if (_tagHelperPrefix != null && tagNameWithoutPrefix == null)
                {
                    // This tag helper needs a prefix but this tag doesn't begin with it.
                    continue;
                }
                else
                {
                    tagNameForComparison = tagNameWithoutPrefix;
                    parentTagNameForComparison = parentTagNameWithoutPrefix;
                }

                var applicableRules = descriptor.TagMatchingRules.Where(rule =>
                {
                    return TagHelperMatchingConventions.SatisfiesRule(tagNameForComparison, parentTagNameForComparison, attributes, rule);
                });

                if (applicableRules.Any())
                {
                    if (applicableDescriptorMappings == null)
                    {
                        applicableDescriptorMappings = new Dictionary<TagHelperDescriptor, IReadOnlyList<TagMatchingRuleDescriptor>>();
                    }

                    applicableDescriptorMappings[descriptor] = applicableRules.ToList();
                }
            }

            if (applicableDescriptorMappings == null)
            {
                return null;
            }

            var tagHelperBinding = new TagHelperBinding(
                tagName,
                attributes,
                parentTagName,
                applicableDescriptorMappings,
                _tagHelperPrefix);

            return tagHelperBinding;
        }

        private void Register(TagHelperDescriptor descriptor)
        {
            for (var i = 0; i < descriptor.TagMatchingRules.Count; i++)
            {
                var rule = descriptor.TagMatchingRules[i];

                HashSet<TagHelperDescriptor> set;
                if (_tagHelperPrefix == null || descriptor.IgnoresTagHelperPrefix())
                {
                    var key = rule.TagName;
                    if (!_registrationsWithoutPrefix.TryGetValue(key, out set))
                    {
                        set = new HashSet<TagHelperDescriptor>(TagHelperDescriptorComparer.Default);
                        _registrationsWithoutPrefix[key] = set;
                    }
                }
                else
                {
                    var key = _tagHelperPrefix + rule.TagName;
                    if (!_registrationsWithPrefix.TryGetValue(key, out set))
                    {
                        set = new HashSet<TagHelperDescriptor>(TagHelperDescriptorComparer.Default);
                        _registrationsWithPrefix[key] = set;
                    }
                }
                set.Add(descriptor);
            }
        }
    }
}