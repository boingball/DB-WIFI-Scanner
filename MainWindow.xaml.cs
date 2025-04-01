using System;
using System.Windows;
using System.Windows.Threading;

namespace DB_WIFI_Scanner
{
    public partial class MainWindow : Window
    {
        private WifiScanner scanner = new WifiScanner();
        private DispatcherTimer timer;

        public MainWindow()
        {
            InitializeComponent();
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(5);
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            List<WifiNetwork> results;

            if (DeepScanCheckBox.IsChecked == true)
            {
                results = scanner.ScanBssList();
                GhostRadar.UpdateNetworks(results);
            }
            else
            {
                results = scanner.Scan();
                //GhostRadar.UpdateNetworks(results); // hide blips in basic mode
            }

            WifiList.Items.Clear();

            foreach (var net in results)
            {
                string label = DeepScanCheckBox.IsChecked == true
                    ? $"{net.SSID} ({net.SignalQuality}%) RSSI: {net.RSSI} dBm - BSSID: {net.BSSID}"
                    : $"{net.SSID} ({net.SignalQuality}%)";

                WifiList.Items.Add(label);
            }
        }

    }
}
