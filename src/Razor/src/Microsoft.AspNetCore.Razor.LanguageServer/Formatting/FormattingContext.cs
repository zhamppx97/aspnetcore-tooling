// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.LanguageServer.Common;
using Microsoft.CodeAnalysis.Text;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Microsoft.AspNetCore.Razor.LanguageServer.Formatting
{
    internal class FormattingContext
    {
        public Uri Uri { get; set; }

        public RazorCodeDocument CodeDocument { get; set; }

        public SourceText SourceText => CodeDocument?.GetSourceText();

        public FormattingOptions Options { get; set; }

        public Range Range { get; set; }

        public Dictionary<int, IndentationContext> Indentations { get; } = new Dictionary<int, IndentationContext>();

        public string GetIndentationLevelString(int indentationLevel)
        {
            var indentChar = Options.InsertSpaces ? ' ' : '\t';
            var indentationLength = indentationLevel;
            if (Options.InsertSpaces)
            {
                indentationLength *= (int)Options.TabSize;
            }
            var indentation = new string(indentChar, indentationLength);
            return indentation;
        }

        public string GetIndentationString(int indentation)
        {
            if (Options.InsertSpaces)
            {
                return new string(' ', indentation);
            }
            else
            {
                // Avoid writing directly to the StringBuilder here, that will throw off the manual indexing 
                // done by the base class.
                var tabs = indentation / Options.TabSize;
                var tabPrefix = new string('\t', (int)tabs);

                var spaces = indentation % Options.TabSize;
                var spaceSuffix = new string(' ', (int)spaces);

                var combined = string.Concat(tabPrefix, spaceSuffix);
                return combined;
            }
        }
    }
}
