// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Razor.Workspaces;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;

namespace Microsoft.VisualStudio.LanguageServerClient.Razor
{
    public class CSharpOutputContainer : ICSharpOutputContainer
    {
        private readonly ITextSnapshot _textSnapshot;

        public CSharpOutputContainer(ITextSnapshot textSnapshot)
        {
            if (textSnapshot is null)
            {
                throw new ArgumentNullException(nameof(textSnapshot));
            }

            _textSnapshot = textSnapshot;
        }

        public TextLoader CreateGeneratedTextLoader(string filePath)
        {
            var sourceText = _textSnapshot.AsText();
            var textLoader = new SourceTextLoader(sourceText, filePath);
            return textLoader;
        }
    }
}
