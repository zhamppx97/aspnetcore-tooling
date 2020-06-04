// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.LanguageServer;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using Nerdbank.Streams;
using OmniSharp.Extensions.LanguageServer.Server;
using StreamJsonRpc;
using Trace = Microsoft.AspNetCore.Razor.LanguageServer.Trace;

namespace Microsoft.VisualStudio.LanguageServerClient.Razor
{
    [Export(typeof(ILanguageClient))]
    [ContentType(RazorLSPConstants.RazorLSPContentTypeName)]
    internal class RazorLanguageServerClient : ILanguageClient, ILanguageClientCustomMessage2
    {
        private readonly RazorLanguageServerCustomMessageTarget _customMessageTarget;
        private readonly ILanguageClientMiddleLayer _middleLayer;
        private ILanguageServer _server;

        [ImportingConstructor]
        public RazorLanguageServerClient(RazorLanguageServerCustomMessageTarget customTarget, RazorLanguageClientMiddleLayer middleLayer)
        {
            if (customTarget is null)
            {
                throw new ArgumentNullException(nameof(customTarget));
            }

            if (middleLayer is null)
            {
                throw new ArgumentNullException(nameof(middleLayer));
            }

            _customMessageTarget = customTarget;
            _middleLayer = middleLayer;
            StartAsync += RazorLanguageServerClient_StartAsync;
            StopAsync += RazorLanguageServerClient_StopAsync;
        }

        public string Name => "Razor Language Server Client";

        public IEnumerable<string> ConfigurationSections => null;

        public object InitializationOptions => null;

        public IEnumerable<string> FilesToWatch => null;

        public object MiddleLayer => _middleLayer;

        public object CustomMessageTarget => _customMessageTarget;

        public event AsyncEventHandler<EventArgs> StartAsync;
        public event AsyncEventHandler<EventArgs> StopAsync;

        public async Task<Connection> ActivateAsync(CancellationToken token)
        {
            var (clientStream, serverStream) = FullDuplexStream.CreatePair();

            // Need an auto-flushing stream for the server because O# doesn't currently flush after writing responses. Without this
            // performing the Initialize handshake with the LanguageServer hangs.
            var autoFlushingStream = new AutoFlushingNerdbankStream(serverStream);
            _server = await RazorLanguageServer.CreateAsync(autoFlushingStream, autoFlushingStream, Trace.Verbose).ConfigureAwait(false);

            // Fire and forget for Initialized. Need to allow the LSP infrastructure to run in order to actually Initialize.
            _ = _server.InitializedAsync(token);
            var connection = new Connection(clientStream, clientStream);
            return connection;
        }

        public async Task OnLoadedAsync()
        {
            await StartAsync.InvokeAsync(this, EventArgs.Empty).ConfigureAwait(false);
        }

        public Task OnServerInitializeFailedAsync(Exception e)
        {
            return Task.CompletedTask;
        }

        public Task OnServerInitializedAsync()
        {
            return Task.CompletedTask;
        }

        public Task AttachForCustomMessageAsync(JsonRpc rpc) => Task.CompletedTask;

        private Task RazorLanguageServerClient_StopAsync(object sender, EventArgs args)
        {
            if (_server == null)
            {
                return Task.CompletedTask;
            }

            _server.Dispose();
            return _server.WaitForExit;
        }

        private Task RazorLanguageServerClient_StartAsync(object sender, EventArgs args)
        {
            return Task.CompletedTask;
        }
    }
}
