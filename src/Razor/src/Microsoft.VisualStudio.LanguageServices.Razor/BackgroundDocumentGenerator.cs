// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Razor.ProjectSystem;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Internal;
using Microsoft.VisualStudio.Editor.Razor;
using Microsoft.VisualStudio.Text.Projection;

namespace Microsoft.CodeAnalysis.Razor
{
    [Shared]
    [Export(typeof(ProjectSnapshotChangeTrigger))]
    internal class BackgroundDocumentGenerator : ProjectSnapshotChangeTrigger
    {
        private readonly ForegroundDispatcher _foregroundDispatcher;
        private readonly RazorDynamicFileInfoProvider _infoProvider;
        private readonly RazorEditorFactoryService _editorFactory;
        private ProjectSnapshotManagerBase _projectManager;

        private readonly Dictionary<DocumentKey, SourceTextContainer> _openDocuments;
        private readonly Dictionary<DocumentKey, (ProjectSnapshot project, DocumentSnapshot document)> _work;
        private Timer _timer;

        private readonly IBufferGraphFactoryService _graphFactory;

        [ImportingConstructor]
        public BackgroundDocumentGenerator(
            ForegroundDispatcher foregroundDispatcher, 
            RazorDynamicFileInfoProvider infoProvider, 
            IBufferGraphFactoryService graphFactory,
            RazorEditorFactoryService editorFactory)
        {
            if (foregroundDispatcher == null)
            {
                throw new ArgumentNullException(nameof(foregroundDispatcher));
            }

            if (infoProvider == null)
            {
                throw new ArgumentNullException(nameof(infoProvider));
            }

            if (graphFactory == null)
            {
                throw new ArgumentNullException(nameof(graphFactory));
            }

            if (editorFactory == null)
            {
                throw new ArgumentNullException(nameof(editorFactory));
            }

            _foregroundDispatcher = foregroundDispatcher;
            _infoProvider = infoProvider;
            _graphFactory = graphFactory;
            _editorFactory = editorFactory;

            _work = new Dictionary<DocumentKey, (ProjectSnapshot project, DocumentSnapshot document)>();
            _openDocuments = new Dictionary<DocumentKey, SourceTextContainer>();
        }

        public bool HasPendingNotifications
        {
            get
            {
                lock (_work)
                {
                    return _work.Count > 0;
                }
            }
        }

        // Used in unit tests to control the timer delay.
        public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(2);

        public bool IsScheduledOrRunning => _timer != null;

        // Used in unit tests to ensure we can control when background work starts.
        public ManualResetEventSlim BlockBackgroundWorkStart { get; set; }

        // Used in unit tests to ensure we can know when background work finishes.
        public ManualResetEventSlim NotifyBackgroundWorkStarting { get; set; }

        // Used in unit tests to ensure we can know when background has captured its current workload.
        public ManualResetEventSlim NotifyBackgroundCapturedWorkload { get; set; }

        // Used in unit tests to ensure we can control when background work completes.
        public ManualResetEventSlim BlockBackgroundWorkCompleting { get; set; }

        // Used in unit tests to ensure we can know when background work finishes.
        public ManualResetEventSlim NotifyBackgroundWorkCompleted { get; set; }

        private void OnStartingBackgroundWork()
        {
            if (BlockBackgroundWorkStart != null)
            {
                BlockBackgroundWorkStart.Wait();
                BlockBackgroundWorkStart.Reset();
            }

            if (NotifyBackgroundWorkStarting != null)
            {
                NotifyBackgroundWorkStarting.Set();
            }
        }

        private void OnCompletingBackgroundWork()
        {
            if (BlockBackgroundWorkCompleting != null)
            {
                BlockBackgroundWorkCompleting.Wait();
                BlockBackgroundWorkCompleting.Reset();
            }
        }

        private void OnCompletedBackgroundWork()
        {
            if (NotifyBackgroundWorkCompleted != null)
            {
                NotifyBackgroundWorkCompleted.Set();
            }
        }

        private void OnBackgroundCapturedWorkload()
        {
            if (NotifyBackgroundCapturedWorkload != null)
            {
                NotifyBackgroundCapturedWorkload.Set();
            }
        }

        public override void Initialize(ProjectSnapshotManagerBase projectManager)
        {
            if (projectManager == null)
            {
                throw new ArgumentNullException(nameof(projectManager));
            }

            _projectManager = projectManager;
            _projectManager.Changed += ProjectManager_Changed;
        }

        protected virtual async Task ProcessDocument(ProjectSnapshot project, DocumentSnapshot document)
        {
            await document.GetGeneratedOutputAsync().ConfigureAwait(false);
            _infoProvider.UpdateFileInfo(project, document);
        }

        public void Enqueue(ProjectSnapshot project, DocumentSnapshot document)
        {
            if (project == null)
            {
                throw new ArgumentNullException(nameof(project));
            }

            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            _foregroundDispatcher.AssertForegroundThread();

            lock (_work)
            {
                // We only want to store the last 'seen' version of any given document. That way when we pick one to process
                // it's always the best version to use.
                _work[new DocumentKey(project.FilePath, document.FilePath)] = (project, document);

                StartWorker();
            }
        }

        protected virtual void StartWorker()
        {
            // Access to the timer is protected by the lock in Enqueue and in Timer_Tick
            if (_timer == null)
            {

                // Timer will fire after a fixed delay, but only once.
                _timer = NonCapturingTimer.Create(state => ((BackgroundDocumentGenerator)state).Timer_Tick(), this, Delay, Timeout.InfiniteTimeSpan);
            }
        }

        private void Timer_Tick()
        {
            _ = TimerTick();
        }

