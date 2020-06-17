﻿using Microsoft.VisualStudio.Text;

namespace VSRAD.Syntax.Helpers
{
    internal enum AsmType
    {
        RadAsm = 1,
        RadAsm2 = 2,
        RadAsmDoc = 3,
        Unknown = 4,
    }

    internal static class TextSnapshotExtension
    {
        internal static AsmType GetAsmType(this ITextSnapshot textSnapshot)
        {
            if (textSnapshot.ContentType.IsOfType(Constants.RadeonAsmDocumentationContentType))
                return AsmType.RadAsmDoc;
            if (textSnapshot.ContentType.IsOfType(Constants.RadeonAsm2SyntaxContentType))
                return AsmType.RadAsm2;
            if (textSnapshot.ContentType.IsOfType(Constants.RadeonAsmSyntaxContentType))
                return AsmType.RadAsm;

            return AsmType.Unknown;
        }
    }
}
