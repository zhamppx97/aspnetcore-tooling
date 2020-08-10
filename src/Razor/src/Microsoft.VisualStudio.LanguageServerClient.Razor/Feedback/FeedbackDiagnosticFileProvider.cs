// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Text;
using Microsoft.Internal.VisualStudio.Shell.Embeddable.Feedback;

namespace Microsoft.VisualStudio.LanguageServerClient.Razor.Feedback
{
    [Shared]
    [Export(typeof(IFeedbackDiagnosticFileProvider))]
    internal class FeedbackDiagnosticFileProvider : IFeedbackDiagnosticFileProvider
    {
        private readonly FeedbackLogDirectoryProvider _feedbackLogDirectoryProvider;

        [ImportingConstructor]
        public FeedbackDiagnosticFileProvider(FeedbackLogDirectoryProvider feedbackLogDirectoryProvider)
        {
            _feedbackLogDirectoryProvider = feedbackLogDirectoryProvider;
        }

        public IReadOnlyCollection<string> GetFiles()
        {
            throw new NotImplementedException();
        }
    }
}
