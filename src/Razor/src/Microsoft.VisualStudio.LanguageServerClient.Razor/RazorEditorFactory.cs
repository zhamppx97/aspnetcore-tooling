// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.ComponentModel.Composition;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis.Razor;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Package;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.VisualStudio.LanguageServerClient.Razor
{
    [Export(typeof(IFilePathToContentTypeProvider))]
    [Name(nameof(RazorLanguageServerFilePathToContentTypeProvider))]
    [FileExtension(".cshtml")]
    internal class RazorLanguageServerFilePathToContentTypeProvider : IFilePathToContentTypeProvider
    {
        private readonly IContentTypeRegistryService _contentTypeRegistryService;
        private readonly LSPEditorFeatureDetector _lspEditorFeatureDetector;

        [ImportingConstructor]
        public RazorLanguageServerFilePathToContentTypeProvider(IContentTypeRegistryService contentTypeRegistryService, LSPEditorFeatureDetector lspEditorFeatureDetector)
        {
            _contentTypeRegistryService = contentTypeRegistryService;
            _lspEditorFeatureDetector = lspEditorFeatureDetector;
        }

        public bool TryGetContentTypeForFilePath(string filePath, out IContentType contentType)
        {
            if (_lspEditorFeatureDetector.IsLSPEditorAvailable(filePath, hierarchy: null))
            {
                contentType = _contentTypeRegistryService.GetContentType(RazorLSPContentTypeDefinition.Name);
                return true;
            }

            contentType = null;
            return false;
        }
    }

    [Guid(EditorFactoryGuidString)]
    internal class RazorEditorFactory : EditorFactory
    {
        private const string EditorFactoryGuidString = "3dfdce9e-1799-4372-8aa6-d8e65182fdfc";

        public RazorEditorFactory(AsyncPackage package) : base(package)
        {
        }

        public override int CreateEditorInstance(
            uint createDocFlags,
            string moniker,
            string physicalView,
            IVsHierarchy hierarchy,
            uint itemid,
            IntPtr existingDocData,
            out IntPtr docView,
            out IntPtr docData,
            out string editorCaption,
            out Guid cmdUI,
            out int cancelled)
        {
            docView = default;
            docData = default;
            editorCaption = null;
            cmdUI = default;
            cancelled = 0;

            // Razor LSP is not enabled, allow another editor to handle this document
            return VSConstants.VS_E_UNSUPPORTEDFORMAT;
        }
    }
}
