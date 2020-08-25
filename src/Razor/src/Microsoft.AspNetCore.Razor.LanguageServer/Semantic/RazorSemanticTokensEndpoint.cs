// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.LanguageServer.Common;
using Microsoft.AspNetCore.Razor.LanguageServer.ProjectSystem;
using Microsoft.AspNetCore.Razor.LanguageServer.Semantic.Models;
using Microsoft.CodeAnalysis.Razor;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Document.Proposals;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models.Proposals;
using Microsoft.AspNetCore.Razor.LanguageServer.Semantic.Interfaces;

namespace Microsoft.AspNetCore.Razor.LanguageServer.Semantic
{
    //internal class LegacyRazorSemanticTokensEndpoint :
    //    RazorSemanticTokensEndpointBase,
    //    IRegistrationExtension
    //{
    //    private const string SemanticCapability = "semanticTokensProvider";

    //    public LegacyRazorSemanticTokensEndpoint(
    //        ForegroundDispatcher  foregroundDispatcher,
    //        DocumentResolver documentResolver,
    //        RazorSemanticTokensInfoService semanticTokensInfoService
    //    ):base(foregroundDispatcher, documentResolver, semanticTokensInfoService) { }

    //    public RegistrationExtensionResult GetRegistration()
    //    {
    //        var semanticTokensOptions = new LegacySemanticTokensOptions
    //        {
    //            DocumentProvider = new SemanticTokensDocumentProviderOptions
    //            {
    //                Edits = true,
    //            },
    //            Legend = RazorSemanticTokensLegend.Instance,
    //            RangeProvider = true,
    //        };

    //        return new RegistrationExtensionResult(SemanticCapability, semanticTokensOptions);
    //    }
    //}

    internal class RazorSemanticTokensEndpointBase
    {
        protected readonly RazorSemanticTokensInfoService _semanticTokensInfoService;
        protected readonly ForegroundDispatcher _foregroundDispatcher;
        protected readonly DocumentResolver _documentResolver;

        public RazorSemanticTokensEndpointBase(
            ForegroundDispatcher foregroundDispatcher,
            DocumentResolver documentResolver,
            RazorSemanticTokensInfoService semanticTokensInfoService)
        {
            if (semanticTokensInfoService is null)
            {
                throw new ArgumentNullException(nameof(semanticTokensInfoService));
            }
            if (foregroundDispatcher is null)
            {
                throw new ArgumentNullException(nameof(foregroundDispatcher));
            }

            if (documentResolver is null)
            {
                throw new ArgumentNullException(nameof(documentResolver));
            }

            _semanticTokensInfoService = semanticTokensInfoService;
            _foregroundDispatcher = foregroundDispatcher;
            _documentResolver = documentResolver;
        }

        internal async Task<SemanticTokens> Handle(string absolutePath, CancellationToken cancellationToken, Range range = null)
        {
            var codeDocument = await TryGetCodeDocumentAsync(absolutePath, cancellationToken);
            if (codeDocument is null)
            {
                return null;
            }

            var tokens = _semanticTokensInfoService.GetSemanticTokens(codeDocument, range);

            return tokens;
        }

        internal async Task<RazorCodeDocument> TryGetCodeDocumentAsync(string absolutePath, CancellationToken cancellationToken)
        {
            var document = await Task.Factory.StartNew(() =>
            {
                _documentResolver.TryResolveDocument(absolutePath, out var documentSnapshot);

                return documentSnapshot;
            }, cancellationToken, TaskCreationOptions.None, _foregroundDispatcher.ForegroundScheduler);

            if (document is null)
            {
                return null;
            }

            var codeDocument = await document.GetGeneratedOutputAsync();
            if (codeDocument.IsUnsupported())
            {
                return null;
            }

            return codeDocument;
        }

    }

    internal class RazorSemanticTokensEndpoint : RazorSemanticTokensEndpointBase, ISemanticTokensHandler, ISemanticTokensRangeHandler, ISemanticTokensDeltaHandler
       //  ILegacySemanticTokensHandler, ILegacySemanticTokensRangeHandler, ILegacySemanticTokensEditHandler
    {
        private readonly ILogger _logger;

        private SemanticTokensCapability _capability;

        public RazorSemanticTokensEndpoint(
            ForegroundDispatcher foregroundDispatcher,
            DocumentResolver documentResolver,
            RazorSemanticTokensInfoService semanticTokensInfoService,
            ILoggerFactory loggerFactory): base(foregroundDispatcher, documentResolver, semanticTokensInfoService)
        {
            if (loggerFactory is null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _logger = loggerFactory.CreateLogger<RazorSemanticTokensEndpoint>();
        }

        public async Task<SemanticTokens> Handle(SemanticTokensParams request, CancellationToken cancellationToken)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return await Handle(request.TextDocument.Uri.ToUri().AbsolutePath, cancellationToken, range: null);
        }

        public async Task<SemanticTokens> Handle(SemanticTokensRangeParams request, CancellationToken cancellationToken)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return await Handle(request.TextDocument.Uri.ToUri().AbsolutePath, cancellationToken, request.Range);
        }

        public async Task<SemanticTokensFullOrDelta?> Handle(SemanticTokensDeltaParams request, CancellationToken cancellationToken)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var codeDocument = await TryGetCodeDocumentAsync(request.TextDocument.Uri.ToUri().AbsolutePath, cancellationToken);
            if (codeDocument is null)
            {
                return null;
            }

            var edits = _semanticTokensInfoService.GetSemanticTokensEdits(codeDocument, request.PreviousResultId);

            return edits;
        }

        public async Task<LegacySemanticTokens> Handle(LegacySemanticTokensParams request, CancellationToken cancellationToken)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var result = await Handle(request.TextDocument.Uri.ToUri().AbsolutePath, cancellationToken, range: null);

            return new LegacySemanticTokens(result);
        }

        public async Task<LegacySemanticTokens> Handle(LegacySemanticTokensRangeParams request, CancellationToken cancellationToken)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var result = await Handle(request.TextDocument.Uri.ToUri().AbsolutePath, cancellationToken, request.Range);

            return new LegacySemanticTokens(result);
        }

        public async Task<LegacySemanticTokensOrSemanticTokensEdits?> Handle(LegacySemanticTokensEditParams request, CancellationToken cancellationToken)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var codeDocument = await TryGetCodeDocumentAsync(request.TextDocument.Uri.ToUri().AbsolutePath, cancellationToken);
            if (codeDocument is null)
            {
                return null;
            }

            var edits = _semanticTokensInfoService.GetSemanticTokensEdits(codeDocument, request.PreviousResultId);

            return new LegacySemanticTokensOrSemanticTokensEdits(edits);
        }
        //public RegistrationExtensionResult GetRegistration()
        //{
        //    var semanticTokensOptions = new SemanticTokensOptions
        //    {
        //        DocumentProvider = new SemanticTokensDocumentProviderOptions
        //        {
        //            Edits = true,
        //        },
        //        Legend = SemanticTokensLegend.Instance,
        //        RangeProvider = true,
        //    };

        //    return new RegistrationExtensionResult(SemanticCapability, semanticTokensOptions);
        //}

        public SemanticTokensRegistrationOptions GetRegistrationOptions()
        {
            return new SemanticTokensRegistrationOptions
            {
                DocumentSelector = RazorDefaults.Selector,
                Full = new SemanticTokensCapabilityRequestFull{
                    Delta = true,
                },
                Legend = RazorSemanticTokensLegend.Instance,
                Range = true,
            };
        }

        public void SetCapability(SemanticTokensCapability capability)
        {
            _capability = capability;
        }
    }
}
