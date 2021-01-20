﻿using System.Text.RegularExpressions;
using VSRAD.Package.Utils;

namespace VSRAD.Package.Server
{
    public sealed class BreakStateDispatchParameters
    {
        private static readonly Regex _paramsRegex = new Regex(@"grid_size \((?<gd_x>\d+), (?<gd_y>\d+), (?<gd_z>\d+)\)\s+group_size \((?<gp_x>\d+), (?<gp_y>\d+), (?<gp_z>\d+)\)\s+wave_size (?<wv>\d+)(\s+comment (?<comment>.*))?", RegexOptions.Compiled);
        private const string _exampleParamsContent = @"
grid_size (2048, 1, 1)
group_size (512, 1, 1)
wave_size 64
comment optional comment";

        public uint WaveSize { get; }
        public uint DimX { get; }
        public uint DimY { get; }
        public uint DimZ { get; }
        public uint GroupSize { get; } // TODO: handle 3d group sizes
        public bool NDRange3D { get; }
        public string StatusString { get; }

        private BreakStateDispatchParameters(uint waveSize, uint dimX, uint dimY, uint dimZ, uint groupSize, bool ndRange3d, string statusString)
        {
            WaveSize = waveSize;
            DimX = dimX;
            DimY = dimY;
            DimZ = dimZ;
            GroupSize = groupSize;
            NDRange3D = ndRange3d;
            StatusString = statusString;
        }

        public static Result<BreakStateDispatchParameters> Parse(string contents)
        {
            if (contents == null)
                return (BreakStateDispatchParameters)null;

            var match = _paramsRegex.Match(contents);

            if (!match.Success)
                return new Error("Could not read the dispatch parameters file. The following is an example of the expected file contents:\r\n" + _exampleParamsContent);

            var gridX = uint.Parse(match.Groups["gd_x"].Value);
            var gridY = uint.Parse(match.Groups["gd_y"].Value);
            var gridZ = uint.Parse(match.Groups["gd_z"].Value);
            var groupX = uint.Parse(match.Groups["gp_x"].Value);
            var groupY = uint.Parse(match.Groups["gp_y"].Value);
            var groupZ = uint.Parse(match.Groups["gp_z"].Value);
            var waveSize = uint.Parse(match.Groups["wv"].Value);
            var ndRange3D = gridY > 1 || gridZ > 1;
            var statusString = match.Groups["comment"].Value;

            if (gridX == 0)
                return new Error("Could not read the dispatch parameters file. GridX cannot be zero.");
            if (groupX == 0)
                return new Error("Could not read the dispatch parameters file. GroupX cannot be zero.");

            if (waveSize == 0)
                return new Error("Could not read the dispatch parameters file. WaveSize cannot be zero.");
            if (waveSize > groupX)
                return new Error("Could not read the dispatch parameters file. WaveSize cannot be bigger than GroupX.");

            if (gridY > 1 && groupY == 0)
                return new Error("Could not read the dispatch parameters file. If GridY is greater than one, GroupY cannot be zero.");
            if (gridZ > 1 && groupZ == 0)
                return new Error("Could not read the dispatch parameters file. If GridZ is greater than one, GroupZ cannot be zero.");

            if (groupX > gridX)
                return new Error("Could not read the dispatch parameters file. GroupX cannot be bigger than GridX.");
            if (groupY > gridY)
                return new Error("Could not read the dispatch parameters file. GroupY cannot be bigger than GridY.");
            if (groupZ > gridZ)
                return new Error("Could not read the dispatch parameters file. GroupZ cannot be bigger than GridZ.");

            var dimX = gridX / groupX;
            var dimY = gridY > 1 ? gridY / groupY : 0;
            var dimZ = gridZ > 1 ? gridZ / groupZ : 0;

            return new BreakStateDispatchParameters(waveSize, dimX, dimY, dimZ, groupSize: groupX, ndRange3D, statusString);
        }
    }
}