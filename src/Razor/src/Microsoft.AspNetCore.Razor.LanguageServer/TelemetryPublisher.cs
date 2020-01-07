// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.Razor.LanguageServer
{
    internal abstract class TelemetryPublisher: IDisposable
    {
        public abstract void Dispose();

        public abstract void Publish(string eventName, IDictionary<string, string> properties = null, IDictionary<string, double> metrics = null);
    }
}