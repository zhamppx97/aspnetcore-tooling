// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.LanguageServer.Common;
using Microsoft.AspNetCore.Razor.OmniSharpPlugin.StrongNamed;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using OmniSharp;

namespace Microsoft.AspNetCore.Razor.OmniSharpPlugin
{
    [Shared]
    [Export(typeof(OmniSharpDocumentProcessedListener))]
    internal class BackgroundDocumentProcessedPublisher : OmniSharpDocumentProcessedListener
    {
        // File paths need to align with the file path that's used to create virutal document buffers in the RazorDocumentFactory.ts.
        // The purpose of the alignment is to ensure that when a Razor virtual C# buffer opens we can properly detect its existence.
        private const string EditorVirtualDocumentSuffix = "__virtual.cs";
        internal const string BackgroundVirtualDocumentSuffix = "__bg" + EditorVirtualDocumentSuffix;

        private readonly OmniSharpForegroundDispatcher _foregroundDispatcher;
        private readonly OmniSharpWorkspace _workspace;
        private ILogger _logger;
        private OmniSharpProjectSnapshotManager _projectManager;

        [ImportingConstructor]
        public BackgroundDocumentProcessedPublisher(
            OmniSharpForegroundDispatcher foregroundDispatcher,
            OmniSharpWorkspace workspace,
            ILoggerFactory loggerFactory)
        {
            if (foregroundDispatcher is null)
            {
                throw new ArgumentNullException(nameof(foregroundDispatcher));
            }

            if (workspace is null)
            {
                throw new ArgumentNullException(nameof(workspace));
            }

            if (loggerFactory is null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _foregroundDispatcher = foregroundDispatcher;
            _workspace = workspace;
            _logger = loggerFactory.CreateLogger<BackgroundDocumentProcessedPublisher>();

            _workspace.WorkspaceChanged += Workspace_WorkspaceChanged;
        }

        public override void DocumentProcessed(OmniSharpProjectSnapshot project, OmniSharpDocumentSnapshot document)
        {
            _foregroundDispatcher.AssertForegroundThread();

            if (!FileKinds.IsComponent(document.FileKind) || FileKinds.IsComponentImport(document.FileKind))
            {
                return;
            }

            var openVirtualDocumentFilePath = document.FilePath + EditorVirtualDocumentSuffix;
            var openDocument = _workspace.GetDocument(openVirtualDocumentFilePath);
            if (openDocument != null)
            {
                // This document is open in the editor, no reason for us to populate anything in the workspace the editor will do that.
                return;
            }

            var virtualDocumentFilePath = document.FilePath + BackgroundVirtualDocumentSuffix;
            var currentDocument = _workspace.GetDocument(virtualDocumentFilePath);
            if (currentDocument == null)
            {
                // Background document doesn't exist, we need to create it

                var roslynProject = GetRoslynProject(project);
                if (roslynProject == null)
                {
                    // There's no Roslyn project associated with the Razor document.
                    _logger.LogTrace($"Could not find a Roslyn project for Razor virtual document '{virtualDocumentFilePath}'.");
                    return;
                }

                var documentId = DocumentId.CreateNewId(roslynProject.Id);
                var name = Path.GetFileName(virtualDocumentFilePath);
                var emptyTextLoader = new EmptyTextLoader(virtualDocumentFilePath);
                var documentInfo = DocumentInfo.Create(documentId, name, filePath: virtualDocumentFilePath, loader: emptyTextLoader);
                _workspace.AddDocument(documentInfo);
                currentDocument = _workspace.GetDocument(virtualDocumentFilePath);

                Debug.Assert(currentDocument != null, "We just added the document, it should definitely be there.");
            }

            // Update document content

            var sourceText = document.GetGeneratedCodeSourceText();
            _workspace.OnDocumentChanged(currentDocument.Id, sourceText);
        }

        public override void Initialize(OmniSharpProjectSnapshotManager projectManager)
        {
            _projectManager = projectManager;
            _projectManager.Changed += ProjectManager_Changed;
        }

        private void ProjectManager_Changed(object sender, OmniSharpProjectChangeEventArgs args)
        {
            switch (args.Kind)
            {
                case OmniSharpProjectChangeKind.DocumentRemoved:
                    var roslynProject = GetRoslynProject(args.Older);
                    if (roslynProject == null)
                    {
                        // Project no longer exists
                        return;
                    }

                    var backgroundDocumentFilePath = GetBackgroundVirtualDocumentFilePath(args.DocumentFilePath);
                    var backgroundDocument = GetRoslynDocument(roslynProject, backgroundDocumentFilePath);
                    if (backgroundDocument == null)
                    {
                        // No background document associated
                        return;
                    }

                    // There's still a background document assocaited with the removed Razor document.
                    _workspace.RemoveDocument(backgroundDocument.Id);
                    break;
            }
        }

        private void Workspace_WorkspaceChanged(object sender, WorkspaceChangeEventArgs args)
        {
            switch (args.Kind)
            {
                case WorkspaceChangeKind.DocumentChanged:
                    {
                        var project = args.NewSolution.GetProject(args.ProjectId);
                        var document = project.GetDocument(args.DocumentId);

                        if (document.FilePath == null)
                        {
                            break;
                        }

                        if (document.FilePath.EndsWith(EditorVirtualDocumentSuffix) && !document.FilePath.EndsWith(BackgroundVirtualDocumentSuffix))
                        {
                            // Document from editor got opened, clear out any background documents of the same type

                            var razorDocumentFilePath = GetRazorDocumentFilePath(document);
                            var backgroundDocumentFilePath = GetBackgroundVirtualDocumentFilePath(razorDocumentFilePath);
                            var backgroundDocument = GetRoslynDocument(project, backgroundDocumentFilePath);
                            if (backgroundDocument != null)
                            {
                                _workspace.RemoveDocument(backgroundDocument.Id);
                            }
                        }
                        break;
                    }
                case WorkspaceChangeKind.DocumentRemoved:
                    {
                        var project = args.OldSolution.GetProject(args.ProjectId);
                        var document = project.GetDocument(args.DocumentId);

                        if (document.FilePath == null)
                        {
                            break;
                        }

                        if (document.FilePath.EndsWith(EditorVirtualDocumentSuffix) && !document.FilePath.EndsWith(BackgroundVirtualDocumentSuffix))
                        {
                            var razorDocumentFilePath = GetRazorDocumentFilePath(document);

                            if (File.Exists(razorDocumentFilePath))
                            {
                                // Razor document closed because the backing C# virtual document went away
                                var backgroundDocumentFilePath = GetBackgroundVirtualDocumentFilePath(razorDocumentFilePath);
                                var newName = Path.GetFileName(backgroundDocumentFilePath);
                                var delegatedTextLoader = new DelegatedTextLoader(document);
                                var movedDocumentInfo = DocumentInfo.Create(args.DocumentId, newName, loader: delegatedTextLoader, filePath: backgroundDocumentFilePath);
                                _workspace.AddDocument(movedDocumentInfo);
                            }
                        }
                    }
                    break;
            }
        }

        private Project GetRoslynProject(OmniSharpProjectSnapshot project)
        {
            var roslynProject = _workspace.CurrentSolution.Projects.FirstOrDefault(roslynProject => string.Equals(roslynProject.FilePath, project.FilePath, FilePathComparison.Instance));
            return roslynProject;
        }

        private static Document GetRoslynDocument(Project project, string backgroundDocumentFilePath)
        {
            var roslynDocument =  project.Documents.FirstOrDefault(document => string.Equals(document.FilePath, backgroundDocumentFilePath, FilePathComparison.Instance));
            return roslynDocument;
        }

        private static string GetRazorDocumentFilePath(Document document)
        {
            if (document.FilePath.EndsWith(BackgroundVirtualDocumentSuffix))
            {
                var razorDocumentFilePath = document.FilePath.Substring(0, document.FilePath.Length - BackgroundVirtualDocumentSuffix.Length);
                return razorDocumentFilePath;
            }
            else if (document.FilePath.EndsWith(EditorVirtualDocumentSuffix))
            {
                var razorDocumentFilePath = document.FilePath.Substring(0, document.FilePath.Length - EditorVirtualDocumentSuffix.Length);
                return razorDocumentFilePath;
            }

            Debug.Fail($"The caller should have ensured that '{document.FilePath}' is associated with a Razor file path.");
            return null;
        }

        private static string GetBackgroundVirtualDocumentFilePath(string razorDocumentFilePath)
        {
            var backgroundDocumentFilePath = razorDocumentFilePath + BackgroundVirtualDocumentSuffix;
            return backgroundDocumentFilePath;
        }

        private class DelegatedTextLoader : TextLoader
        {
            private readonly Document _document;

            public DelegatedTextLoader(Document document)
            {
                if (document is null)
                {
                    throw new ArgumentNullException(nameof(document));
                }

                _document = document;
            }

            public async override Task<TextAndVersion> LoadTextAndVersionAsync(Workspace workspace, DocumentId documentId, CancellationToken cancellationToken)
            {
                var sourceText = await _document.GetTextAsync();
                var textVersion = await _document.GetTextVersionAsync();
                var textAndVersion = TextAndVersion.Create(sourceText, textVersion);
                return textAndVersion;
            }
        }
    }
}
