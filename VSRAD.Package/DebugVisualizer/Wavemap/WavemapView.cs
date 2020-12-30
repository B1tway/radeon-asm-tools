﻿using System.Collections.Generic;
using System.Data;
using System.Drawing;

namespace VSRAD.Package.DebugVisualizer.Wavemap
{
#pragma warning disable CA1815 // the comparing of this structs is not a case, so disable warning that tells us to implement Equals()
    public struct WaveInfo
    {
        public Color BreakColor;
        public uint BreakLine;
        public int GroupIdx;
        public int WaveIdx;
        public bool IsVisible;
    }
#pragma warning restore CA1815

    class BreakpointColorManager
    {
        private readonly Dictionary<uint, Color> _breakpointColorMapping = new Dictionary<uint, Color>();
        private readonly Color[] _colors = new Color[] { Color.FromArgb(181, 137, 0), Color.FromArgb(203, 75, 22), Color.FromArgb(108, 113, 196), Color.FromArgb(42, 161, 152), Color.FromArgb(133, 153, 0) };
        private int _currentColorIndex;

        private Color GetNextColor()
        {
            if (_currentColorIndex == _colors.Length) _currentColorIndex = 0;
            return _colors[_currentColorIndex++];
        }

        public Color GetColorForBreakpoint(uint breakLine)
        {
            if (_breakpointColorMapping.TryGetValue(breakLine, out var color))
            {
                return color;
            }
            else
            {
                var c = GetNextColor();
                _breakpointColorMapping.Add(breakLine, c);
                return c;
            }
        }
    }

    public sealed class WavemapView
    {
        private readonly int _waveSize;
        private readonly int _laneDataSize;
        public int WavesPerGroup { get; }
        public int GroupCount { get; }

        private readonly uint[] _data;

        private readonly BreakpointColorManager _colorManager;

        public WavemapView(uint[] data, int waveSize, int laneDataSize, int groupSize, int groupCount)
        {
            _data = data;
            _waveSize = waveSize;
            _laneDataSize = laneDataSize;
            WavesPerGroup = groupSize / waveSize;
            GroupCount = groupCount;
            _colorManager = new BreakpointColorManager();
        }

        private uint GetBreakpointLine(int waveIndex)
        {
            var breakIndex = waveIndex * _waveSize * _laneDataSize + _laneDataSize; // break line is in the first lane of system watch
            return _data[breakIndex];
        }

        private bool IsValidWave(int row, int column) =>
            GetWaveFlatIndex(row, column) * _waveSize * _laneDataSize + _laneDataSize < _data.Length && row < WavesPerGroup;

        private int GetWaveFlatIndex(int row, int column) => column * WavesPerGroup + row;

        private WaveInfo GetWaveInfoByRowAndColumn(int row, int column)
        {
            if (!IsValidWave(row, column))
                return new WaveInfo
                {
                    BreakColor = Color.FromArgb(0, 0, 0, 0),
                    BreakLine = 0,
                    GroupIdx = 0,
                    WaveIdx = 0,
                    IsVisible = false
                };


            var flatIndex = GetWaveFlatIndex(row, column);
            var breakLine = GetBreakpointLine(flatIndex);

            return new WaveInfo
            {
                BreakColor = _colorManager.GetColorForBreakpoint(breakLine),
                BreakLine = breakLine,
                GroupIdx = column,
                WaveIdx = row,
                IsVisible = true
            };
        }

        public WaveInfo this[int row, int column]
        {
            get => GetWaveInfoByRowAndColumn(row, column);
        }
    }
}
