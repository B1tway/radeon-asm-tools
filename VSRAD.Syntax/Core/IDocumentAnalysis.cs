﻿using Microsoft.VisualStudio.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VSRAD.Syntax.Core
{
    public delegate void AnalysisUpdatedEventHandler(IAnalysisResult analysisResult, CancellationToken cancellationToken);

    public interface IDocumentAnalysis
    {
        IAnalysisResult CurrentResult { get; }
        Task<IAnalysisResult> GetAnalysisResultAsync(ITextSnapshot textSnapshot);

        event AnalysisUpdatedEventHandler AnalysisUpdated;
    }
}
