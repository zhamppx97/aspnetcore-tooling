// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Microsoft.CodeAnalysis.Razor.Workspaces;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;

namespace Microsoft.VisualStudio.LanguageServerClient.Razor
{
    internal class CSharpVirtualDocument : VirtualDocument
    {
        private readonly Uri _parentDocumentUri;
        private readonly ILSPDocumentFileInfoProvider _documentFileInfoProvider;
        private long? _hostDocumentSyncVersion;
        private CSharpVirtualDocumentSnapshot _currentSnapshot;

        public CSharpVirtualDocument(
            Uri uri,
            ITextBuffer textBuffer,
            Uri parentDocumentUri,
            ILSPDocumentFileInfoProvider documentFileInfoProvider)
        {
            if (uri is null)
            {
                throw new ArgumentNullException(nameof(uri));
            }

            if (textBuffer is null)
            {
                throw new ArgumentNullException(nameof(textBuffer));
            }

            if (parentDocumentUri is null)
            {
                throw new ArgumentNullException(nameof(parentDocumentUri));
            }

            if (documentFileInfoProvider is null)
            {
                throw new ArgumentNullException(nameof(documentFileInfoProvider));
            }

            Uri = uri;
            TextBuffer = textBuffer;
            _parentDocumentUri = parentDocumentUri;
            _documentFileInfoProvider = documentFileInfoProvider;
            _currentSnapshot = UpdateSnapshot();
        }

        public override Uri Uri { get; }

        public override long? HostDocumentSyncVersion => _hostDocumentSyncVersion;

        public override ITextBuffer TextBuffer { get; }

        public override VirtualDocumentSnapshot CurrentSnapshot => _currentSnapshot;

        public override VirtualDocumentSnapshot Update(IReadOnlyList<TextChange> changes, long hostDocumentVersion)
        {
            if (changes is null)
            {
                throw new ArgumentNullException(nameof(changes));
            }

            _hostDocumentSyncVersion = hostDocumentVersion;

            if (changes.Count == 0)
            {
                _currentSnapshot = UpdateSnapshot();
                return _currentSnapshot;
            }

            var edit = TextBuffer.CreateEdit();
            for (var i = 0; i < changes.Count; i++)
            {
                var change = changes[i];

                if (change.IsDelete())
                {
                    edit.Delete(change.Span.Start, change.Span.Length);
                }
                else if (change.IsReplace())
                {
                    edit.Replace(change.Span.Start, change.Span.Length, change.NewText);
                }
                else if (change.IsInsert())
                {
                    edit.Insert(change.Span.Start, change.NewText);
                }
                else
                {
                    throw new InvalidOperationException("Unknown edit type when updating LSP C# buffer.");
                }
            }

            edit.Apply();
            _currentSnapshot = UpdateSnapshot();
            var csharpOutputContainer = new CSharpOutputContainer(_currentSnapshot.Snapshot);
            _documentFileInfoProvider.UpdateFileInfo(_parentDocumentUri, csharpOutputContainer);

            return _currentSnapshot;
        }

        private CSharpVirtualDocumentSnapshot UpdateSnapshot() => new CSharpVirtualDocumentSnapshot(Uri, TextBuffer.CurrentSnapshot, HostDocumentSyncVersion);
    }
}
