﻿using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using System;
using System.Collections.Generic;
using System.Linq;
using VSRAD.Syntax.Helpers;
using VSRAD.Syntax.IntelliSense.Navigation;
using VSRAD.Syntax.IntelliSense.Navigation.NavigationList;
using VSRAD.Syntax.Parser;
using VSRAD.Syntax.Parser.Blocks;
using VSRAD.Syntax.Parser.Tokens;

namespace VSRAD.Syntax.IntelliSense
{
    internal static class IntellisenseTokenDescription
    {
        public static object GetColorizedTokenDescription(NavigationToken token)
        {
            try
            {
                return GetColorizedDescription(token);
            }
            catch (Exception e)
            {
                Error.LogError(e, "Colorized description");
                return null;
            }
        }

        public static object GetColorizedDescription(IEnumerable<NavigationToken> tokens) =>
            tokens.Count() == 1
                ? GetColorizedTokenDescription(tokens.First())
                : GetColorizedTokensDescription(tokens);

        private static object GetColorizedTokensDescription(IEnumerable<NavigationToken> tokens)
        {
            try
            {
                var elements = new List<object>();
                foreach (var navigationToken in tokens)
                {
                    var token = new DefinitionToken(navigationToken);
                    elements.Add(new ClassifiedTextElement(new ClassifiedTextRun(PredefinedClassificationTypeNames.Identifier, token.FilePath)));

                    var analysisToken = navigationToken.AnalysisToken;
                    var typeName = analysisToken.Type.GetName();
                    var nameElement = GetNameElement(analysisToken.Type, navigationToken.GetText());

                    elements.Add(new ContainerElement(
                        ContainerElementStyle.Wrapped,
                        new ClassifiedTextElement(
                            new ClassifiedTextRun(PredefinedClassificationTypeNames.Identifier, $"({typeName}) ")
                        ),
                        nameElement,
                        new ClassifiedTextElement(
                            new ClassifiedTextRun(PredefinedClassificationTypeNames.FormalLanguage, $": {token.LineText}")
                        )
                    ));
                    elements.Add(new ClassifiedTextElement(
                        new ClassifiedTextRun(PredefinedClassificationTypeNames.FormalLanguage, "")
                    ));
                }

                return new ContainerElement(
                    ContainerElementStyle.Stacked,
                    elements);
            }
            catch (Exception e)
            {
                Error.LogError(e, "Colorized description list");
                return null;
            }
        }

        private static object GetColorizedDescription(NavigationToken token)
        {
            var version = token.Snapshot;
            if (!version.TryGetDocumentAnalysis(out var documentAnalysis))
                return null;

            var type = token.AnalysisToken.Type;
            string description = null;
            if (type == RadAsmTokenType.FunctionName
                || type == RadAsmTokenType.GlobalVariable
                || type == RadAsmTokenType.LocalVariable
                || type == RadAsmTokenType.Label)
            {
                var tokenSpan = token.AnalysisToken.TrackingToken.GetSpan(version);
                var line = version.GetLineFromPosition(tokenSpan.Start);
                var tokens = documentAnalysis.GetTokens(new Span(tokenSpan.End, line.EndIncludingLineBreak - tokenSpan.End));

                if (!GetDescriptionFromComment(documentAnalysis, version, tokens, out description))
                {
                    line = version.GetLineFromLineNumber(line.LineNumber - 1);
                    tokens = documentAnalysis.GetTokens(new Span(line.Start, line.EndIncludingLineBreak - line.Start));

                    GetDescriptionFromComment(documentAnalysis, version, tokens, out description);
                }
            }
            else if (type == RadAsmTokenType.Instruction)
            {
                var definition = new DefinitionToken(token);
                description = definition.LineText;
            }

