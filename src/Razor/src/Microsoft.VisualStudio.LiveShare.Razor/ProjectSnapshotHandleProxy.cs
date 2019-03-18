// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Razor.ProjectSystem;

namespace Microsoft.VisualStudio.LiveShare.Razor
{
    public sealed class ProjectSnapshotHandleProxy
    {
        public ProjectSnapshotHandleProxy(
            Uri filePath,
            RazorConfiguration configuration,
            string rootNamespace,
            LanguageVersion csharpLanguageVersion,
            ProjectWorkspaceState projectWorkspaceState)
        {
            if (filePath == null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            FilePath = filePath;
            Configuration = configuration;
            RootNamespace = rootNamespace;
            CSharpLanguageVersion = csharpLanguageVersion;
            ProjectWorkspaceState = projectWorkspaceState;
        }

        public Uri FilePath { get; }

        public RazorConfiguration Configuration { get; }

        public string RootNamespace { get; }

        public LanguageVersion CSharpLanguageVersion { get; }

        public ProjectWorkspaceState ProjectWorkspaceState { get; }
    }
}
