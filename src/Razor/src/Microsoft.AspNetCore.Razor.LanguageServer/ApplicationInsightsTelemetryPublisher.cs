// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.Razor.LanguageServer
{
    internal class ApplicationInsightsTelemetryPublisher : TelemetryPublisher
    {
        private const string InstrumentationKey = "31c50112-58ff-4e40-bc15-48af64e7dfeb";

        private readonly TelemetryClient _client = GetTelemetryClient();

        public ApplicationInsightsTelemetryPublisher()
        {
            _client = GetTelemetryClient();
        }

        public override void Publish(string eventName, IDictionary<string, string> properties, IDictionary<string, double> metrics = null)
        {
            _client.TrackEvent(eventName, properties, metrics);
        }

        private static TelemetryClient GetTelemetryClient()
        {
            var configuration = new TelemetryConfiguration {
                InstrumentationKey = InstrumentationKey
            };
            return new TelemetryClient(configuration);
        }

        public override void Dispose()
        {
            _client.Flush();
        }
    }
}
