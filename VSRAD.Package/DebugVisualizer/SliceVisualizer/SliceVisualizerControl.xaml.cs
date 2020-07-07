﻿using System.ComponentModel;
using System.Windows.Controls;
using VSRAD.Package.ProjectSystem;
using VSRAD.Package.ToolWindows;

namespace VSRAD.Package.DebugVisualizer.SliceVisualizer
{
    public sealed partial class SliceVisualizerControl : UserControl, IDisposableToolWindow
    {
        private readonly SliceVisualizerTable _table;
        private readonly SliceVisualizerContext _context;

        public SliceVisualizerControl(IToolWindowIntegration integration)
        {
            _context = integration.GetSliceVisualizerContext();
            DataContext = _context;
            PropertyChangedEventManager.AddHandler(_context.Options.SliceVisualizerOptions, SliceVisualizerOptionChanged, "");
            InitializeComponent();

            var tableFontAndColor = new FontAndColorProvider();
            _table = new SliceVisualizerTable(_context, tableFontAndColor);
            TableHost.Setup(_table);
        }
    }
}
