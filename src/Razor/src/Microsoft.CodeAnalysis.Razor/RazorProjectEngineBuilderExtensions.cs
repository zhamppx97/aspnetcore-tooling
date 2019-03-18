// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis.CSharp;

namespace Microsoft.CodeAnalysis.Razor
{
    /// <summary>
    /// Roslyn specific <see cref="RazorProjectEngineBuilder"/> extensions.
    /// </summary>
    public static class RazorProjectEngineBuilderExtensions
    {
        /// <summary>
        /// Sets the C# language version to respect when generating code. 
        /// </summary>
        /// <param name="builder">The <see cref="RazorProjectEngineBuilder"/>.</param>
        /// <param name="csharpLanguageVersion">The C# <see cref="LanguageVersion"/>.</param>
        /// <returns>The <see cref="RazorProjectEngineBuilder"/>.</returns>
        public static RazorProjectEngineBuilder SetCSharpLanguageVersion(this RazorProjectEngineBuilder builder, LanguageVersion csharpLanguageVersion)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (builder.Configuration.LanguageVersion.Major < 3)
            {
                // Prior to 3.0 there were no C# version specific controlled features so there's no value in setting a CSharp language version, noop.
                return builder;
            }

            var existingFeature = builder.Features.OfType<ConfigureParserForCSharpVersionFeature>().FirstOrDefault();
            if (existingFeature != null)
            {
                builder.Features.Remove(existingFeature);
            }

            builder.Features.Add(new ConfigureParserForCSharpVersionFeature(csharpLanguageVersion));

            return builder;
        }

        private class ConfigureParserForCSharpVersionFeature : IConfigureRazorCodeGenerationOptionsFeature
        {
            public ConfigureParserForCSharpVersionFeature(LanguageVersion csharpLanguageVersion)
            {
                CSharpLanguageVersion = csharpLanguageVersion;
            }

            public LanguageVersion CSharpLanguageVersion { get; }

            public int Order { get; set; }

            public RazorEngine Engine { get; set; }

            public void Configure(RazorCodeGenerationOptionsBuilder options)
            {
                if (options == null)
                {
                    throw new ArgumentNullException(nameof(options));
                }

                if (CSharpLanguageVersion != LanguageVersion.Default &&
                    CSharpLanguageVersion < LanguageVersion.CSharp8)
                {
                    // Nullability enforcement does not exist prior to 8.0
                    options.SuppressNullabilityEnforcement = true;
                }
            }
        }
    }
}
