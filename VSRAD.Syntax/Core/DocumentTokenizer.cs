﻿using Microsoft.VisualStudio.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using VSRAD.Syntax.Helpers;
using VSRAD.Syntax.Core.Tokens;
using VSRAD.Syntax.Core.Helper;
using VSRAD.Syntax.Core.Lexer;
using System.Threading;

namespace VSRAD.Syntax.Core
{
    internal class DocumentTokenizer : IDocumentTokenizer
    {
        private readonly TrackingToken.NonOverlappingComparer _comparer;
        private readonly ILexer _lexer;
        private TokenizerCollection CurrentTokens;
        private CancellationTokenSource _cts;

        public ITokenizerResult CurrentResult { get; private set; }
        public event TokenizerUpdatedEventHandler TokenizerUpdated;

        public ITextSnapshot CurrentSnapshot
        {
            get { return _comparer.Version; }
            set { _comparer.Version = value; }
        }

        public DocumentTokenizer(ITextBuffer buffer, ILexer lexer)
        {
            _lexer = lexer;
            _comparer = new TrackingToken.NonOverlappingComparer();
            CurrentSnapshot = buffer.CurrentSnapshot;
            _cts = new CancellationTokenSource();

            Initialize();
            buffer.Changed += BufferChanged;
        }

        public void Initialize() => Rescan();

        public void Rescan()
        {
            var initialTextSegment = new[] { CurrentSnapshot.GetText() };
            var lexerTokens = _lexer.Run(textSegments: initialTextSegment, offset: 0).Select(t => new TrackingToken(CurrentSnapshot, t));

            CurrentTokens = new TokenizerCollection(lexerTokens, _comparer);
            RaiseTokensChanged(CurrentTokens.ToList());
        }

        public RadAsmTokenType GetTokenType(int type) =>
            _lexer.LexerTokenToRadAsmToken(type);

        private void BufferChanged(object src, TextContentChangedEventArgs arg)
        {
            _cts.Cancel();
            _cts = new CancellationTokenSource();
            ApplyTextChanges(arg);
        }

        private void ApplyTextChanges(TextContentChangedEventArgs args) 
        {
            try
            {
                // in some cases the text buffer may cause ContentChanged with 0 changes
                if (args.Changes.Count == 0) return;

                ApplyTextChange(args.Before, args.After, new JoinedTextChange(args.Changes));
            }
            catch (Exception ex)
            {
                Error.LogError(ex, "Document analysis apply changes");
                Initialize();
            }
        }

        private void ApplyTextChange(ITextSnapshot before, ITextSnapshot after, ITextChange change)
        {
            List<TrackingToken> forRemoval = GetInvalidated(before, change);
            // Some of the tokens marked for removal must be deleted before applying a new version,
            // because otherwise some trackingtokens will have broken spans
            int i = 0;
            for (; i < forRemoval.Count; i++)
                CurrentTokens.Remove(forRemoval[i]);
            CurrentSnapshot = after;
            IList<TrackingToken> updated = Rescan(forRemoval, before, change.Delta);
            for (; i < forRemoval.Count; i++)
                CurrentTokens.Remove(forRemoval[i]);
            foreach (var token in updated)
                CurrentTokens.Add(token);
            RaiseTokensChanged(updated);
        }

        private void RaiseTokensChanged(IList<TrackingToken> updated)
        {
            CurrentResult = new TokenizerResult(CurrentSnapshot, tokens: CurrentTokens, updatedTokens: updated);
            TokenizerUpdated?.Invoke(CurrentResult, _cts.Token);
        }

        private List<TrackingToken> GetInvalidated(ITextSnapshot oldSnapshot, ITextChange change) =>
            CurrentTokens.GetInvalidated(oldSnapshot, change.OldSpan);

        private IList<TrackingToken> Rescan(List<TrackingToken> forRemoval, ITextSnapshot oldSnapshot, int delta)
        {
            var invalidatedSpan = InvalidatedSpan(forRemoval, oldSnapshot, delta);
            var invalidatedText = CurrentSnapshot.GetText(invalidatedSpan);

            return RescanCore(forRemoval, invalidatedSpan, invalidatedText);
        }

