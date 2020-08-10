// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.VisualStudio.LanguageServerClient.Razor.Feedback
{
    internal class FeedbackFileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly FeedbackLogWriter _feedbackFileLogger;

        public FeedbackFileLogger(string categoryName, FeedbackLogWriter feedbackFileLogger)
        {
            if (categoryName is null)
            {
                throw new ArgumentNullException(nameof(categoryName));
            }

            if (feedbackFileLogger is null)
            {
                throw new ArgumentNullException(nameof(feedbackFileLogger));
            }

            _categoryName = categoryName;
            _feedbackFileLogger = feedbackFileLogger;
        }

        public IDisposable BeginScope<TState>(TState state) => Scope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var formattedResult = formatter(state, exception);
            var logContent = $"[{_categoryName}] {formattedResult}";
            _feedbackFileLogger.WriteToFile(logContent);
        }

        private class Scope : IDisposable
        {
            public static readonly Scope Instance = new Scope();

            public void Dispose()
            {
            }
        }
    }
}
