// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Razor.LanguageServer.Common;
using Microsoft.AspNetCore.Razor.LanguageServer.Semantic.Models;
using OmniSharp.Extensions.JsonRpc;

namespace Microsoft.AspNetCore.Razor.LanguageServer.Semantic.Interfaces
{
    [Serial, Method(LanguageServerConstants.LegacyRazorSemanticTokensEditEndpoint)]
    internal interface ILegacySemanticTokensEditHandler :
        IJsonRpcRequestHandler<LegacySemanticTokensEditParams, LegacySemanticTokensOrSemanticTokensEdits?>
    {
    }

    [Serial, Method(LanguageServerConstants.LegacyRazorSemanticTokensEndpoint)]
    internal interface ILegacySemanticTokensHandler :
        IJsonRpcRequestHandler<LegacySemanticTokensParams, LegacySemanticTokens>
    {
    }

    [Serial, Method(LanguageServerConstants.LegacyRazorSemanticTokensRangeEndpoint)]
    internal interface ILegacySemanticTokensRangeHandler :
        IJsonRpcRequestHandler<LegacySemanticTokensRangeParams, LegacySemanticTokens>
    {
    }
}
