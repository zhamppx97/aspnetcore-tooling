// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Composition;
using System.IO;

namespace Microsoft.VisualStudio.LanguageServerClient.Razor.Feedback
{
    [Shared]
    [Export(typeof(FeedbackFileLoggerProviderFactory))]
    internal class DefaultFeedbackFileLoggerProviderFactory : FeedbackFileLoggerProviderFactory
    {
        private readonly object _creationLock;
        private readonly FeedbackLogDirectoryProvider _feedbackLogDirectoryProvider;
        private DefaultFeedbackLogWriter _currentLogWriter;

        [ImportingConstructor]
        public DefaultFeedbackFileLoggerProviderFactory(FeedbackLogDirectoryProvider feedbackLogDirectoryProvider)
        {
            if (feedbackLogDirectoryProvider is null)
            {
                throw new ArgumentNullException(nameof(feedbackLogDirectoryProvider));
            }

            _feedbackLogDirectoryProvider = feedbackLogDirectoryProvider;
            _creationLock = new object();
        }

        public override FeedbackFileLoggerProvider GetOrCreate()
        {
            lock (_creationLock)
            {
                if (_currentLogWriter != null)
                {
                    // Dispose last log writer so we can start a new session. Technically only one should only ever be active at a time.
                    _currentLogWriter.Dispose();
                }

                _currentLogWriter = new DefaultFeedbackLogWriter(_feedbackLogDirectoryProvider);
                var provider = new FeedbackFileLoggerProvider(_currentLogWriter);

                return provider;
            }
        }
    }
}
