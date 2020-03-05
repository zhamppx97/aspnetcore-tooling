// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.VisualStudio.LanguageServerClient.Razor
{
    public sealed class SourceTextLoader : TextLoader
    {
        private readonly SourceText _sourceText;
        private readonly string _filePath;

        public SourceTextLoader(SourceText sourceText, string filePath)
        {
            if (sourceText is null)
            {
                throw new ArgumentNullException(nameof(sourceText));
            }

            if (filePath is null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            _sourceText = sourceText;
            _filePath = filePath;
        }

        public override Task<TextAndVersion> LoadTextAndVersionAsync(Workspace workspace, DocumentId documentId, CancellationToken cancellationToken)
        {
            return Task.FromResult(TextAndVersion.Create(_sourceText, VersionStamp.Create(), _filePath));
        }
    }
}
