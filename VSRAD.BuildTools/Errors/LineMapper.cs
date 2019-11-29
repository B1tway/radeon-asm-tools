﻿using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace VSRAD.BuildTools.Errors
{
    public struct LineMarker
    {
        public int PpLine;
        public int SourceLine;
        public string SourceFile;
    }

    public static class LineMapper
    {
        private static readonly Regex LineMarkerRegex = new Regex(@"(?<line>\d+)\s+\""(?<file>.*)\""", RegexOptions.Compiled);

        public static List<LineMarker> MapLines(string preprocessed)
        {
            var lines = preprocessed.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var result = new List<LineMarker>(); ;
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (line.StartsWith("#") || line.StartsWith("//#"))
                {
                    var match = LineMarkerRegex.Match(line);
                    result.Add(new LineMarker
                    {
                        PpLine = i + 1,
                        SourceLine = int.Parse(match.Groups["line"].Value),
                        SourceFile = match.Groups["file"].Value
                    });
                }
            }
            return result;
        }

        public static string MapSourceToHost(string remotePath, string[] projectPaths)
        {
            var remotePathArray = remotePath.Split(new[] { @"\", @"/" }, StringSplitOptions.None);
            Array.Reverse(remotePathArray);
            string probablePath = remotePath;
            int longestMatch = -1;
            foreach (var path in projectPaths)
            {
                var pathArray = path.Split(new[] { @"\", @"/" }, StringSplitOptions.None);
                Array.Reverse(pathArray);
                int matchCount = 0;

                while (matchCount < pathArray.Length && matchCount < remotePathArray.Length && remotePathArray[matchCount] == pathArray[matchCount])
                    matchCount++;

                if (matchCount > longestMatch)
                {
                    probablePath = path;
                    longestMatch = matchCount;
                }
            }
            return probablePath;
        }
    }
}
