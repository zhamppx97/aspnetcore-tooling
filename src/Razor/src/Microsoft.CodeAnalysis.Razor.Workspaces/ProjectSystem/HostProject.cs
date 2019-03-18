// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis.CSharp;

namespace Microsoft.CodeAnalysis.Razor.ProjectSystem
{
    internal class HostProject
    {
        public HostProject(
            string projectFilePath, 
            RazorConfiguration razorConfiguration, 
            string rootNamespace,
            LanguageVersion csharpLanguageVersion)
        {
            if (projectFilePath == null)
            {
                throw new ArgumentNullException(nameof(projectFilePath));
            }

            if (razorConfiguration == null)
            {
                throw new ArgumentNullException(nameof(razorConfiguration));
            }

            FilePath = projectFilePath;
            Configuration = razorConfiguration;
            RootNamespace = rootNamespace;
            CSharpLanguageVersion = csharpLanguageVersion;
        }

        public RazorConfiguration Configuration { get; }

        public string FilePath { get; }

        public string RootNamespace { get; }

        public LanguageVersion CSharpLanguageVersion { get; }
    }
}