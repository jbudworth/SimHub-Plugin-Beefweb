using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SimHub.Plugin.Beefweb
{
    public partial class SettingsControl : UserControl
    {
        private readonly BeefwebPlugin _plugin;
        private readonly DispatcherTimer _statusTimer;

        public SettingsControl(BeefwebPlugin plugin)
        {
            InitializeComponent();
            _plugin = plugin;

            LoadFromSettings();

            StatusText.Text = _plugin.GetNowPlayingSummary();

            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statusTimer.Tick += (s, e) => StatusText.Text = _plugin.GetNowPlayingSummary();
            _statusTimer.Start();

            Unloaded += (s, e) => _statusTimer.Stop();
        }

        private void LoadFromSettings()
        {
            var settings = _plugin.Settings;

            HostTextBox.Text = settings.Host;
            PortTextBox.Text = settings.Port.ToString();
            UseAuthCheckbox.IsChecked = settings.UseAuthentication;
            UsernameTextBox.Text = settings.Username;
            PasswordBox.Password = settings.Password;
            PollingSlider.Value = settings.PollingIntervalMs;
            SeekStepSlider.Value = settings.SeekStepSeconds;
            VolumeStepSlider.Value = settings.VolumeStepPercent;

            AuthPanel.IsEnabled = settings.UseAuthentication;
        }

        private void UseAuthCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            AuthPanel.IsEnabled = UseAuthCheckbox.IsChecked == true;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var settings = _plugin.Settings;

            settings.Host = HostTextBox.Text.Trim();
            settings.Port = int.TryParse(PortTextBox.Text, out var port) ? port : settings.Port;
            settings.UseAuthentication = UseAuthCheckbox.IsChecked == true;
            settings.Username = UsernameTextBox.Text.Trim();
            settings.Password = PasswordBox.Password;
            settings.PollingIntervalMs = (int)PollingSlider.Value;
            settings.SeekStepSeconds = (int)SeekStepSlider.Value;
            settings.VolumeStepPercent = VolumeStepSlider.Value;

            _plugin.SaveSettings();

            StatusText.Text = "Settings saved.";
        }

        private async void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Testing...";
            var result = await _plugin.TestConnectionAsync();
            StatusText.Text = result;
        }
    }
}
