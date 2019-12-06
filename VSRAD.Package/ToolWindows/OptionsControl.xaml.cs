﻿using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using VSRAD.Package.Options;
using VSRAD.Package.ProjectSystem.Macros;
using VSRAD.Package.Server;
using VSRAD.Package.Utils;

namespace VSRAD.Package.ToolWindows
{
    public partial class OptionsControl : UserControl
    {
        public sealed class Context : DefaultNotifyPropertyChanged
        {
            public ProjectOptions Options { get; }
            public IReadOnlyList<string> ProfileNames => Options.Profiles.Keys.ToList();

            private string _disconnectLabel = "Disconnected";
            public string DisconnectLabel { get => _disconnectLabel; set => SetField(ref _disconnectLabel, value); }

            private string _connectionInfo = "";
            public string ConnectionInfo { get => _connectionInfo; set => SetField(ref _connectionInfo, value); }

            public ICommand DisconnectCommand { get; }

            private readonly ICommunicationChannel _channel;

            public Context(ProjectOptions options, ICommunicationChannel channel)
            {
                Options = options;
                Options.Profiles.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(Options.Profiles.Keys)) RaisePropertyChanged(nameof(ProfileNames)); };
                _channel = channel;
                _channel.ConnectionStateChanged += ConnectionStateChanged;
                DisconnectCommand = new WpfDelegateCommand((_) => _channel.ForceDisconnect(), isEnabled: false);
                ConnectionStateChanged();
            }

            private void ConnectionStateChanged()
            {
                DisconnectLabel = _channel.ConnectionState == ClientState.Connected ? "Disconnect"
                                : _channel.ConnectionState == ClientState.Connecting ? "Connecting..." : "Disconnected";
                ConnectionInfo = _channel.ConnectionOptions.ToString();
                ((WpfDelegateCommand)DisconnectCommand).IsEnabled = _channel.ConnectionState == ClientState.Connected;
            }
        }

        private readonly ProjectOptions _projectOptions;
        private readonly MacroEditManager _macroEditor;

        public OptionsControl(IToolWindowIntegration integration)
        {
            _projectOptions = integration.ProjectOptions;
            _macroEditor = integration.GetExport<MacroEditManager>();
            DataContext = new Context(integration.ProjectOptions, integration.GetExport<ICommunicationChannel>());
            InitializeComponent();
            ColoringRegionsGrid.PreviewMouseWheel += (s, e) =>
            {
                if (ColoringRegionsGrid.IsMouseOver)
                    ControlScrollViewer.ScrollToVerticalOffset(ControlScrollViewer.VerticalOffset - e.Delta * 0.125);
            };
        }

        // TODO: can freeze here
        private void EditProfiles(object sender, System.Windows.RoutedEventArgs e)
        {
            new ProjectSystem.Profiles.ProfileOptionsWindow(_macroEditor, _projectOptions).ShowDialog();
        }
    }
}
