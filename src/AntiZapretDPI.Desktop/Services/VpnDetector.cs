using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using AntiZapretDPI.Contracts;

namespace AntiZapretDPI.Services
{
    public class VpnDetector : IVpnDetector
    {
        private const int MaxNumOfInterfaces = 512;
        private const uint NoError = 0;
        private const uint ErrorInsufficientBuffer = 122;

        private static readonly string[] VpnMarkers =
        {
            "vpn", "wireguard", "wintun", "openvpn", "tap-windows", "tap adapter",
            "tailscale", "zerotier", "cloudflare", "warp", "mullvad", "nordvpn",
            "protonvpn", "proton vpn", "surfshark", "cyberghost", "windscribe",
            "hotspot shield", "private internet", "expressvpn", "ivpn", "avast",
            "secureline", "bitdefender", "kaspersky", "eset", "norton", "checkpoint",
            "check point", "anyconnect", "globalprotect", "forticlient", "f5 networks",
            "sangfor", "ipsec", "ikev2", "sstp", "l2tp", "pptp", "yandex", "vpn unlimited",
            "hotspot shield", "amnezia", "hide", "psiphon", "hoxx", "zenmate", "touch vpn",
            "vpnify", "betternet", "tunnelbear", "vypr", "astrill", "purevpn", "tor guard",
            "v2ray", "xray", "sing-box", "shadowsocks", "outline", "torguard"
        };

        private static readonly string[] NonVpnTunnelMarkers =
        {
            "teredo", "isatap", "6to4", "microsoft", "basip", "isatap", "pppoe", "mactap"
        };

        private int? _baselineOwnerIndex;
        private int? _candidateOwnerIndex;
        private int _candidateStreak;

        public bool IsVpnActive()
        {
            var upNics = GetUpNetworkInterfaces();

            if (AnyVpnLikeInterface(upNics))
            {
                return true;
            }

            return EvaluateDefaultRoutes(upNics);
        }

