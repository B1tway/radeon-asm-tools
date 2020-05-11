﻿using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VSRAD.Syntax.Helpers;
using VSRAD.Syntax.Options;
using VSRAD.Syntax.Parser;
using VSRAD.Syntax.Parser.Tokens;

namespace VSRAD.Syntax.IntelliSense.Completion
{
    internal sealed class ScopeTokenCompletionSource : BasicCompletionSource
    {
        private static readonly ImageElement GlobalVariableIcon = GetImageElement(KnownImageIds.GlobalVariable);
        private static readonly ImageElement LocalVariableIcon = GetImageElement(KnownImageIds.LocalVariable);
        private static readonly ImageElement ArgumentIcon = GetImageElement(KnownImageIds.Parameter);
        private static readonly ImageElement LabelIcon = GetImageElement(KnownImageIds.Label);
        private readonly IDictionary<TokenType, IEnumerable<KeyValuePair<IBaseToken, CompletionItem>>> _completions;

        private bool _autocompleteLabels;
        private bool _autocompleteVariables;

        public ScopeTokenCompletionSource(
            OptionsProvider optionsProvider, 
            IParserManager parserManager) : base(optionsProvider, parserManager)
        {
            _completions = new Dictionary<TokenType, IEnumerable<KeyValuePair<IBaseToken, CompletionItem>>>();
            DisplayOptionsUpdated(optionsProvider);
        }

        public override Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger, SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            if (ParserManager.ActualParser == null || ParserManager.ActualParser.PointInComment(triggerLocation))
                return Task.FromResult<CompletionContext>(null);

            var completions = Enumerable.Empty<CompletionItem>();
            if (_autocompleteLabels)
                completions = completions
                    .Concat(GetScopedCompletions(triggerLocation, TokenType.Label, LabelIcon));
            if (_autocompleteVariables)
                completions = completions
                    .Concat(GetScopedCompletions(triggerLocation, TokenType.GlobalVariable, GlobalVariableIcon))
                    .Concat(GetScopedCompletions(triggerLocation, TokenType.LocalVariable, LocalVariableIcon))
                    .Concat(GetScopedCompletions(triggerLocation, TokenType.Argument, ArgumentIcon));

            return Task.FromResult(completions.Any() ? new CompletionContext(completions.OrderBy(c => c.DisplayText).ToImmutableArray()) : null);
        }

        public override Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
        {
            if (TryGetDescription(TokenType.Label, item, out var description))
                return Task.FromResult(description);
            if (TryGetDescription(TokenType.GlobalVariable, item, out description))
                return Task.FromResult(description);
            if (TryGetDescription(TokenType.LocalVariable, item, out description))
                return Task.FromResult(description);
            if (TryGetDescription(TokenType.Argument, item, out description))
                return Task.FromResult(description);

            return Task.FromResult((object)string.Empty);
        }

        protected override void DisplayOptionsUpdated(OptionsProvider options)
        {
            if (!(_autocompleteLabels = options.AutocompleteLabels))
                _completions.Remove(TokenType.Label);
            if (!(_autocompleteVariables = options.AutocompleteVariables))
            {
                _completions.Remove(TokenType.LocalVariable);
                _completions.Remove(TokenType.Argument);
                _completions.Remove(TokenType.GlobalVariable);
            }
        }

        private bool TryGetDescription(TokenType tokenType, CompletionItem item, out object description)
        {
            try
            {
                if (_completions.TryGetValue(tokenType, out var pairs)
                    && pairs.Select(p => p.Value.DisplayText).Contains(item.DisplayText))
                {
                    description = IntellisenseTokenDescription.GetColorizedDescription(pairs.Single(p => p.Value.DisplayText == item.DisplayText).Key);
                    return true;
                }
            }
            catch (Exception e)
            {
                Error.LogError(e);
            }

            description = null;
            return false;
        }

        private ImmutableArray<CompletionItem> GetScopedCompletions(SnapshotPoint triggerPoint, TokenType type, ImageElement icon)
        {
            var scopedCompletions = ImmutableArray<CompletionItem>.Empty;
            var parser = ParserManager.ActualParser;

            if (parser == null)
                return scopedCompletions;

            var scopedCompletionPairs = parser
                .GetScopedTokens(triggerPoint, type)
                .Select(t => new KeyValuePair<IBaseToken, CompletionItem>(t, new CompletionItem(t.TokenName, this, icon)));

            _completions[type] = scopedCompletionPairs;
            return scopedCompletionPairs
                .Select(p => p.Value)
                .ToImmutableArray();
        }
    }
}