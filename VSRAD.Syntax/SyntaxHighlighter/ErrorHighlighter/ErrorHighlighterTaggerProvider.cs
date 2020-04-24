﻿using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace VSRAD.Syntax.SyntaxHighlighter.ErrorHighlighter
{
    [Export(typeof(IViewTaggerProvider))]
    [ContentType(Constants.RadeonAsmSyntaxContentType)]
    [TagType(typeof(IErrorTag))]
    internal sealed class ErrorHighlighterTaggerProvider : IViewTaggerProvider
    {
        public delegate void ErrorsUpdateDelegate(IReadOnlyDictionary<string, List<(int line, int column, string message)>> errors);

        public event ErrorsUpdateDelegate ErrorsUpdated;

        private readonly DTE2 _dte;
        private readonly EnvDTE.BuildEvents _buildEvents;

        [ImportingConstructor]
        public ErrorHighlighterTaggerProvider(SVsServiceProvider serviceProvider)
        {
            _dte = serviceProvider.GetService(typeof(SDTE)) as DTE2;
            _buildEvents = _dte.Events.BuildEvents;
            _buildEvents.OnBuildProjConfigDone += ProjectBuildDone;
        }

        private void ProjectBuildDone(string project, string projectConfig, string platform, string solutionConfig, bool success)
        {
            var errors = new Dictionary<string, List<(int line, int column, string message)>>();
            // the list doesn't contain hidden elements and we can’t affect it.
            // But we can show it and then return the state
            var showError = _dte.ToolWindows.ErrorList.ShowErrors;
            var showWarning = _dte.ToolWindows.ErrorList.ShowWarnings;
            _dte.ToolWindows.ErrorList.ShowErrors = true;
            _dte.ToolWindows.ErrorList.ShowWarnings = true;

            var errorList = _dte.ToolWindows.ErrorList.ErrorItems;
            for (int i = 1; i <= errorList.Count; i++)
            {
                var error = errorList.Item(i);

                if (!errors.ContainsKey(error.FileName))
                    errors[error.FileName] = new List<(int line, int column, string message)>();

                errors[error.FileName].Add((error.Line, error.Column, error.Description));
            }
            _dte.ToolWindows.ErrorList.ShowErrors = showError;
            _dte.ToolWindows.ErrorList.ShowWarnings = showWarning;
            ErrorsUpdated?.Invoke(errors);
        }

        public ITagger<T> CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag
        {
            if (textView.TextBuffer != buffer)
                return null;

            return new ErrorHighlighterTagger(this, textView, buffer) as ITagger<T>;
        }
    }
}
