// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Razor;
using Microsoft.CodeAnalysis.Razor.ProjectSystem;
using Microsoft.VisualStudio.Editor.Razor.Documents;
using Microsoft.VisualStudio.LanguageServer.ContainedLanguage;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.LiveShare;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Projection;
using Microsoft.VisualStudio.Utilities;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.VisualStudio.LanguageServerClient.Razor
{
    [ExportCollaborationService(typeof(HostInitializationService), Scope = SessionScope.Host)]
    internal sealed class HostInitializationService : ICollaborationServiceFactory
    {
        private readonly EditorDocumentManager _editorDocumentManager;
        private readonly ForegroundDispatcher _foregroundDispatcher;
        private readonly IContentTypeRegistryService _contentTypeRegistry;
        private readonly IBufferGraphFactoryService _bufferGraphFactoryService;
        private readonly DefaultProjectSnapshotManager _projectSnapshotManager;
        private IContentType _razorLSPContentType;

        [ImportingConstructor]
        public HostInitializationService(
            ForegroundDispatcher foregroundDispatcher,
            IContentTypeRegistryService contentTypeRegistry,
            IBufferGraphFactoryService bufferGraphFactoryService,
            [Import(typeof(VisualStudioWorkspace))] CodeAnalysis.Workspace defaultWorkspace)
        {
            _foregroundDispatcher = foregroundDispatcher;
            _contentTypeRegistry = contentTypeRegistry;
            _bufferGraphFactoryService = bufferGraphFactoryService;
            var razorLanguageServices = defaultWorkspace.Services.GetLanguageServices(RazorLanguage.Name);
            _projectSnapshotManager = (DefaultProjectSnapshotManager)razorLanguageServices.GetRequiredService<ProjectSnapshotManager>();
            _editorDocumentManager = defaultWorkspace.Services.GetRequiredService<EditorDocumentManager>();
        }

        public IContentType RazorLSPContentType
        {
            get
            {
                if (_razorLSPContentType == null)
                {
                    _razorLSPContentType = _contentTypeRegistry.GetContentType(RazorLSPConstants.RazorLSPContentTypeName);
                }

                return _razorLSPContentType;
            }
        }

        public async Task<ICollaborationService> CreateServiceAsync(CollaborationSession session, CancellationToken cancellationToken)
        {
            // This is something totally unexpected, let's just send it over to the workspace.
            await Task.Factory.StartNew(
                (p) =>
                {
                    var projectSnapshotManager = (DefaultProjectSnapshotManager)p;

                    foreach (var filePath in projectSnapshotManager.OpenDocuments)
                    {
                        if (!_editorDocumentManager.TryGetMatchingDocuments(filePath, out var editorDocuments))
                        {
                            continue;
                        }


                        for (var i = 0; i < editorDocuments.Length; i++)
                        {
                            var textBuffer = editorDocuments[i].EditorTextBuffer;

                            if (!textBuffer.ContentType.IsOfType(RazorLSPConstants.RazorLSPContentTypeName))
                            {
                                var bufferGraph = _bufferGraphFactoryService.CreateBufferGraph(textBuffer);

                                foreach (var buffer in bufferGraph.GetTextBuffers(buffer => true))
                                {
                                    textBuffer.ChangeContentType(InertContentType.Instance, editTag: null);
                                }

                                textBuffer.ChangeContentType(RazorLSPContentType, editTag: null);
                            }
                        }
                    }
                },
                _projectSnapshotManager,
                CancellationToken.None,
                TaskCreationOptions.None,
                _foregroundDispatcher.ForegroundScheduler).ConfigureAwait(false);

            return new EmptyService();
        }

        private class EmptyService : ICollaborationService
        {
        }
    }
}
