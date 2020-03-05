// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Razor.Workspaces;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Internal;
using Microsoft.VisualStudio.Editor.Razor;

namespace Microsoft.CodeAnalysis.Razor.ProjectSystem
{
    [System.Composition.Shared]
    [ExportMetadata("Extensions", new string[] { "cshtml", "razor", })]
    [Export(typeof(RazorDynamicFileInfoProvider))]
    [Export(typeof(IDynamicFileInfoProvider))]
    [Export(typeof(ILSPDocumentFileInfoProvider))]
    internal class DefaultRazorDynamicFileInfoProvider : RazorDynamicFileInfoProvider, IDynamicFileInfoProvider, ILSPDocumentFileInfoProvider
    {
        private readonly ConcurrentDictionary<Key, Entry> _entries;
        private readonly Func<Key, Entry> _createEmptyEntry;
        private readonly DocumentServiceProviderFactory _factory;

        [ImportingConstructor]
        public DefaultRazorDynamicFileInfoProvider(
            DocumentServiceProviderFactory factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            _factory = factory;

            _entries = new ConcurrentDictionary<Key, Entry>();
            _createEmptyEntry = (key) => new Entry(CreateEmptyInfo(key));
        }

        public event EventHandler<string> Updated;

        public void UpdateFileInfo(Uri uri, ICSharpOutputContainer csharpOutputContainer)
        {
            var filePath = uri.GetAbsoluteOrUNCPath().Replace('/', '\\');
            KeyValuePair<Key, Entry>? associatedKvp = null;
            foreach (var entry in _entries)
            {
                if (FilePathComparer.Instance.Equals(filePath, entry.Key.FilePath))
                {
                    associatedKvp = entry;
                    break;
                }
            }

            if (associatedKvp == null)
            {
                return;
            }

            var associatedKey = associatedKvp.Value.Key;
            var associatedEntry = associatedKvp.Value.Value;

            lock (associatedEntry.Lock)
            {
                associatedEntry.Current = CreateInfo(associatedKey, csharpOutputContainer);
            }

            Updated?.Invoke(this, filePath);
        }

        // Called by us to update entries
        public override void UpdateFileInfo(string projectFilePath, DynamicDocumentContainer documentContainer)
        {
            if (projectFilePath == null)
            {
                throw new ArgumentNullException(nameof(projectFilePath));
            }

            if (documentContainer == null)
            {
                throw new ArgumentNullException(nameof(documentContainer));
            }

            // There's a possible race condition here where we're processing an update
            // and the project is getting unloaded. So if we don't find an entry we can
            // just ignore it.
            var key = new Key(projectFilePath, documentContainer.FilePath);
            if (_entries.TryGetValue(key, out var entry))
            {
                lock (entry.Lock)
                {
                    entry.Current = CreateInfo(key, documentContainer);
                }

                Updated?.Invoke(this, documentContainer.FilePath);
            }
        }

        // Called by us when a document opens in the editor
        public override void SuppressDocument(string projectFilePath, string documentFilePath)
        {
            if (projectFilePath == null)
            {
                throw new ArgumentNullException(nameof(projectFilePath));
            }

            if (documentFilePath == null)
            {
                throw new ArgumentNullException(nameof(documentFilePath));
            }

            // There's a possible race condition here where we're processing an update
            // and the project is getting unloaded. So if we don't find an entry we can
            // just ignore it.
            var key = new Key(projectFilePath, documentFilePath);
            if (_entries.TryGetValue(key, out var entry))
            {
                var updated = false;
                lock (entry.Lock)
                {
                    if (!(entry.Current.TextLoader is EmptyTextLoader))
                    {
                        updated = true;
                        entry.Current = CreateEmptyInfo(key);
                    }
                }

                if (updated)
                {
                    Updated?.Invoke(this, documentFilePath);
                }
            }
        }

        public Task<DynamicFileInfo> GetDynamicFileInfoAsync(ProjectId projectId, string projectFilePath, string filePath, CancellationToken cancellationToken)
        {
            if (projectFilePath == null)
            {
                throw new ArgumentNullException(nameof(projectFilePath));
            }

            if (filePath == null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            var key = new Key(projectFilePath, filePath);
            var entry = _entries.GetOrAdd(key, _createEmptyEntry);
            return Task.FromResult(entry.Current);
        }

        public Task RemoveDynamicFileInfoAsync(ProjectId projectId, string projectFilePath, string filePath, CancellationToken cancellationToken)
        {
            if (projectFilePath == null)
            {
                throw new ArgumentNullException(nameof(projectFilePath));
            }

            if (filePath == null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            var key = new Key(projectFilePath, filePath);
            _entries.TryRemove(key, out var entry);
            return Task.CompletedTask;
        }

        private DynamicFileInfo CreateEmptyInfo(Key key)
        {
            var filename = key.FilePath + "__virtual.cs";
            var textLoader = new EmptyTextLoader(filename);
            return new DynamicFileInfo(filename, SourceCodeKind.Regular, textLoader, _factory.CreateEmpty());
        }

        private DynamicFileInfo CreateInfo(Key key, DynamicDocumentContainer document)
        {
            var filename = key.FilePath + "__virtual.cs";
            var textLoader = document.GetTextLoader(filename);
            return new DynamicFileInfo(filename, SourceCodeKind.Regular, textLoader, _factory.Create(document));
        }

        private DynamicFileInfo CreateInfo(Key key, ICSharpOutputContainer csharpOutputContainer)
        {
            var filename = key.FilePath + "__virtual.cs";
            var textLoader = csharpOutputContainer.CreateGeneratedTextLoader(filename);
            return new DynamicFileInfo(filename, SourceCodeKind.Regular, textLoader, _factory.CreateLSP());
        }

        // Using a separate handle to the 'current' file info so that can allow Roslyn to send
        // us the add/remove operations, while we process the update operations.
        public class Entry
        {
            // Can't ever be null for thread-safety reasons
            private DynamicFileInfo _current;

            public Entry(DynamicFileInfo current)
            {
                if (current == null)
                {
                    throw new ArgumentNullException(nameof(current));
                }

                Current = current;
                Lock = new object();
            }

            public DynamicFileInfo Current
            {
                get => _current;
                set
                {
                    if (value == null)
                    {
                        throw new ArgumentNullException(nameof(value));
                    }

                    _current = value;
                }
            }

            public object Lock { get; }

            public override string ToString()
            {
                lock (Lock)
                {
                    return $"{Current.FilePath} - {Current.TextLoader.GetType()}";
                }
            }
        }

        private readonly struct Key : IEquatable<Key>
        {
            public readonly string ProjectFilePath;
            public readonly string FilePath;

            public Key(string projectFilePath, string filePath)
            {
                ProjectFilePath = projectFilePath;
                FilePath = filePath;
            }

            public bool Equals(Key other)
            {
                return
                    FilePathComparer.Instance.Equals(ProjectFilePath, other.ProjectFilePath) &&
                    FilePathComparer.Instance.Equals(FilePath, other.FilePath);
            }

            public override bool Equals(object obj)
            {
                return obj is Key other ? Equals(other) : false;
            }

            public override int GetHashCode()
            {
                var hash = new HashCodeCombiner();
                hash.Add(ProjectFilePath, FilePathComparer.Instance);
                hash.Add(FilePath, FilePathComparer.Instance);
                return hash;
            }
        }
    }
}
