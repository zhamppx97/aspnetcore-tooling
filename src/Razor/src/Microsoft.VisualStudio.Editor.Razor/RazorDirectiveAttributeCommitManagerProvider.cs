// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using Microsoft.CodeAnalysis.Razor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.VisualStudio.Editor.Razor
{
    [System.Composition.Shared]
    [Export(typeof(IAsyncCompletionCommitManagerProvider))]
    [Name("Razor directive attribute completion commit provider.")]
    [ContentType(RazorLanguage.CoreContentType)]
    internal class RazorDirectiveAttributeCommitManagerProvider : IAsyncCompletionCommitManagerProvider
    {
        public IAsyncCompletionCommitManager GetOrCreate(ITextView textView)
        {
            if (textView == null)
            {
                throw new ArgumentNullException(nameof(textView));
            }

            var razorBuffer = textView.BufferGraph.GetRazorBuffers().FirstOrDefault();
            if (!razorBuffer.Properties.TryGetProperty(typeof(RazorDirectiveAttributeCommitManager), out IAsyncCompletionCommitManager completionSource) ||
                completionSource == null)
            {
                completionSource = CreateCommitManager(razorBuffer);
                razorBuffer.Properties.AddProperty(typeof(RazorDirectiveAttributeCommitManager), completionSource);
            }

            return completionSource;
        }

        // Internal for testing
        internal IAsyncCompletionCommitManager CreateCommitManager(ITextBuffer razorBuffer)
        {
            if (!razorBuffer.Properties.TryGetProperty(typeof(VisualStudioRazorParser), out VisualStudioRazorParser parser))
            {
                // Parser hasn't been associated with the text buffer yet.
                return null;
            }

            var completionSource = new RazorDirectiveAttributeCommitManager();
            return completionSource;
        }
    }
}
