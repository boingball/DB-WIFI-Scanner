using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DB_WIFI_Scanner
{
    public class WifiNetwork
    {
        public string SSID { get; set; }
        public int SignalQuality { get; set; } // 0-100
        public string BSSID { get; set; }
        public int RSSI { get; set; } // in dBm
    }

    public class WifiScanner
    {
        private const int WLAN_CLIENT_VERSION_XP_SP2 = 1;
        private const int WLAN_CLIENT_VERSION_LONGHORN = 2;

        [DllImport("wlanapi.dll")]
        internal static extern uint WlanOpenHandle(
            uint dwClientVersion,
            IntPtr pReserved,
            out uint pdwNegotiatedVersion,
            out IntPtr phClientHandle);

        [DllImport("wlanapi.dll")]
        internal static extern uint WlanCloseHandle(
            IntPtr hClientHandle,
            IntPtr pReserved);

        [DllImport("wlanapi.dll")]
        internal static extern uint WlanEnumInterfaces(
            IntPtr hClientHandle,
            IntPtr pReserved,
            out IntPtr ppInterfaceList);

        [DllImport("wlanapi.dll")]
        internal static extern uint WlanScan(
            IntPtr hClientHandle,
            ref Guid pInterfaceGuid,
            IntPtr pDot11Ssid,
            IntPtr pIeData,
            IntPtr pReserved);

        [DllImport("wlanapi.dll")]
        private static extern uint WlanGetAvailableNetworkList(
            IntPtr hClientHandle,
            ref Guid pInterfaceGuid,
            uint dwFlags,
            IntPtr pReserved,
            out IntPtr ppAvailableNetworkList);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct WLAN_INTERFACE_INFO
        {
            public Guid InterfaceGuid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strInterfaceDescription;
            public WLAN_INTERFACE_STATE isState;
        }

        internal enum WLAN_INTERFACE_STATE
        {
            NotReady = 0,
            Connected = 1,
            AdHocNetworkFormed = 2,
            Disconnecting = 3,
            Disconnected = 4,
            Associating = 5,
            Discovering = 6,
            Authenticating = 7
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WLAN_INTERFACE_INFO_LIST
        {
            public int dwNumberOfItems;
            public int dwIndex;
            public WLAN_INTERFACE_INFO InterfaceInfo;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct WLAN_AVAILABLE_NETWORK
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strProfileName;
            public DOT11_SSID dot11Ssid;
            public uint dot11BssType;
            public uint uNumberOfBssids;
            public bool bNetworkConnectable;
            public uint wlanNotConnectableReason;
            public uint uNumberOfPhyTypes;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public uint[] dot11PhyTypes;
            public bool bMorePhyTypes;
            public uint wlanSignalQuality; // 0-100
            public bool bSecurityEnabled;
            public uint dot11DefaultAuthAlgorithm;
            public uint dot11DefaultCipherAlgorithm;
            public uint dwFlags;
            public uint dwReserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DOT11_SSID
        {
            public uint uSSIDLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] ucSSID;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WLAN_AVAILABLE_NETWORK_LIST
        {
            public uint dwNumberOfItems;
            public uint dwIndex;
            public WLAN_AVAILABLE_NETWORK Network;
        }

        [DllImport("wlanapi.dll")]
        private static extern uint WlanGetNetworkBssList(
    IntPtr hClientHandle,
    ref Guid pInterfaceGuid,
    IntPtr pDot11Ssid,
    DOT11_BSS_TYPE dot11BssType,
    bool bSecurityEnabled,
    IntPtr pReserved,
    out IntPtr ppWlanBssList);

        [StructLayout(LayoutKind.Sequential)]
        internal struct WLAN_BSS_LIST
        {
            public uint TotalSize;
            public uint NumberOfItems;
            // Followed by WLAN_BSS_ENTRY structs
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WLAN_BSS_ENTRY
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] dot11Bssid;
            public uint uPhyId;
            public DOT11_SSID dot11Ssid;
            public DOT11_BSS_TYPE dot11BssType;
            public DOT11_PHY_TYPE dot11BssPhyType;
            public int lRssi; // 🔥 This is your raw signal!
            public uint uLinkQuality;
            public bool bInRegDomain;
            public ushort usBeaconPeriod;
            public ulong ullTimestamp;
            public ulong ullHostTimestamp;
            public ushort usCapabilityInformation;
            public uint ulChCenterFrequency;
            public WLAN_RATE_SET wlanRateSet;
            public uint ulIeOffset;
            public uint ulIeSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WLAN_RATE_SET
        {
            public uint uRateSetLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 126)]
            public ushort[] usRateSet;
        }

        internal enum DOT11_BSS_TYPE
        {
            Infrastructure = 1,
            Independent = 2,
            Any = 3
        }

        internal enum DOT11_PHY_TYPE : uint
        {
            Unknown = 0,
            Any = 0,
            FHSS = 1,
            DSSS = 2,
            IRBaseband = 3,
            OFDM = 4,
            HRDSSS = 5,
            ERP = 6,
            HT = 7,
            VHT = 8,
            IHV_Start = 0x80000000,
            IHV_End = 0xffffffff
        }

        public List<WifiNetwork> Scan()
        {
            var networks = new List<WifiNetwork>();
            uint negotiatedVersion;
            IntPtr clientHandle;

            if (WlanOpenHandle(WLAN_CLIENT_VERSION_LONGHORN, IntPtr.Zero, out negotiatedVersion, out clientHandle) != 0)
                return networks;

            IntPtr interfaceList;
            if (WlanEnumInterfaces(clientHandle, IntPtr.Zero, out interfaceList) != 0)
                return networks;

            int listHeaderSize = Marshal.SizeOf(typeof(uint)) * 2;
            int ifaceSize = Marshal.SizeOf(typeof(WLAN_INTERFACE_INFO));
            int ifaceCount = Marshal.ReadInt32(interfaceList);

            for (int i = 0; i < ifaceCount; i++)
            {
                IntPtr ifacePtr = new IntPtr(interfaceList.ToInt64() + listHeaderSize + i * ifaceSize);
                var iface = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(ifacePtr);

           //    if (!iface.strInterfaceDescription.ToLower().Contains("wi-fi"))
           //         continue;

                // Trigger a scan and wait briefly
                WlanScan(clientHandle, ref iface.InterfaceGuid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
               // System.Threading.Thread.Sleep(2000);

                IntPtr availableNetworkListPtr;
                if (WlanGetAvailableNetworkList(clientHandle, ref iface.InterfaceGuid, 0, IntPtr.Zero, out availableNetworkListPtr) == 0)
                {
                    uint numItems = (uint)Marshal.ReadInt32(availableNetworkListPtr);
                    int offset = Marshal.SizeOf(typeof(uint)) * 2;
                    int networkSize = Marshal.SizeOf(typeof(WLAN_AVAILABLE_NETWORK));

                    for (int j = 0; j < numItems; j++)
                    {
                        IntPtr itemPtr = new IntPtr(availableNetworkListPtr.ToInt64() + offset + j * networkSize);
                        var net = Marshal.PtrToStructure<WLAN_AVAILABLE_NETWORK>(itemPtr);

                        int ssidLength = (int)net.dot11Ssid.uSSIDLength;
                        string ssid = Encoding.ASCII.GetString(net.dot11Ssid.ucSSID, 0, ssidLength).Trim();

                        if (string.IsNullOrWhiteSpace(ssid))
                            continue;

                        networks.Add(new WifiNetwork
                        {
                            SSID = ssid,
                            SignalQuality = (int)net.wlanSignalQuality
                        });
                    }
                }
            }

            WlanCloseHandle(clientHandle, IntPtr.Zero);
            return networks;
        }


        public List<WifiNetwork> ScanBssList()
        {
            var networks = new List<WifiNetwork>();
            uint negotiatedVersion;
            IntPtr clientHandle;

            if (WlanOpenHandle(WLAN_CLIENT_VERSION_LONGHORN, IntPtr.Zero, out negotiatedVersion, out clientHandle) != 0)
                return networks;

            IntPtr interfaceList;
            if (WlanEnumInterfaces(clientHandle, IntPtr.Zero, out interfaceList) != 0)
                return networks;

            int listHeaderSize = Marshal.SizeOf(typeof(uint)) * 2;
            int ifaceSize = Marshal.SizeOf(typeof(WLAN_INTERFACE_INFO));
            int ifaceCount = Marshal.ReadInt32(interfaceList);

            for (int i = 0; i < ifaceCount; i++)
            {
                IntPtr ifacePtr = new IntPtr(interfaceList.ToInt64() + listHeaderSize + i * ifaceSize);
                var iface = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(ifacePtr);

                // if (!iface.strInterfaceDescription.ToLower().Contains("wi-fi"))
                //     continue;

                IntPtr bssListPtr;
                uint result = WlanGetNetworkBssList(clientHandle, ref iface.InterfaceGuid, IntPtr.Zero, DOT11_BSS_TYPE.Any, false, IntPtr.Zero, out bssListPtr);
                if (result != 0)
                    continue;

                var bssList = Marshal.PtrToStructure<WLAN_BSS_LIST>(bssListPtr);
                int bssEntrySize = Marshal.SizeOf(typeof(WLAN_BSS_ENTRY));

                // Temporary list to store all entries before deduplication
                var tempNetworks = new List<WifiNetwork>();

                for (int j = 0; j < bssList.NumberOfItems; j++)
                {
                    IntPtr entryPtr = new IntPtr(bssListPtr.ToInt64() + 8 + j * bssEntrySize);
                    var entry = Marshal.PtrToStructure<WLAN_BSS_ENTRY>(entryPtr);

                    // Parse SSID safely
                    int ssidLength = (int)entry.dot11Ssid.uSSIDLength;
                    ssidLength = Math.Min(ssidLength, entry.dot11Ssid.ucSSID.Length);

                    byte[] ssidBytes = new byte[ssidLength];
                    Array.Copy(entry.dot11Ssid.ucSSID, ssidBytes, ssidLength);

                    string ssid;
                    try
                    {
                        ssid = Encoding.UTF8.GetString(ssidBytes);
                    }
                    catch
                    {
                        ssid = Encoding.ASCII.GetString(ssidBytes);
                    }

                    // Strip control characters
                    ssid = new string(ssid.Where(c => !char.IsControl(c)).ToArray()).Trim();
                    if (string.IsNullOrWhiteSpace(ssid)) ssid = "[Hidden or Invalid SSID]";
                    if (string.IsNullOrWhiteSpace(ssid)) continue;

                    // Format BSSID
                    string bssid = BitConverter.ToString(entry.dot11Bssid).Replace("-", ":");

                    tempNetworks.Add(new WifiNetwork
                    {
                        SSID = ssid,
                        SignalQuality = ConvertRssiToQuality(entry.lRssi),
                        BSSID = bssid,
                        RSSI = entry.lRssi
                    });
                }

                // Deduplicate by BSSID, keeping the entry with the strongest signal
                foreach (var group in tempNetworks.GroupBy(n => n.BSSID))
                {
                    var bestEntry = group.OrderByDescending(n => n.RSSI).First();
                    // Ensure we don't add duplicates (in case the same BSSID appears across interfaces)
                    if (!networks.Any(n => n.BSSID == bestEntry.BSSID))
                    {
                        networks.Add(bestEntry);
                    }
                }
            }

            WlanCloseHandle(clientHandle, IntPtr.Zero);
            return networks;
        }
        private int ConvertRssiToQuality(int rssi)
        {
            if (rssi <= -100) return 0;
            if (rssi >= -50) return 100;
            return 2 * (rssi + 100);
        }

    }
}