        private static bool AnyVpnLikeInterface(IReadOnlyDictionary<int, NetworkInterface> upNics)
        {
            foreach (var nic in upNics.Values)
            {
                if (IsVpnLike(nic))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsVpnLike(NetworkInterface nic)
        {
            var type = nic.NetworkInterfaceType;
            if (type is NetworkInterfaceType.Loopback or NetworkInterfaceType.Unknown)
            {
                return false;
            }

            var name = nic.Name ?? string.Empty;
            var description = nic.Description ?? string.Empty;
            var text = (name + " " + description).ToLowerInvariant();

            if (ContainsAny(text, NonVpnTunnelMarkers))
            {
                return false;
            }

            if (type is NetworkInterfaceType.Tunnel)
            {
                return true;
            }

            if (!IsLikelyPhysical(type))
            {
                return true;
            }

            return ContainsAny(text, VpnMarkers);
        }

        private static bool IsLikelyPhysical(NetworkInterfaceType type)
        {
            return type is NetworkInterfaceType.Ethernet or NetworkInterfaceType.TokenRing or
                NetworkInterfaceType.Fddi or NetworkInterfaceType.BasicIsdn or
                NetworkInterfaceType.PrimaryIsdn or NetworkInterfaceType.Ppp or
                NetworkInterfaceType.Ethernet3Megabit or NetworkInterfaceType.Atm or
                NetworkInterfaceType.Isdn or NetworkInterfaceType.FastEthernetT or
                NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Wireless80211 or
                NetworkInterfaceType.GigabitEthernet;
        }

        private bool EvaluateDefaultRoutes(IReadOnlyDictionary<int, NetworkInterface> upNics)
        {
            var routes = GetDefaultIpv4Routes();
            if (routes.Count == 0)
            {
                return false;
            }

            int? owner = null;
            foreach (var route in routes)
            {
                int candidate = (int)route.dwForwardIfIndex;
                if (upNics.ContainsKey(candidate))
                {
                    owner = candidate;
                    break;
                }
            }
            if (owner == null)
            {
                return false;
            }

            if (_baselineOwnerIndex == null)
            {
                _baselineOwnerIndex = owner;
                return false;
            }

            if (_baselineOwnerIndex == owner)
            {
                return false;
            }

            if (upNics.TryGetValue(_baselineOwnerIndex.Value, out var previous) &&
                previous.OperationalStatus == OperationalStatus.Up)
            {
                if (_candidateOwnerIndex != owner)
                {
                    _candidateOwnerIndex = owner;
                    _candidateStreak = 1;
                }
                else
                {
                    _candidateStreak++;
                }

                return _candidateStreak >= 2;
            }

            _candidateOwnerIndex = null;
            _candidateStreak = 0;
            _baselineOwnerIndex = owner;
            return false;
        }

        private static IReadOnlyDictionary<int, NetworkInterface> GetUpNetworkInterfaces()
        {
            var result = new Dictionary<int, NetworkInterface>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                var type = nic.NetworkInterfaceType;
                if (type is NetworkInterfaceType.Loopback or NetworkInterfaceType.Unknown)
                {
                    continue;
                }

                if (!HasIpv4Address(nic))
                {
                    continue;
                }

                int index = GetIpv4Index(nic);
                if (index > 0 && !result.ContainsKey(index))
                {
                    result.Add(index, nic);
                }
            }
            return result;
        }

        private static List<MibIpForwardRow> GetDefaultIpv4Routes()
        {
            var rows = new List<MibIpForwardRow>();
            uint size = 0;
            try
            {
                GetIpForwardTable(IntPtr.Zero, ref size, true);
                IntPtr buffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    uint result = GetIpForwardTable(buffer, ref size, true);
                    if (result != NoError && result != ErrorInsufficientBuffer)
                    {
                        return rows;
                    }

                    int rowSize = Marshal.SizeOf<MibIpForwardRow>();
                    long basePtr = buffer.ToInt64();
                    uint count = (uint)Marshal.ReadInt32(new IntPtr(basePtr));
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr offset = new IntPtr(basePtr + sizeof(uint) + (long)i * rowSize);
                        var row = Marshal.PtrToStructure<MibIpForwardRow>(offset);
                        if (row.dwForwardDest == 0 && row.dwForwardMask == 0)
                        {
                            rows.Add(row);
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }

                rows.Sort((a, b) => a.dwForwardMetric1.CompareTo(b.dwForwardMetric1));
            }
            catch
            {
                rows.Clear();
            }
            return rows;
        }

        private static bool ContainsAny(string text, IReadOnlyList<string> markers)
        {
            foreach (var marker in markers)
            {
                if (text.Contains(marker, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasIpv4Address(NetworkInterface nic)
        {
            try
            {
                return nic.GetIPProperties().UnicastAddresses
                    .Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
            }
            catch
            {
                return false;
            }
        }

        private static int GetIpv4Index(NetworkInterface nic)
        {
            try
            {
                return nic.GetIPProperties().GetIPv4Properties().Index;
            }
            catch
            {
                return -1;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MibIpForwardRow
        {
            public uint dwForwardDest;
            public uint dwForwardMask;
            public uint dwForwardPolicy;
            public uint dwForwardNextHop;
            public uint dwForwardIfIndex;
            public uint dwForwardType;
            public uint dwForwardProto;
            public uint dwForwardAge;
            public uint dwForwardNextHopAS;
            public uint dwForwardMetric1;
            public uint dwForwardMetric2;
            public uint dwForwardMetric3;
            public uint dwForwardMetric4;
            public uint dwForwardMetric5;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MibIpForwardTable
        {
            public uint dwNumEntries;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxNumOfInterfaces)]
            public MibIpForwardRow[] table;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetIpForwardTable(IntPtr pIpForwardTable, ref uint pdwSize, bool bOrder);
    }
}
