using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DB_WIFI_Scanner
{
    public partial class GhostRadarControl : UserControl
    {
        private readonly System.Timers.Timer refreshTimer;
        private readonly Random rng = new Random();
        private List<WifiNetwork> networks = new List<WifiNetwork>();
        private const int RadarRadius = 180;
        private const int CenterX = 200;
        private const int CenterY = 200;
        public CheckBox HyperScanCheckBox => HyperScanCheckBoxInternal;
        private System.Windows.Threading.DispatcherTimer sweepTimer;
        private System.Timers.Timer scanTriggerTimer;
        private Line sweepBeam;

        public GhostRadarControl()
        {
            InitializeComponent();
            DrawGrid();
            StartSweepAnimation();

            refreshTimer = new System.Timers.Timer(1000);
            refreshTimer.Interval = HyperScanCheckBoxInternal.IsChecked == true ? 300 : 1000;
            refreshTimer.Elapsed += (s, e) =>
            {
                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

                Dispatcher.Invoke(() =>
                {
                    if (!IsLoaded) return;
                    DrawBlips();
                });
            };
            refreshTimer.Start();
            scanTriggerTimer = new System.Timers.Timer(5000); // every 5 sec
            scanTriggerTimer.Elapsed += (s, e) => ForceWifiRescan();
            scanTriggerTimer.Start();

            this.Unloaded += GhostRadarControl_Unloaded;
        }
        private void ForceWifiRescan()
        {
            try
            {
                uint version;
                IntPtr clientHandle;

                if (WifiScanner.WlanOpenHandle(2, IntPtr.Zero, out version, out clientHandle) != 0)
                    return;

                IntPtr interfaceList;
                if (WifiScanner.WlanEnumInterfaces(clientHandle, IntPtr.Zero, out interfaceList) != 0)
                {
                    WifiScanner.WlanCloseHandle(clientHandle, IntPtr.Zero);
                    return;
                }

                int headerSize = Marshal.SizeOf(typeof(uint)) * 2;
                int ifaceSize = Marshal.SizeOf(typeof(WifiScanner.WLAN_INTERFACE_INFO));
                int ifaceCount = Marshal.ReadInt32(interfaceList);

                for (int i = 0; i < ifaceCount; i++)
                {
                    IntPtr ifacePtr = new IntPtr(interfaceList.ToInt64() + headerSize + i * ifaceSize);
                    var iface = Marshal.PtrToStructure<WifiScanner.WLAN_INTERFACE_INFO>(ifacePtr);

                    WifiScanner.WlanScan(clientHandle, ref iface.InterfaceGuid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                }

                WifiScanner.WlanCloseHandle(clientHandle, IntPtr.Zero);
            }
            catch
            {
                // ignore scan failures
            }
        }

        private void HyperScanCheckBoxChanged(object sender, RoutedEventArgs e)
        {
            refreshTimer.Interval = HyperScanCheckBoxInternal.IsChecked == true ? 300 : 1000;
        }

        private void GhostRadarControl_Unloaded(object sender, RoutedEventArgs e)
        {
            refreshTimer?.Stop();
            sweepTimer?.Stop();
            scanTriggerTimer?.Stop();

        }

        private void StartSweepAnimation()
        {
            sweepTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(30)
            };

            double angle = 0;
            sweepTimer.Tick += (s, e) =>
            {
                angle += 2;
                if (angle >= 360) angle = 0;

                double radians = angle * Math.PI / 180;
                double x = CenterX + RadarRadius * Math.Cos(radians);
                double y = CenterY + RadarRadius * Math.Sin(radians);

                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

                Dispatcher.Invoke(() =>
                {
                    if (!IsLoaded || sweepBeam == null) return;

                    sweepBeam.X1 = CenterX;
                    sweepBeam.Y1 = CenterY;
                    sweepBeam.X2 = x;
                    sweepBeam.Y2 = y;
                });
            };

            sweepTimer.Start();
        }

        public void UpdateNetworks(List<WifiNetwork> latest)
        {
            networks = latest;
        }

        private void DrawGrid()
        {
            RadarCanvas.Children.Clear();

            // Draw concentric circles
            for (int r = 30; r <= RadarRadius; r += 30)
            {
                var circle = new Ellipse
                {
                    Width = r * 2,
                    Height = r * 2,
                    Stroke = Brushes.Green,
                    StrokeThickness = 0.5,
                    Opacity = 0.2
                };
                Canvas.SetLeft(circle, CenterX - r);
                Canvas.SetTop(circle, CenterY - r);
                RadarCanvas.Children.Add(circle);
            }

            // Draw crosshairs
            for (int i = 0; i < 360; i += 30)
            {
                double angle = i * Math.PI / 180;
                double x = CenterX + RadarRadius * Math.Cos(angle);
                double y = CenterY + RadarRadius * Math.Sin(angle);
                var line = new Line
                {
                    X1 = CenterX,
                    Y1 = CenterY,
                    X2 = x,
                    Y2 = y,
                    Stroke = Brushes.Green,
                    StrokeThickness = 0.5,
                    Opacity = 0.2
                };
                RadarCanvas.Children.Add(line);
            }

            // Add sweep beam
            sweepBeam = new Line
            {
                Stroke = Brushes.Lime,
                StrokeThickness = 2,
                Opacity = 0.3
            };
            RadarCanvas.Children.Add(sweepBeam);
        }

        private double GetStableAngleFromBssid(string bssid)
        {
            if (string.IsNullOrWhiteSpace(bssid)) return rng.NextDouble() * 2 * Math.PI;

            int hash = bssid.GetHashCode();
            double normalized = (hash & 0x7FFFFFFF) / (double)int.MaxValue; // 0 to 1
            return normalized * 2 * Math.PI;
        }

        private void DrawBlips()
        {
            // Track previous SSIDs to detect removals
            var currentKeys = networks.Select(n => n.BSSID).ToHashSet();
            var previousLabels = RadarCanvas.Children.OfType<TextBlock>().Where(e => (string)e.Tag == "label").ToList();
            var previousBlips = RadarCanvas.Children.OfType<Ellipse>().Where(e => (string)e.Tag == "blip").ToList();

            foreach (var blip in previousBlips) RadarCanvas.Children.Remove(blip);
            foreach (var label in previousLabels) RadarCanvas.Children.Remove(label);

            GhostListBox.Items.Clear();

            if (networks == null || networks.Count == 0) return;

            int index = 1;
            foreach (var net in networks)
            {
                int rssi = net.RSSI;
                double distance = MapRssiToRadius(rssi) + (rng.NextDouble() * 2 - 1); // ±1 pixel ghost drift
                double angle = GetStableAngleFromBssid(net.BSSID);

                double x = CenterX + distance * Math.Cos(angle);
                double y = CenterY + distance * Math.Sin(angle);

                bool isClose = rssi > -35;

                var blip = new Ellipse
                {
                    Width = isClose ? 12 : 8,
                    Height = isClose ? 12 : 8,
                    Fill = isClose ? Brushes.Red : Brushes.LimeGreen,
                    Stroke = isClose ? Brushes.OrangeRed : Brushes.White,
                    StrokeThickness = 0.5,
                    Tag = "blip"
                };

                Canvas.SetLeft(blip, x - blip.Width / 2);
                Canvas.SetTop(blip, y - blip.Height / 2);
                RadarCanvas.Children.Add(blip);

                var label = new TextBlock
                {
                    Text = $"AP-{index}",
                    Foreground = Brushes.Lime,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Tag = "label"
                };
                Canvas.SetLeft(label, x + 6);
                Canvas.SetTop(label, y - 6);
                RadarCanvas.Children.Add(label);

                GhostListBox.Items.Add($"AP-{index}: {net.SSID}");

                if (isClose)
                {
                    TriggerGhostCapture();
                }

                index++;
            }
        }


        private double MapRssiToRadius(int rssi)
        {
            // RSSI Range: -90 (far) to -35 (very close)
            const int minRSSI = -90;
            const int maxRSSI = -35;

            // Clamp RSSI to expected range
            int clamped = Math.Max(minRSSI, Math.Min(maxRSSI, rssi));

            // Invert so that stronger signal = smaller radius (closer to center)
            double ratio = (clamped - maxRSSI) / (double)(minRSSI - maxRSSI); // 0 = -35 (center), 1 = -90 (edge)

            // Push it inward slightly to avoid edge
            return 5 + (ratio * (RadarRadius - 5));
        }


        private void TriggerGhostCapture()
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

            Dispatcher.Invoke(() =>
            {
                AlertOverlay.Opacity = 0.6;
                CaptureText.Visibility = Visibility.Visible;
            });

            var timer = new System.Timers.Timer(1000);
            timer.Elapsed += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    AlertOverlay.Opacity = 0;
                    CaptureText.Visibility = Visibility.Collapsed;
                });
                timer.Stop();
            };
            timer.Start();
        }
    }
}
