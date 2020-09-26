﻿using System;
using System.Collections.Generic;
using System.Windows.Shapes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using VSRAD.Package.DebugVisualizer.Wavemap;

namespace VSRAD.Package.DebugVisualizer
{
    class WavemapCanvas
    {
        private readonly Canvas _canvas;
        private readonly Rectangle[][] _rectangles = { new Rectangle[200], new Rectangle[200] };
        private readonly WavemapView _wiew;

        public WavemapCanvas(Canvas canvas)
        {
            _canvas = canvas;

            var _data = new uint[7200];
            for (uint i = 3, j = 313; i < 7200; i += 18, j += 313)
                _data[i] = j;

            _wiew = new WavemapView(_data, 6, 3, 12);

            for (int i = 0; i < 200; ++i)
            {
                for (int j = 0; j < 2; ++j)
                {
                    var r = GetWaveRectangleByCoordinates(j, i);
                    _rectangles[j][i] = r;
                    _canvas.Children.Add(r);
                }
            }
        }

        private Rectangle GetWaveRectangleByCoordinates(int row, int column)
        {
            var wave = _wiew[row, column];
            var r = new Rectangle();
            r.ToolTip = new ToolTip() { Content = $"Group: {wave.GroupIdx}\nWave: {wave.WaveIdx}\nLine: {wave.BreakLine}" };
            r.Fill = wave.BreakColor;
            r.Height = 7;
            r.Width = 7;
            r.StrokeThickness = 1;
            r.Stroke = Brushes.Black;
            Canvas.SetLeft(r, 1 + 6 * column);
            Canvas.SetTop(r, 1 + 7 * row);
            return r;
        }
    }
}