            if (type == RadAsmTokenType.FunctionName)
            {
                var fb = GetFunctionBlockByToken(documentAnalysis, token.AnalysisToken);
                if (fb == null)
                    return null;

                var addBrackets = version.GetAsmType() == AsmType.RadAsm2;
                var nameTextRuns = new List<ClassifiedTextRun>()
                {
                    new ClassifiedTextRun(SyntaxHighlighter.PredefinedClassificationTypeNames.Functions, token.GetText()),
                    new ClassifiedTextRun(PredefinedClassificationTypeNames.Identifier, addBrackets ? "(" : " "),
                };

                var arguments = fb.Tokens
                    .Where(t => t.Type == RadAsmTokenType.FunctionParameter)
                    .ToArray();
                for (int i = 0; i < arguments.Length; i++)
                {
                    if (i == arguments.Length - 1)
                        nameTextRuns.Add(new ClassifiedTextRun(SyntaxHighlighter.PredefinedClassificationTypeNames.Arguments, arguments[i].TrackingToken.GetText(version)));
                    else
                        nameTextRuns.Add(new ClassifiedTextRun(SyntaxHighlighter.PredefinedClassificationTypeNames.Arguments, $"{arguments[i].TrackingToken.GetText(version)}, "));
                }

                if (addBrackets)
                    nameTextRuns.Add(new ClassifiedTextRun(PredefinedClassificationTypeNames.Identifier, ")"));

                return GetDescriptionElement(token.AnalysisToken.Type.GetName(), new ClassifiedTextElement(nameTextRuns), description);
            }
            else if (type == RadAsmTokenType.GlobalVariable || type == RadAsmTokenType.LocalVariable)
            {
                var variable = (VariableToken)token.AnalysisToken;
                var nameTextRuns = new List<ClassifiedTextRun>()
                {
                    new ClassifiedTextRun(PredefinedClassificationTypeNames.Identifier, token.GetText()),
                };

                if (variable.DefaultValue != TrackingToken.Empty)
                {
                    nameTextRuns.Add(new ClassifiedTextRun(PredefinedClassificationTypeNames.FormalLanguage, " = "));
                    nameTextRuns.Add(new ClassifiedTextRun(PredefinedClassificationTypeNames.Number, variable.DefaultValue.GetText(version)));
                }

                return GetDescriptionElement(token.AnalysisToken.Type.GetName(), new ClassifiedTextElement(nameTextRuns), description);
            }
            else
            {
                return GetColorizedDescription(
                    token.AnalysisToken.Type,
                    token.GetText(),
                    description);
            }
        }

        public static object GetColorizedDescription(RadAsmTokenType tokenType, string tokenName, string description = null)
        {
            var typeName = tokenType.GetName();
            var nameElement = GetNameElement(tokenType, tokenName);
            return GetDescriptionElement(typeName, nameElement, description);
        }

        private static ClassifiedTextElement GetNameElement(RadAsmTokenType type, string tokenText)
        {
            switch (type)
            {
                case RadAsmTokenType.FunctionParameter:
                    return new ClassifiedTextElement(new ClassifiedTextRun(SyntaxHighlighter.PredefinedClassificationTypeNames.Arguments, tokenText));
                case RadAsmTokenType.GlobalVariable:
                case RadAsmTokenType.LocalVariable:
                    return new ClassifiedTextElement(new ClassifiedTextRun(PredefinedClassificationTypeNames.Identifier, tokenText));
                case RadAsmTokenType.Label:
                    return new ClassifiedTextElement(new ClassifiedTextRun(SyntaxHighlighter.PredefinedClassificationTypeNames.Labels, tokenText));
                case RadAsmTokenType.Instruction:
                    return new ClassifiedTextElement(new ClassifiedTextRun(SyntaxHighlighter.PredefinedClassificationTypeNames.Instructions, tokenText));
                default:
                    return new ClassifiedTextElement(new ClassifiedTextRun(PredefinedClassificationTypeNames.Other, tokenText));
            }
        }

        private static ContainerElement GetDescriptionElement(string typeName, ClassifiedTextElement nameElement, string description)
        {
            var tokenElement = new ContainerElement(
                ContainerElementStyle.Wrapped,
                new ClassifiedTextElement(
                    new ClassifiedTextRun(PredefinedClassificationTypeNames.Identifier, $"({typeName}) ")
                ),
                nameElement
                );

            if (string.IsNullOrEmpty(description))
                return tokenElement;

            return new ContainerElement(
                    ContainerElementStyle.Stacked,
                    tokenElement,
                    new ClassifiedTextElement(
                        new ClassifiedTextRun(PredefinedClassificationTypeNames.Identifier, "")
                    ),
                    new ClassifiedTextElement(
                        new ClassifiedTextRun(PredefinedClassificationTypeNames.Identifier, description)
                    )
                );
        }

        private static FunctionBlock GetFunctionBlockByToken(DocumentAnalysis documentAnalysis, AnalysisToken functionToken)
        {
            foreach (var block in documentAnalysis.LastParserResult)
            {
                if (block.Type == BlockType.Function)
                {
                    var funcBlock = (FunctionBlock)block;
                    if (funcBlock.Name == functionToken)
                        return funcBlock;
                }
            }

            return null;
        }

        private static bool GetDescriptionFromComment(DocumentAnalysis documentAnalysis, ITextSnapshot version, IEnumerable<TrackingToken> tokens, out string description)
        {
            var commentTokens = tokens.Where(t => t.Type == documentAnalysis.LINE_COMMENT || t.Type == documentAnalysis.BLOCK_COMMENT);

            if (commentTokens.Any())
            {
                description = commentTokens
                    .First()
                    .GetText(version)
                    .Trim(new char[] { '/', '*', ' ', '\r', '\n' })
                    .Replace("*", string.Empty);
                return true;
            }
            else
            {
                description = null;
                return false;
            }
        }
    }
}