        private async Task TimerTick()
        {
            try
            {
                _foregroundDispatcher.AssertBackgroundThread();

                // Timer is stopped.
                _timer.Change(Timeout.Infinite, Timeout.Infinite);

                OnStartingBackgroundWork();

                KeyValuePair<DocumentKey, (ProjectSnapshot project, DocumentSnapshot document)>[] work;
                lock (_work)
                {
                    work = _work.ToArray();
                    _work.Clear();
                }

                OnBackgroundCapturedWorkload();

                for (var i = 0; i < work.Length; i++)
                {
                    var (project, document) = work[i].Value;
                    try
                    {
                        await ProcessDocument(project, document).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        ReportError(project, ex);
                    }
                }

                OnCompletingBackgroundWork();

                lock (_work)
                {
                    // Resetting the timer allows another batch of work to start.
                    _timer.Dispose();
                    _timer = null;

                    // If more work came in while we were running start the worker again.
                    if (_work.Count > 0)
                    {
                        StartWorker();
                    }
                }

                OnCompletedBackgroundWork();
            }
            catch (Exception ex)
            {
                // This is something totally unexpected, let's just send it over to the workspace.
                await Task.Factory.StartNew(
                    (p) => ((ProjectSnapshotManagerBase)p).ReportError(ex),
                    _projectManager,
                    CancellationToken.None,
                    TaskCreationOptions.None,
                    _foregroundDispatcher.ForegroundScheduler).ConfigureAwait(false);
            }
        }

        private void ReportError(ProjectSnapshot project, Exception ex)
        {
            GC.KeepAlive(Task.Factory.StartNew(
                (p) => ((ProjectSnapshotManagerBase)p).ReportError(ex, project), 
                _projectManager,
                CancellationToken.None,
                TaskCreationOptions.None,
                _foregroundDispatcher.ForegroundScheduler));
        }

        private void ProjectManager_Changed(object sender, ProjectChangeEventArgs e)
        {
            switch (e.Kind)
            {
                case ProjectChangeKind.ProjectAdded:
                    {
                        var projectSnapshot = e.Newer;
                        foreach (var documentFilePath in projectSnapshot.DocumentFilePaths)
                        {
                            Enqueue(projectSnapshot, projectSnapshot.GetDocument(documentFilePath));
                        }

                        break;
                    }
                case ProjectChangeKind.ProjectChanged:
                    {
                        var projectSnapshot = e.Newer;
                        foreach (var documentFilePath in projectSnapshot.DocumentFilePaths)
                        {
                            Enqueue(projectSnapshot, projectSnapshot.GetDocument(documentFilePath));
                        }

                        break;
                    }

                case ProjectChangeKind.DocumentAdded:
                case ProjectChangeKind.DocumentChanged:
                    {
                        var project = e.Newer;
                        var document = project.GetDocument(e.DocumentFilePath);

                        var key = new DocumentKey(project.FilePath, document.FilePath);

                        if (_projectManager.IsDocumentOpen(e.DocumentFilePath) && _openDocuments.TryGetValue(key, out var textContainer))
                        {
                            // Document is already tracked as open. For open documents, we want to update immediately.
                            _infoProvider.UpdateOpenDocument(project, document, textContainer);
                            break;
                        }
                        else if (_projectManager.IsDocumentOpen(e.DocumentFilePath) && TryGetCSharpBuffer(document, out textContainer))
                        {
                            // The document is open now. Clear any background tasks that were updating it.
                            lock (_work)
                            {
                                _work.Remove(key);
                            }

                            _openDocuments.Add(key, textContainer);
                            _infoProvider.UpdateOpenDocument(project, document, textContainer);
                            break;
                        }

                        // Document is not open anymore so make sure we're not holding onto a text container for it.
                        //
                        // We can also end up here if the document is open but we failed to find a text buffer for it.
                        _openDocuments.Remove(new DocumentKey(project.FilePath, document.FilePath));

                        Enqueue(project, document);
                        foreach (var relatedDocument in project.GetRelatedDocuments(document))
                        {
                            Enqueue(project, document);
                        }

                        break;
                    }

                case ProjectChangeKind.DocumentRemoved:
                    {
                        // For removals use the old snapshot to find the removed document, so we can figure out 
                        // what the imports were in the new snapshot.
                        var document = e.Older.GetDocument(e.DocumentFilePath);

                        foreach (var relatedDocument in e.Newer.GetRelatedDocuments(document))
                        {
                            Enqueue(e.Newer, document);
                        }

                        break;
                    }

                case ProjectChangeKind.ProjectRemoved:
                    {
                        // ignore
                        break;
                    }

                default:
                    throw new InvalidOperationException($"Unknown ProjectChangeKind {e.Kind}");
            }
        }

        private bool TryGetCSharpBuffer(DocumentSnapshot document, out SourceTextContainer textContainer)
        {
            // This can fail sometimes, like when the C# projection is disconnected from the editor.
            if (!document.TryGetText(out var text))
            {
                textContainer = null;
                return false;
            }

            if (text.Container == null)
            {
                textContainer = null;
                return false;
            }

            var subjectBuffer = text.Container.GetTextBuffer();
            if (subjectBuffer == null)
            {
                textContainer = null;
                return false;
            }

            if (!_editorFactory.TryGetDocumentTracker(subjectBuffer, out var tracker))
            {
                textContainer = null;
                return false;
            }
            
            if (tracker.TextViews.Count == 0)
            {
                textContainer = null;
                return false;
            }

            var textBuffer = tracker.TextViews[0].BufferGraph.GetTextBuffers(b => b.ContentType.IsOfType("CSharp")).FirstOrDefault();
            textContainer = textBuffer.AsTextContainer();
            return textContainer != null;
        }
    }
}