using System;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Razor.ProjectSystem;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.VisualStudio.LanguageServerClient.Razor
{

    [Shared]
    [Export(typeof(ProjectSnapshotChangeTrigger))]
    internal class VirtualDocumentWorkspaceOpener : ProjectSnapshotChangeTrigger
    {
        private readonly LSPDocumentManager _documentManager;
        private Workspace _workspace;

        [ImportingConstructor]
        public VirtualDocumentWorkspaceOpener(LSPDocumentManager documentManager)
        {
            if (documentManager is null)
            {
                throw new ArgumentNullException(nameof(documentManager));
            }

            _documentManager = documentManager;
        }

        public override void Initialize(ProjectSnapshotManagerBase projectManager)
        {
            _workspace = projectManager.Workspace;
            _workspace.WorkspaceChanged += Workspace_WorkspaceChanged;
        }

        private void Workspace_WorkspaceChanged(object sender, WorkspaceChangeEventArgs args)
        {
            if (args.Kind != WorkspaceChangeKind.DocumentReloaded)
            {
                return;
            }

            var document = args.NewSolution.GetDocument(args.DocumentId);
            if (document.FilePath.EndsWith("__virtual.cs") && !_workspace.IsDocumentOpen(args.DocumentId))
            {
                if (!_documentManager.TryGetDocument(document.FilePath.Substring(0, document.FilePath.Length - "__virtual.cs".Length), out var lspDocument))
                {
                    return;
                }

                if (!lspDocument.TryGetVirtualDocument<CSharpVirtualDocumentSnapshot>(out var virtualDocument))
                {
                    return;
                }

                var container = virtualDocument.Snapshot.TextBuffer.AsTextContainer();
                var method = typeof(Workspace).GetMethod(
                    "OnDocumentOpened",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                method.Invoke(_workspace, new object[] { args.DocumentId, container, true });
            }
        }
    }
}
