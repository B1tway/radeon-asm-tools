﻿using VSRAD.Syntax.Parser;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Shell;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.Windows.Shapes;
using System.Collections.Generic;
using Microsoft.VisualStudio.Text.Formatting;
using VSRAD.Syntax.Parser.Blocks;
using System.Windows.Media;
using Microsoft.VisualStudio.Text;

namespace VSRAD.Syntax.Guides
{
    internal sealed class IndentGuide
    {
        private readonly IWpfTextView _wpfTextView;
        private readonly IParserManager _parserManager;
        private readonly IAdornmentLayer _layer;
        private readonly Canvas _canvas;
        private IBaseParser _currentParser;
        private IList<Line> _currentAdornments;

        public IndentGuide(IWpfTextView textView, IParserManager parserManager)
        {
            _wpfTextView = textView ?? throw new NullReferenceException();
            _parserManager = parserManager ?? throw new NullReferenceException();
            _currentAdornments = new List<Line>();
            _canvas = new Canvas
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            _layer = _wpfTextView.GetAdornmentLayer(Constants.IndentGuideAdornmentLayerName) ?? throw new NullReferenceException();

            _layer.AddAdornment(AdornmentPositioningBehavior.OwnerControlled, null, null, _canvas, CanvasRemoved);
            _parserManager.UpdateParserHandler += ParserCompleted;
            _wpfTextView.LayoutChanged += ParserCompleted;
        }

        private void CanvasRemoved(object tag, UIElement element)
        {
            _layer.AddAdornment(AdornmentPositioningBehavior.OwnerControlled, null, null, _canvas, CanvasRemoved);
        }

        private void UpdateIndentGuides()
        {
            try
            {
                SetupIndentGuides();
            }
            catch (Exception e)
            {
                ActivityLog.LogError(Constants.RadeonAsmSyntaxContentType, e.Message);
            }
        }

        private void SetupIndentGuides()
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _canvas.Visibility = Visibility.Visible;

                var firstVisibleLine = _wpfTextView.TextViewLines.First(line => line.IsFirstTextViewLineForSnapshotLine);
                var lastVisibleLine = _wpfTextView.TextViewLines.Last(line => line.IsLastTextViewLineForSnapshotLine);

                var newSpanElements = _currentParser.ListBlock.Where(block => IsInVisualBuffer(block, firstVisibleLine, lastVisibleLine)).ToList();
                var updatedIndentGuides = GetUpdatedIndentGuides(newSpanElements, firstVisibleLine, lastVisibleLine);

                ClearAndUpdateCurrentGuides(updatedIndentGuides);
            });
        }

        private bool IsInVisualBuffer(IBaseBlock block, ITextViewLine firstVisibleLine, ITextViewLine lastVisibleLine)
        {
            bool isOnStart = block.BlockSpan.Start <= lastVisibleLine.End;
            bool isOnEnd = block.BlockSpan.End >= firstVisibleLine.Start;

            return isOnStart && isOnEnd;
        }

        private IEnumerable<Line> GetUpdatedIndentGuides(IEnumerable<IBaseBlock> blocks, ITextViewLine firstVisibleLine, ITextViewLine lastVisibleLine)
        {
            double horizontalOffset = firstVisibleLine.TextLeft;
            double spaceWidth = firstVisibleLine.VirtualSpaceWidth;

            foreach (var block in blocks)
            {
                var span = block.BlockSpan;
                var viewLineStart = _wpfTextView.GetTextViewLineContainingBufferPosition(span.Start);
                var viewLineEnd = _wpfTextView.GetTextViewLineContainingBufferPosition(span.End);

                var indentStart = IndentValue(span.Start);
                var leftOffset = indentStart * spaceWidth + horizontalOffset;
                var brush = Brushes.White;

                yield return new Line()
                {
                    Width = 30.5,
                    Stroke = Brushes.White,
                    StrokeDashArray = new DoubleCollection() { 2 },
                    X1 = leftOffset,
                    X2 = leftOffset,
                    Y1 = viewLineStart.Bottom,
                    Y2 = (viewLineEnd.Top != 0) ? viewLineEnd.Top : _wpfTextView.ViewportBottom,
                };
            }
        }

        private int IndentValue(SnapshotPoint point)
        {
            var count = 0;
            var line = point.Snapshot.GetLineFromPosition(point);
            var lineText = line.GetText();
            foreach (var ch in lineText)
            {
                if (ch == ' ') count++;
                else if (ch == '\t') count += 4;
                else break;
            }

            return count;
        }

        private void ClearAndUpdateCurrentGuides(IEnumerable<Line> newIndentGuides)
        {
            foreach (var oldIndentGuide in _currentAdornments)
            {
                _canvas.Children.Remove(oldIndentGuide);
            }

            _currentAdornments = newIndentGuides.ToList();

            foreach (var newIndentGuide in _currentAdornments)
            {
                _canvas.Children.Add(newIndentGuide);
            }
        }

        private void ParserCompleted(object actualParser, object _)
        {
            _currentParser = _parserManager.ActualParser;

            UpdateIndentGuides();
        }
    }
}