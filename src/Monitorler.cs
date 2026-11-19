using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EkranIsikKontrol
{
    // MCCS (DDC/CI) VCP kodları.
    public static class Vcp
    {
        public const byte Parlaklik = 0x10;
        public const byte Kontrast = 0x12;
        public const byte RenkOnAyari = 0x14;
        public const byte GirisKaynagi = 0x60;
        public const byte Ses = 0x62;

        public static string RenkOnAyariAdi(uint v)
        {
            switch (v)
            {
                case 0x01: return "sRGB";
                case 0x02: return "Doğal (native)";
                case 0x03: return "4000K";
                case 0x04: return "5000K";
                case 0x05: return "6500K";
                case 0x06: return "7500K";
                case 0x07: return "8200K";
                case 0x08: return "9300K";
                case 0x09: return "10000K";
                case 0x0A: return "11500K";
                case 0x0B: return "Kullanıcı 1";
                case 0x0C: return "Kullanıcı 2";
                case 0x0D: return "Kullanıcı 3";
                default: return "Değer " + v;
            }
        }

        public static string GirisAdi(uint v)
        {
            switch (v)
            {
                case 0x01: return "VGA 1";
                case 0x02: return "VGA 2";
                case 0x03: return "DVI 1";
                case 0x04: return "DVI 2";
                case 0x0F: return "DisplayPort 1";
                case 0x10: return "DisplayPort 2";
                case 0x11: return "HDMI 1";
                case 0x12: return "HDMI 2";
                default: return "Giriş " + v;
            }
        }
    }

    // Yetenek dizgesini çözer: "...vcp(02 10 12 14(05 06 08 0B) 60(01 11 0F) 62)..."
    public sealed class MonitorYetenekleri
    {
        public string Model = "";
        public readonly HashSet<byte> Kodlar = new HashSet<byte>();
        public readonly Dictionary<byte, List<uint>> Secenekler = new Dictionary<byte, List<uint>>();

        public bool Destekler(byte kod) => Kodlar.Contains(kod);

        public static MonitorYetenekleri Coz(string s)
        {
            var y = new MonitorYetenekleri();
            if (string.IsNullOrEmpty(s)) return y;
            Match m = Regex.Match(s, @"model\(([^)]*)\)", RegexOptions.IgnoreCase);
            if (m.Success) y.Model = m.Groups[1].Value.Trim();

            int i = s.IndexOf("vcp(", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return y;
            i += 4;
            int derinlik = 1;
            int bas = i;
            while (i < s.Length && derinlik > 0)
            {
                if (s[i] == '(') derinlik++;
                else if (s[i] == ')') derinlik--;
                i++;
            }
            string govde = s.Substring(bas, Math.Max(0, i - bas - 1));

            int p = 0;
            while (p < govde.Length)
            {
                while (p < govde.Length && char.IsWhiteSpace(govde[p])) p++;
                if (p + 2 > govde.Length) break;
                string hex = govde.Substring(p, 2);
                if (!byte.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out byte kod)) { p++; continue; }
                p += 2;
                y.Kodlar.Add(kod);
                while (p < govde.Length && char.IsWhiteSpace(govde[p])) p++;
                if (p < govde.Length && govde[p] == '(')
                {
                    int kapa = govde.IndexOf(')', p);
                    if (kapa < 0) kapa = govde.Length;
                    var liste = new List<uint>();
                    foreach (string parca in govde.Substring(p + 1, kapa - p - 1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        if (uint.TryParse(parca, System.Globalization.NumberStyles.HexNumber, null, out uint v)) liste.Add(v);
                    y.Secenekler[kod] = liste;
                    p = kapa + 1;
                }
            }
            return y;
        }
    }

    public struct VcpDeger
    {
        public bool Basarili;
        public uint Simdiki;
        public uint En;
        public static VcpDeger Yok => new VcpDeger { Basarili = false };
    }

    // Bir fiziksel monitör. Tutamacı yalnızca DdcIsci iş parçacığında kullanılır.
    public sealed class FizikselMonitor
    {
        public int Sira;
        public string Ad;              // ör. "ASUS VG249"
        public string EkranAygiti;     // ör. \\.\DISPLAY1
        public IntPtr Tutamac;
        public MonitorYetenekleri Yetenekler = new MonitorYetenekleri();
        public bool DdcCalisiyor;

        public override string ToString() => Ad;
    }

    internal static class DdcYerel
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

        [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplayDevices(string device, uint index, ref DISPLAY_DEVICE dd, uint flags);
        [DllImport("dxva2.dll", SetLastError = true)] public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr h, out uint n);
        [DllImport("dxva2.dll", SetLastError = true)] public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr h, uint n, [Out] PHYSICAL_MONITOR[] arr);
        [DllImport("dxva2.dll", SetLastError = true)] public static extern bool DestroyPhysicalMonitor(IntPtr h);
        [DllImport("dxva2.dll", SetLastError = true)] public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr h, byte code, IntPtr type, out uint cur, out uint max);
        [DllImport("dxva2.dll", SetLastError = true)] public static extern bool SetVCPFeature(IntPtr h, byte code, uint value);
        [DllImport("dxva2.dll", SetLastError = true)] public static extern bool GetCapabilitiesStringLength(IntPtr h, out uint len);
        [DllImport("dxva2.dll", SetLastError = true, CharSet = CharSet.Ansi)] public static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr h, StringBuilder s, uint len);

        public const uint EDD_GET_DEVICE_INTERFACE_NAME = 1;
    }

    // Monitör adlarını Windows'un EDID kaydından (WmiMonitorID) bulur.
    public static class MonitorAdlari
    {
        // Anahtar: "DISPLAY\AUS24A1\5&...&UID4352" gibi örnek kimliği (büyük harf).
        public static Dictionary<string, string> Oku()
        {
            var sonuc = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var ara = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, ManufacturerName, UserFriendlyName FROM WmiMonitorID"))
                    foreach (ManagementObject o in ara.Get())
                    {
                        string ornek = (o["InstanceName"] as string ?? "");
                        int alt = ornek.LastIndexOf('_');
                        if (alt > 0) ornek = ornek.Substring(0, alt);
                        string ad = Metin(o["UserFriendlyName"] as ushort[]);
                        if (string.IsNullOrWhiteSpace(ad)) ad = Metin(o["ManufacturerName"] as ushort[]);
                        if (!string.IsNullOrWhiteSpace(ad)) sonuc[ornek] = ad.Trim();
                    }
            }
            catch (Exception ex) { Gunluk.Uyari("Monitör adları okunamadı: " + ex.Message); }
            return sonuc;
        }

        private static string Metin(ushort[] d) =>
            d == null ? "" : new string(d.TakeWhile(c => c != 0).Select(c => (char)c).ToArray());

        // "\\?\DISPLAY#AUS24A1#5&2d&0&UID4352#{e6f07b5f-...}" -> "DISPLAY\AUS24A1\5&2d&0&UID4352"
        public static string ArayuzYolundanOrnek(string yol)
        {
            if (string.IsNullOrEmpty(yol)) return "";
            string s = yol;
            if (s.StartsWith(@"\\?\")) s = s.Substring(4);
            int sus = s.IndexOf("#{", StringComparison.Ordinal);
            if (sus >= 0) s = s.Substring(0, sus);
            return s.Replace('#', '\\');
        }

        // \\.\DISPLAY1 aygıtına bağlı monitörün adı; bulunamazsa null.
        public static string EkranAygitininAdi(string ekranAygiti, Dictionary<string, string> adlar)
        {
            var dd = new DdcYerel.DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DdcYerel.DISPLAY_DEVICE)) };
            for (uint i = 0; DdcYerel.EnumDisplayDevices(ekranAygiti, i, ref dd, DdcYerel.EDD_GET_DEVICE_INTERFACE_NAME); i++)
            {
                string ornek = ArayuzYolundanOrnek(dd.DeviceID);
                if (adlar.TryGetValue(ornek, out string ad)) return ad;
                dd.cb = Marshal.SizeOf(typeof(DdcYerel.DISPLAY_DEVICE));
            }
            return null;
        }
    }

    // DDC/CI tek bir iş parçacığında, sırayla yapılır (monitörler eşzamanlı komutları sevmez).
    // Yazmalar birleştirilir: kaydırıcı hızlı oynatılınca yalnızca en son değer gönderilir.
    public sealed class DdcIsci : IDisposable
    {
        private readonly Thread isParcacigi;
        private readonly AutoResetEvent sinyal = new AutoResetEvent(false);
        private readonly object kilit = new object();
        private readonly Dictionary<(int, byte), uint> bekleyenYazmalar = new Dictionary<(int, byte), uint>();
        private readonly Queue<Action> isler = new Queue<Action>();
        private volatile bool durdur;
        private List<FizikselMonitor> monitorler = new List<FizikselMonitor>();

        public event Action<FizikselMonitor, byte, bool> YazmaBitti;

        public DdcIsci()
        {
            isParcacigi = new Thread(Dongu) { IsBackground = true, Name = "DDC/CI" };
            isParcacigi.Start();
        }

        public IReadOnlyList<FizikselMonitor> Monitorler { get { lock (kilit) return monitorler.ToList(); } }

        public Task<List<FizikselMonitor>> TaraAsync() => Calistir(Tara);

        public Task<VcpDeger> OkuAsync(FizikselMonitor m, byte kod) => Calistir(() => Oku(m, kod));

        public void Yaz(FizikselMonitor m, byte kod, uint deger)
        {
            if (m == null) return;
            lock (kilit) bekleyenYazmalar[(m.Sira, kod)] = deger;
            sinyal.Set();
        }

        // Bekleyen yazmalar bitene kadar bekler (profil uygulandıktan sonra değerleri tekrar okumak için).
        public Task BekleAsync() => Calistir(() => 0);

        private Task<T> Calistir<T>(Func<T> f)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (kilit)
                isler.Enqueue(() =>
                {
                    try { tcs.SetResult(f()); }
                    catch (Exception ex) { tcs.SetException(ex); }
                });
            sinyal.Set();
            return tcs.Task;
        }

        private void Dongu()
        {
            while (!durdur)
            {
                sinyal.WaitOne(500);
                while (!durdur)
                {
                    KeyValuePair<(int, byte), uint>[] yazmalar;
                    Action is_ = null;
                    lock (kilit)
                    {
                        yazmalar = bekleyenYazmalar.ToArray();
                        bekleyenYazmalar.Clear();
                        if (yazmalar.Length == 0 && isler.Count > 0) is_ = isler.Dequeue();
                    }
                    if (yazmalar.Length == 0 && is_ == null) break;
                    foreach (var y in yazmalar) YazmayiYap(y.Key.Item1, y.Key.Item2, y.Value);
                    is_?.Invoke();
                }
            }
        }

        private void YazmayiYap(int sira, byte kod, uint deger)
        {
            FizikselMonitor m;
            lock (kilit) m = monitorler.FirstOrDefault(x => x.Sira == sira);
            if (m == null) return;
            bool tamam = false;
            // Bazı monitörler arka arkaya gelen komutu kaçırır; bir kez yeniden dene.
            for (int deneme = 0; deneme < 2 && !tamam; deneme++)
            {
                tamam = DdcYerel.SetVCPFeature(m.Tutamac, kod, deger);
                if (!tamam) Thread.Sleep(60);
            }
            if (!tamam) Gunluk.Uyari("DDC yazılamadı: " + m.Ad + " kod 0x" + kod.ToString("X2") + " = " + deger + " (hata " + Marshal.GetLastWin32Error() + ")");
            // MCCS: ardışık komutlar arasında ~50 ms beklenmeli.
            Thread.Sleep(50);
            try { YazmaBitti?.Invoke(m, kod, tamam); } catch (Exception) { }
        }

        private VcpDeger Oku(FizikselMonitor m, byte kod)
        {
            if (m == null) return VcpDeger.Yok;
            for (int deneme = 0; deneme < 3; deneme++)
            {
                if (DdcYerel.GetVCPFeatureAndVCPFeatureReply(m.Tutamac, kod, IntPtr.Zero, out uint cur, out uint max))
                {
                    Thread.Sleep(40);
                    return new VcpDeger { Basarili = true, Simdiki = cur, En = max };
                }
                Thread.Sleep(80);
            }
            return VcpDeger.Yok;
        }

        private List<FizikselMonitor> Tara()
        {
            // Eski tutamaçları bırak.
            lock (kilit)
            {
                foreach (var eski in monitorler) DdcYerel.DestroyPhysicalMonitor(eski.Tutamac);
                monitorler = new List<FizikselMonitor>();
            }

            var adlar = MonitorAdlari.Oku();
            var yeni = new List<FizikselMonitor>();
            DdcYerel.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref DdcYerel.RECT r, IntPtr d) =>
            {
                var bilgi = new DdcYerel.MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(DdcYerel.MONITORINFOEX)) };
                DdcYerel.GetMonitorInfo(h, ref bilgi);
                if (!DdcYerel.GetNumberOfPhysicalMonitorsFromHMONITOR(h, out uint n) || n == 0) return true;
                var dizi = new DdcYerel.PHYSICAL_MONITOR[n];
                if (!DdcYerel.GetPhysicalMonitorsFromHMONITOR(h, n, dizi)) return true;
                string ad = MonitorAdlari.EkranAygitininAdi(bilgi.szDevice, adlar);
                foreach (var pm in dizi)
                    yeni.Add(new FizikselMonitor
                    {
                        Sira = yeni.Count,
                        Ad = ad ?? pm.szPhysicalMonitorDescription,
                        EkranAygiti = bilgi.szDevice,
                        Tutamac = pm.hPhysicalMonitor
                    });
                return true;
            }, IntPtr.Zero);

            foreach (var m in yeni)
            {
                m.Yetenekler = YetenekleriOku(m);
                // Yetenek dizgesi okunamasa bile parlaklık okunabiliyorsa DDC çalışıyor demektir.
                m.DdcCalisiyor = Oku(m, Vcp.Parlaklik).Basarili;
                if (m.DdcCalisiyor && m.Yetenekler.Kodlar.Count == 0)
                {
                    m.Yetenekler.Kodlar.Add(Vcp.Parlaklik);
                    m.Yetenekler.Kodlar.Add(Vcp.Kontrast);
                }
                if (string.IsNullOrEmpty(m.Ad) || m.Ad == "Generic PnP Monitor")
                    if (!string.IsNullOrEmpty(m.Yetenekler.Model)) m.Ad = m.Yetenekler.Model;
                Gunluk.Bilgi("Monitör: " + m.Ad + " (" + m.EkranAygiti + ") DDC=" + m.DdcCalisiyor +
                             " kodlar=" + string.Join(" ", m.Yetenekler.Kodlar.OrderBy(k => k).Select(k => k.ToString("X2"))));
            }
            lock (kilit) monitorler = yeni;
            return yeni;
        }

        private static MonitorYetenekleri YetenekleriOku(FizikselMonitor m)
        {
            for (int deneme = 0; deneme < 2; deneme++)
            {
                if (DdcYerel.GetCapabilitiesStringLength(m.Tutamac, out uint uzunluk) && uzunluk > 0)
                {
                    var sb = new StringBuilder((int)uzunluk);
                    if (DdcYerel.CapabilitiesRequestAndCapabilitiesReply(m.Tutamac, sb, uzunluk))
                        return MonitorYetenekleri.Coz(sb.ToString());
                }
                Thread.Sleep(100);
            }
            return new MonitorYetenekleri();
        }

        public void Dispose()
        {
            durdur = true;
            sinyal.Set();
            isParcacigi.Join(1500);
            lock (kilit)
            {
                foreach (var m in monitorler) DdcYerel.DestroyPhysicalMonitor(m.Tutamac);
                monitorler.Clear();
            }
        }
    }
}
