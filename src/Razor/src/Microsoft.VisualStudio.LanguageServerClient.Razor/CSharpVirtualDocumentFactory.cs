// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using Microsoft.CodeAnalysis.Razor;
using Microsoft.CodeAnalysis.Razor.ProjectSystem;
using Microsoft.CodeAnalysis.Razor.Workspaces;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.VisualStudio.LanguageServerClient.Razor
{
    [Shared]
    [Export(typeof(VirtualDocumentFactory))]
    internal class CSharpVirtualDocumentFactory : VirtualDocumentFactory
    {
        // Internal for testing
        internal const string CSharpLSPContentTypeName = "C#_LSP";
        internal const string VirtualCSharpFileNameSuffix = "__virtual.cs";

        private readonly IContentTypeRegistryService _contentTypeRegistry;
        private readonly ITextBufferFactoryService _textBufferFactory;
        private readonly ITextDocumentFactoryService _textDocumentFactory;
        private readonly FileUriProvider _fileUriProvider;
        private readonly ILSPDocumentFileInfoProvider _documentFileInfoProvider;
        private IContentType _csharpLSPContentType;

        [ImportingConstructor]
        public CSharpVirtualDocumentFactory(
            IContentTypeRegistryService contentTypeRegistry,
            ITextBufferFactoryService textBufferFactory,
            ITextDocumentFactoryService textDocumentFactory,
            FileUriProvider fileUriProvider,
            ILSPDocumentFileInfoProvider documentFileInfoProvider)
        {
            if (contentTypeRegistry is null)
            {
                throw new ArgumentNullException(nameof(contentTypeRegistry));
            }

            if (textBufferFactory is null)
            {
                throw new ArgumentNullException(nameof(textBufferFactory));
            }

            if (textDocumentFactory is null)
            {
                throw new ArgumentNullException(nameof(textDocumentFactory));
            }

            if (fileUriProvider is null)
            {
                throw new ArgumentNullException(nameof(fileUriProvider));
            }

            if (documentFileInfoProvider is null)
            {
                throw new ArgumentNullException(nameof(documentFileInfoProvider));
            }

            _contentTypeRegistry = contentTypeRegistry;
            _textBufferFactory = textBufferFactory;
            _textDocumentFactory = textDocumentFactory;
            _fileUriProvider = fileUriProvider;
            _documentFileInfoProvider = documentFileInfoProvider;
        }

        private IContentType CSharpLSPContentType
        {
            get
            {
                if (_csharpLSPContentType == null)
                {
                    var registeredContentType = _contentTypeRegistry.GetContentType(CSharpLSPContentTypeName);
                    _csharpLSPContentType = new RemoteContentDefinitionType(registeredContentType);
                }

                return _csharpLSPContentType;
            }
        }

        public override bool TryCreateFor(ITextBuffer hostDocumentBuffer, out VirtualDocument virtualDocument)
        {
            if (hostDocumentBuffer is null)
            {
                throw new ArgumentNullException(nameof(hostDocumentBuffer));
            }

            if (!hostDocumentBuffer.ContentType.IsOfType(RazorLSPContentTypeDefinition.Name))
            {
                // Another content type we don't care about.
                virtualDocument = null;
                return false;
            }

            var hostDocumentUri = _fileUriProvider.GetOrCreate(hostDocumentBuffer);

            // Index.cshtml => Index.cshtml__virtual.cs
            var virtualCSharpFilePath = hostDocumentUri.GetAbsoluteOrUNCPath() + VirtualCSharpFileNameSuffix;
            var virtualCSharpUri = new Uri(virtualCSharpFilePath);


            var csharpBuffer = _textBufferFactory.CreateTextBuffer();
            csharpBuffer.Properties.AddProperty("ContainedLanguageMarker", true);
            csharpBuffer.Properties.AddProperty(LanguageClientConstants.ClientNamePropertyKey, "RazorCSharp");

            var textDocument = _textDocumentFactory.CreateTextDocument(csharpBuffer, virtualCSharpFilePath);
            csharpBuffer.ChangeContentType(CSharpLSPContentType, editTag: null);
            virtualDocument = new CSharpVirtualDocument(virtualCSharpUri, csharpBuffer, hostDocumentUri, _documentFileInfoProvider);
            return true;
        }

        private class RemoteContentDefinitionType : IContentType
        {
            private static readonly IReadOnlyList<string> ExtendedBaseContentTypes = new[]
            {
                "code-languageserver-base",
                CodeRemoteContentDefinition.CodeRemoteContentTypeName
            };

            private readonly IContentType _innerContentType;

            internal RemoteContentDefinitionType(IContentType innerContentType)
            {
                if (innerContentType is null)
                {
                    throw new ArgumentNullException(nameof(innerContentType));
                }

                _innerContentType = innerContentType;
                TypeName = innerContentType.TypeName;
                DisplayName = innerContentType.DisplayName;
            }

            public string TypeName { get; }

            public string DisplayName { get; }

            public IEnumerable<IContentType> BaseTypes => _innerContentType.BaseTypes;

            public bool IsOfType(string type)
            {
                return ExtendedBaseContentTypes.Contains(type) || _innerContentType.IsOfType(type);
            }
        }
    }
}
