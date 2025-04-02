using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DB_WIFI_Scanner
{
    public partial class MainWindow : Window
    {
        private WifiScanner scanner = new WifiScanner();
        private DispatcherTimer timer;
        private bool useDeepScan = false;
        string selectedBssid = null;
        private bool focusManuallyCleared = false;
        private bool updatingSelectionInternally = false;
        private bool suppressSelectionChanged = false;

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
            List<WifiNetwork> results = DeepScanCheckBox.IsChecked == true
                ? (useDeepScan ? scanner.ScanBssList() : scanner.Scan())
                : scanner.Scan();

            GhostRadar.UpdateNetworks(results);

            // Temporarily stop selection logic while rebuilding
            suppressSelectionChanged = true;

            WifiList.Items.Clear();
            ListBoxItem itemToSelect = null;

            foreach (var net in results)
            {
                string label = DeepScanCheckBox.IsChecked == true
                    ? $"{net.SSID} ({net.SignalQuality}%) RSSI: {net.RSSI} dBm - BSSID: {net.BSSID}"
                    : $"{net.SSID} ({net.SignalQuality}%)";

                var item = new ListBoxItem
                {
                    Content = label,
                    Tag = net
                };

                if (!focusManuallyCleared && net.BSSID == selectedBssid)
                {
                    itemToSelect = item;
                }

                WifiList.Items.Add(item);
            }

            if (itemToSelect != null)
            {
                WifiList.SelectedItem = itemToSelect;
            }

            suppressSelectionChanged = false;
        }

        private void DeepScanCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            useDeepScan = DeepScanCheckBox.IsChecked == true;
            GhostRadar.UpdateNetworks(new List<WifiNetwork>()); // Clear radar while switching mode
        }


        private void WifiList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressSelectionChanged) return;

            if (WifiList.SelectedItem is ListBoxItem item && item.Tag is WifiNetwork net)
            {
                selectedBssid = net.BSSID;
                GhostRadar.FocusOnBssid(selectedBssid);
                focusManuallyCleared = false;
            }
            else
            {
                GhostRadar.FocusOnBssid(null);
                selectedBssid = null;
                focusManuallyCleared = true;
            }
        }
        private void ClearFocusButton_Click(object sender, RoutedEventArgs e)
        {
            WifiList.SelectedItem = null;
            GhostRadar.FocusOnBssid(null);
            selectedBssid = null;
            focusManuallyCleared = true;
        }

    }
}
