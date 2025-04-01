using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
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
        private Dictionary<string, Point> previousBlipPositions = new();
        private const double InterpolationSpeed = 0.15; // tweak for speed
        public CheckBox HyperScanCheckBox => HyperScanCheckBoxInternal;
        private System.Windows.Threading.DispatcherTimer sweepTimer;
        private System.Timers.Timer scanTriggerTimer;
        private Line sweepBeam;
        bool ghostClose = false;
        private Dictionary<string, string> knownGhosts = new Dictionary<string, string>();
        private Dictionary<string, List<Point>> blipTrails = new();
        private const int MaxTrailLength = 5;

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

            RssiThresholdSlider.ValueChanged += (s, e) =>
            {
                RssiValueText.Text = $"{(int)RssiThresholdSlider.Value} dBm";
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

        private Point Lerp(Point from, Point to)
        {
            return new Point(
                from.X + (to.X - from.X) * InterpolationSpeed,
                from.Y + (to.Y - from.Y) * InterpolationSpeed
            );
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

        private int GetCaptureThreshold()
        {
            return RssiThresholdSlider != null ? (int)RssiThresholdSlider.Value : -30;
        }

        private void DrawBlips()
        {
            if (knownGhosts == null)
                knownGhosts = new Dictionary<string, string>();

            var currentKeys = networks.Select(n => n.BSSID).ToHashSet();
            var previousLabels = RadarCanvas.Children.OfType<TextBlock>().Where(e => (string)e.Tag == "label").ToList();
            var previousBlips = RadarCanvas.Children.OfType<Ellipse>().Where(e => (string)e.Tag == "blip").ToList();
            var closestNet = networks.OrderByDescending(n => n.RSSI).FirstOrDefault(); // RSSI is in dBm: higher = stronger
            string closestBssid = closestNet?.BSSID;

            foreach (var blip in previousBlips) RadarCanvas.Children.Remove(blip);
            foreach (var label in previousLabels) RadarCanvas.Children.Remove(label);

            GhostListBox.Items.Clear();

            bool ghostClose = false;
            string detectedApLabel = null;

            foreach (var net in networks)
            {
                int rssi = net.RSSI;
                double distance = MapRssiToRadius(rssi) + (rng.NextDouble() * 2 - 1);
                double angle = GetStableAngleFromBssid(net.BSSID);
                double x = CenterX + distance * Math.Cos(angle);
                double y = CenterY + distance * Math.Sin(angle);

                if (!knownGhosts.ContainsKey(net.BSSID))
                {
                    knownGhosts[net.BSSID] = $"AP-{knownGhosts.Count + 1}";
                }

                string apLabel = knownGhosts[net.BSSID];

                Brush fill;
                Brush stroke = Brushes.White;

                int threshold = GetCaptureThreshold();
                if (rssi > threshold)
                {
                    fill = Brushes.Red;
                    stroke = Brushes.OrangeRed;
                    ghostClose = true;
                    detectedApLabel = apLabel;
                }
                else if (rssi > threshold - 5)
                    fill = Brushes.Orange;
                else if (rssi > threshold - 20)
                    fill = Brushes.YellowGreen;
                else if (rssi > threshold - 30)
                    fill = Brushes.LimeGreen;
                else if (rssi > threshold - 40)
                    fill = Brushes.Teal;
                else
                    fill = Brushes.DarkSlateGray;

                bool isClosest = net.BSSID == closestBssid;

                // Fake '3D' projection for closest AP
                if (net.BSSID == closestBssid)
                {
                    double forwardBias = Math.PI / 2; // aim downward
                    angle = (angle + forwardBias) / 2.0;
                }

                var blip = new Ellipse
                {
                    Width = isClosest ? 16 : 10,
                    Height = isClosest ? 16 : 10,
                    Fill = fill,
                    Stroke = isClosest ? Brushes.WhiteSmoke : stroke,
                    StrokeThickness = isClosest ? 2.0 : 0.8,
                    Opacity = isClosest ? 1.0 : 0.8,
                    Tag = "blip"
                };

                if (isClosest)
                {
                    blip.Effect = new DropShadowEffect
                    {
                        Color = Colors.Red,
                        BlurRadius = 15,
                        ShadowDepth = 0,
                        Opacity = 0.6
                    };
                }

                Point current = new(x, y);
                if (previousBlipPositions.TryGetValue(net.BSSID, out var prev))
                    current = Lerp(prev, current);
                previousBlipPositions[net.BSSID] = current;

                if (!blipTrails.ContainsKey(net.BSSID))
                    blipTrails[net.BSSID] = new List<Point>();

                var trail = blipTrails[net.BSSID];
                trail.Add(current);
                if (trail.Count > MaxTrailLength)
                    trail.RemoveAt(0);

                // Draw trail points
                for (int t = 0; t < trail.Count; t++)
                {
                    var p = trail[t];
                    var ghostTrail = new Ellipse
                    {
                        Width = 4,
                        Height = 4,
                        Fill = new SolidColorBrush(Color.FromArgb((byte)(40 + 40 * t), 0, 255, 0)),
                        Stroke = Brushes.Transparent,
                        Tag = "trail"
                    };
                    Canvas.SetLeft(ghostTrail, p.X - 2);
                    Canvas.SetTop(ghostTrail, p.Y - 2);
                    RadarCanvas.Children.Add(ghostTrail);
                }
                RadarCanvas.Children.OfType<Ellipse>()
                .Where(e => e.Tag as string == "trail")
                .ToList()
                .ForEach(e => RadarCanvas.Children.Remove(e));

                bool pulse = rssi > GetCaptureThreshold() + 2; // really close!
                if (pulse)
                {
                    blip.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Colors.Red,
                        BlurRadius = 20,
                        ShadowDepth = 0,
                        Opacity = 0.8
                    };
                }

                Canvas.SetLeft(blip, current.X - 4);
                Canvas.SetTop(blip, current.Y - 4);

                Canvas.SetLeft(blip, x - blip.Width / 2);
                Canvas.SetTop(blip, y - blip.Height / 2);
                RadarCanvas.Children.Add(blip);

                var label = new TextBlock
                {
                    Text = apLabel,
                    Foreground = Brushes.Lime,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Tag = "label"
                };
                Canvas.SetLeft(label, x + 6);
                Canvas.SetTop(label, y - 6);
                RadarCanvas.Children.Add(label);

                GhostListBox.Items.Add($"{apLabel}: {net.SSID} ({rssi} dBm)");
            }

            SetGhostCaptureState(ghostClose, detectedApLabel);
        }

        private void SetGhostCaptureState(bool active, string apLabel)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

            Dispatcher.Invoke(() =>
            {
                AlertOverlay.Opacity = active ? 0.6 : 0;
                CaptureText.Text = active && apLabel != null ? $"GHOST-AP CAPTURED!\n{apLabel} found" : "GHOSTAP CAPTURED!";
                CaptureText.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        private double MapRssiToRadius(int rssi)
        {
            int threshold = GetCaptureThreshold(); // e.g., -30
            int minRssi = -90;                     // weakest signal we'll consider
            int maxRssi = threshold;              // strongest signal (user-controlled)

            // Ensure rssi is within bounds
            int clamped = Math.Max(minRssi, Math.Min(maxRssi, rssi));

            // Inverted ratio: higher RSSI (closer to 0) means smaller distance
            double ratio = 1.0 - ((clamped - minRssi) / (double)(maxRssi - minRssi));
            return ratio * RadarRadius;
        }

        private void DrawGrid()
        {
            RadarCanvas.Children.Clear();

            // Draw concentric colored zone rings
            var bands = new[]
            {
        new { Radius = 10, Color = Colors.Red },
        new { Radius = 40, Color = Colors.Orange },
        new { Radius = 80, Color = Colors.YellowGreen },
        new { Radius = 120, Color = Colors.LimeGreen },
        new { Radius = 150, Color = Colors.Teal },
        new { Radius = 175, Color = Colors.DarkSlateGray }
    };

            foreach (var band in bands)
            {
                var ring = new Ellipse
                {
                    Width = band.Radius * 2,
                    Height = band.Radius * 2,
                    Stroke = new SolidColorBrush(band.Color),
                    StrokeThickness = 1.2,
                    Opacity = 0.25
                };
                Canvas.SetLeft(ring, CenterX - band.Radius);
                Canvas.SetTop(ring, CenterY - band.Radius);
                RadarCanvas.Children.Add(ring);
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

        private void SetGhostCaptureState(bool active)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

            Dispatcher.Invoke(() =>
            {
                AlertOverlay.Opacity = active ? 0.6 : 0;
                CaptureText.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            });
        }
    }
}
