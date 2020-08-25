// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using MediatR;
using Microsoft.Extensions.Internal;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Models.Proposals;

namespace Microsoft.AspNetCore.Razor.LanguageServer.Semantic.Models
{
    internal class LegacySemanticTokensOptions
    {
        public SemanticTokensLegend Legend { get; set; }

        public bool RangeProvider { get; set; }

        public SemanticTokensDocumentProviderOptions DocumentProvider { get; set; }
    }

    internal class LegacySemanticTokens
    {
        public LegacySemanticTokens(SemanticTokens tokens)
        {
            Data = tokens.Data.Select(d => (uint)d).ToArray();
            ResultId = tokens.ResultId;
        }

        public string ResultId { get; set; }
        public uint[] Data { get; set; }
    }

    internal class SemanticTokensOptions
    {
        public SemanticTokensLegend Legend { get; set; }

        public bool RangeProvider { get; set; }

        public SemanticTokensDocumentProviderOptions DocumentProvider { get; set; }
    }

    internal class LegacySemanticTokensEdit
    {
        public LegacySemanticTokensEdit(SemanticTokensEdit edit)
        {
            Start = edit.Start;
            DeleteCount = edit.DeleteCount;
            Data = edit.Data.Select(d => (uint)d);            
        }

        public int Start { get; set; }
        public int DeleteCount { get; set; }
        public IEnumerable<uint> Data { get; set; }

        public override bool Equals(object obj)
        {
            if ((obj is null && this != null) || !(obj is LegacySemanticTokensEdit other))
            {
                return false;
            }

            var equal = Start.Equals(other.Start);
            equal &= DeleteCount.Equals(other.DeleteCount);
            equal &= (Data is null && other.Data is null) || Enumerable.SequenceEqual(Data, other.Data);

            return equal;
        }

        public override int GetHashCode()
        {
            var combiner = HashCodeCombiner.Start();

            combiner.Add(Start);
            combiner.Add(DeleteCount);
            combiner.Add(Data);

            return combiner.CombinedHash;
        }
    }

    internal class LegacySemanticTokensEditCollection
    {
        public LegacySemanticTokensEditCollection(SemanticTokensDelta delta)
        {
            ResultId = delta.ResultId;
            Edits = delta.Edits.Select(e => new LegacySemanticTokensEdit(e)).ToList();
        }

        public string ResultId { get; set; }
        public IReadOnlyList<LegacySemanticTokensEdit> Edits { get; set; }
    }

    internal struct LegacySemanticTokensOrSemanticTokensEdits
    {
        public LegacySemanticTokensOrSemanticTokensEdits(SemanticTokensFullOrDelta semanticTokensFullOrDelta)
        {
            if (semanticTokensFullOrDelta.IsDelta)
            {
                SemanticTokensEdits = new LegacySemanticTokensEditCollection(semanticTokensFullOrDelta.Delta);
                SemanticTokens = null;
            }
            else
            {
                SemanticTokensEdits = null;
                SemanticTokens = new LegacySemanticTokens(semanticTokensFullOrDelta.Full);
            }
        }

        public LegacySemanticTokensOrSemanticTokensEdits(LegacySemanticTokensEditCollection semanticTokensEdits)
        {
            SemanticTokensEdits = semanticTokensEdits;
            SemanticTokens = null;
        }

        public LegacySemanticTokensOrSemanticTokensEdits(LegacySemanticTokens semanticTokens)
        {
            SemanticTokensEdits = null;
            SemanticTokens = semanticTokens;
        }

        public bool IsSemanticTokens => SemanticTokens != null;
        public LegacySemanticTokens SemanticTokens { get; }

        public bool IsSemanticTokensEdits => SemanticTokensEdits != null;
        public LegacySemanticTokensEditCollection SemanticTokensEdits { get; }

        public static implicit operator LegacySemanticTokensOrSemanticTokensEdits(LegacySemanticTokensEditCollection semanticTokensEdits)
        {
            return new LegacySemanticTokensOrSemanticTokensEdits(semanticTokensEdits);
        }

        public static implicit operator LegacySemanticTokensOrSemanticTokensEdits(LegacySemanticTokens semanticTokens)
        {
            return new LegacySemanticTokensOrSemanticTokensEdits(semanticTokens);
        }
    }

    internal class LegacySemanticTokensEditParams : ITextDocumentIdentifierParams, IRequest<LegacySemanticTokensOrSemanticTokensEdits?>
    {
        public string PreviousResultId { get; set; }

        public TextDocumentIdentifier TextDocument { get; set; }
    }

    internal class LegacySemanticTokensParams : ITextDocumentIdentifierParams, IRequest<LegacySemanticTokens>
    {
        public TextDocumentIdentifier TextDocument { get; set; }
    }

    internal class LegacySemanticTokensRangeParams : ITextDocumentIdentifierParams, IRequest<LegacySemanticTokens>
    {
        public Range Range { get; set; }

        public TextDocumentIdentifier TextDocument { get; set; }
    }
}