        private IList<TrackingToken> RescanCore(List<TrackingToken> forRemoval, Span invalidatedSpan, string invalidatedText)
        {
            var newlyCreated = new List<TrackingToken>();
            var removalCandidates = new List<TrackingToken>();
            // this lazy iterator walks tokens that are outside of the initial invalidation span
            var excessText = CurrentTokens.InOrderAfter(CurrentSnapshot, invalidatedSpan.End)
                                 .Select(t => GetTextAndMarkForRemoval(t, ref removalCandidates))
                                 .TakeWhile(s => s != null);
            var tokens = _lexer.Run(new string[] { invalidatedText }.Concat(excessText), invalidatedSpan.Start);
            foreach (var token in tokens)
            {
                newlyCreated.Add(new TrackingToken(CurrentSnapshot, token));
                if (token.Span.End == invalidatedSpan.End)
                    break;
                if (removalCandidates.Count > 0)
                {
                    if (token.Span.End == removalCandidates[removalCandidates.Count - 1].GetEnd(CurrentSnapshot))
                        break;
                }
            }
            AppendInvalidTokens(forRemoval, newlyCreated, removalCandidates);
            return newlyCreated;
        }

        private void AppendInvalidTokens(List<TrackingToken> forRemoval, List<TrackingToken> newlyCreated, List<TrackingToken> removalCandidates)
        {
            if (newlyCreated.Count == 0 || removalCandidates.Count == 0)
                return;

            int end = newlyCreated[newlyCreated.Count - 1].GetEnd(CurrentSnapshot);
            foreach (var token in removalCandidates)
            {
                int tokenEnd = token.GetEnd(CurrentSnapshot);
                if (tokenEnd > end)
                    break;

                forRemoval.Add(token);
                if (tokenEnd == end)
                    break;
            }
        }

        private string GetTextAndMarkForRemoval(TrackingToken current, ref List<TrackingToken> removalCandidates)
        {
            removalCandidates.Add(current);
            var span = current.GetSpan(CurrentSnapshot);

            if (span.End > CurrentSnapshot.Length)
                return null;

            return current.GetText(CurrentSnapshot);
        }

        private Span InvalidatedSpan(IList<TrackingToken> invalid, ITextSnapshot oldSnapshot, int delta)
        {
            // if the set of invalidated tokens is empty, that means we
            // are observing text being inserted into an empty document
            if (invalid.Count == 0)
                return new Span(0, delta);

            int invalidationStart = invalid[0].GetStart(oldSnapshot); // this position is the same in both versions
            int invalidationEnd = GetInvalidationEnd(oldSnapshot, invalid, delta);

            return new Span(invalidationStart, invalidationEnd - invalidationStart);
        }

        private int GetInvalidationEnd(ITextSnapshot oldSnapshot, IList<TrackingToken> invalid, int delta)
        {
            var oldSpan = invalid[invalid.Count - 1].GetSpan(oldSnapshot);
            var newSpan = invalid[invalid.Count - 1].GetSpan(CurrentSnapshot);
            var tokenStartDelta = newSpan.Start - oldSpan.Start;

            return newSpan.End + delta - tokenStartDelta;
        }

        private class JoinedTextChange : ITextChange
        {
            public JoinedTextChange(INormalizedTextChangeCollection changes)
            {
                var oldStart = changes[0].OldSpan.Start;
                var oldEnd = changes[changes.Count - 1].OldEnd;
                OldSpan = new Span(oldStart, oldEnd - oldStart);
                Delta = changes[changes.Count - 1].NewEnd - changes[changes.Count - 1].OldEnd;
            }

            public Span OldSpan { get; }
            public int Delta { get; }

            public int LineCountDelta { get { throw new NotImplementedException(); } }
            public int NewEnd { get { throw new NotImplementedException(); } }
            public int NewLength { get { throw new NotImplementedException(); } }
            public int NewPosition { get { throw new NotImplementedException(); } }
            public Span NewSpan { get { throw new NotImplementedException(); } }
            public string NewText { get { throw new NotImplementedException(); } }
            public int OldEnd { get { throw new NotImplementedException(); } }
            public int OldLength { get { throw new NotImplementedException(); } }
            public int OldPosition { get { throw new NotImplementedException(); } }
            public string OldText { get { throw new NotImplementedException(); } }
        }
    }
}
