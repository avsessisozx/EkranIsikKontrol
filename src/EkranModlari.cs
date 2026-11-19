using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace EkranIsikKontrol
{
    public sealed class EkranModu : IEquatable<EkranModu>
    {
        public int Genislik, Yukseklik, Hz;

        public string Cozunurluk => Genislik + " × " + Yukseklik;
        public override string ToString() => Cozunurluk + " @ " + Hz + " Hz";
        public bool Equals(EkranModu o) => o != null && o.Genislik == Genislik && o.Yukseklik == Yukseklik && o.Hz == Hz;
        public override bool Equals(object o) => Equals(o as EkranModu);
        public override int GetHashCode() => (Genislik * 397 ^ Yukseklik) * 397 ^ Hz;
    }

    public sealed class EkranAygiti
    {
        public string AygitAdi;   // \\.\DISPLAY1
        public string Ad;         // ASUS VG249 (ya da bağdaştırıcı adı)
        public bool Birincil;
        public bool Sanal;
        public override string ToString() => Ad + (Birincil ? " (ana ekran)" : "");
    }

    public enum ModSonucu { Tamam, YenidenBaslatmaGerekli, Desteklenmiyor, Hata }

    // Windows'un çözünürlük ve yenileme hızı ayarları (ChangeDisplaySettingsEx).
    public static class EkranModlari
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public ushort dmSpecVersion;
            public ushort dmDriverVersion;
            public ushort dmSize;
            public ushort dmDriverExtra;
            public uint dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public uint dmDisplayOrientation;
            public uint dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public ushort dmLogPixels;
            public uint dmBitsPerPel;
            public uint dmPelsWidth;
            public uint dmPelsHeight;
            public uint dmDisplayFlags;
            public uint dmDisplayFrequency;
            public uint dmICMMethod;
            public uint dmICMIntent;
            public uint dmMediaType;
            public uint dmDitherType;
            public uint dmReserved1;
            public uint dmReserved2;
            public uint dmPanningWidth;
            public uint dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE dm);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ChangeDisplaySettingsEx(string device, ref DEVMODE dm, IntPtr hwnd, uint flags, IntPtr param);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ChangeDisplaySettingsEx(string device, IntPtr dm, IntPtr hwnd, uint flags, IntPtr param);

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int ENUM_REGISTRY_SETTINGS = -2;
        private const uint DM_BITSPERPEL = 0x40000, DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000;
        private const uint CDS_UPDATEREGISTRY = 0x1, CDS_TEST = 0x2;
        private const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1, DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4;
        private const int DISP_CHANGE_SUCCESSFUL = 0, DISP_CHANGE_RESTART = 1, DISP_CHANGE_BADMODE = -2;

        private static DEVMODE YeniDevmode() => new DEVMODE { dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE)) };

        public static List<EkranAygiti> Aygitlar()
        {
            var adlar = MonitorAdlari.Oku();
            var liste = new List<EkranAygiti>();
            var dd = new DdcYerel.DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DdcYerel.DISPLAY_DEVICE)) };
            for (uint i = 0; DdcYerel.EnumDisplayDevices(null, i, ref dd, 0); i++)
            {
                if ((dd.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
                {
                    string ad = MonitorAdlari.EkranAygitininAdi(dd.DeviceName, adlar);
                    string baglastirici = dd.DeviceString ?? "";
                    bool sanal = ad == null && baglastirici.IndexOf("virtual", StringComparison.OrdinalIgnoreCase) >= 0;
                    liste.Add(new EkranAygiti
                    {
                        AygitAdi = dd.DeviceName,
                        Ad = ad ?? (baglastirici.Length > 0 ? baglastirici : dd.DeviceName),
                        Birincil = (dd.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0,
                        Sanal = sanal
                    });
                }
                dd.cb = Marshal.SizeOf(typeof(DdcYerel.DISPLAY_DEVICE));
            }
            // Gerçek monitörler önce, ana ekran en başta.
            return liste.OrderBy(a => a.Sanal).ThenByDescending(a => a.Birincil).ToList();
        }

        public static EkranModu Simdiki(string aygit)
        {
            DEVMODE dm = YeniDevmode();
            if (!EnumDisplaySettings(aygit, ENUM_CURRENT_SETTINGS, ref dm)) return null;
            return new EkranModu { Genislik = (int)dm.dmPelsWidth, Yukseklik = (int)dm.dmPelsHeight, Hz = (int)dm.dmDisplayFrequency };
        }

        // 32 bit renkli, tekrarsız modlar; büyükten küçüğe.
        public static List<EkranModu> Modlar(string aygit)
        {
            var set = new HashSet<EkranModu>();
            DEVMODE dm = YeniDevmode();
            for (int i = 0; EnumDisplaySettings(aygit, i, ref dm); i++)
            {
                if (dm.dmBitsPerPel == 32 && dm.dmDisplayFrequency > 1)
                    set.Add(new EkranModu { Genislik = (int)dm.dmPelsWidth, Yukseklik = (int)dm.dmPelsHeight, Hz = (int)dm.dmDisplayFrequency });
                dm = YeniDevmode();
            }
            return set.OrderByDescending(m => m.Genislik * m.Yukseklik).ThenByDescending(m => m.Genislik).ThenByDescending(m => m.Hz).ToList();
        }

        // Kalıcı: false ise yalnızca şimdilik uygulanır (kullanıcı onaylamazsa GeriAl ile kayıttaki ayara dönülür).
        public static ModSonucu Uygula(string aygit, EkranModu mod, bool kalici)
        {
            DEVMODE dm = YeniDevmode();
            if (!EnumDisplaySettings(aygit, ENUM_CURRENT_SETTINGS, ref dm)) return ModSonucu.Hata;
            dm.dmPelsWidth = (uint)mod.Genislik;
            dm.dmPelsHeight = (uint)mod.Yukseklik;
            dm.dmDisplayFrequency = (uint)mod.Hz;
            dm.dmBitsPerPel = 32;
            dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY | DM_BITSPERPEL;

            int test = ChangeDisplaySettingsEx(aygit, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
            if (test == DISP_CHANGE_BADMODE) return ModSonucu.Desteklenmiyor;
            if (test != DISP_CHANGE_SUCCESSFUL) { Gunluk.Uyari("Mod testi başarısız: " + mod + " kod " + test); return ModSonucu.Desteklenmiyor; }

            int r = ChangeDisplaySettingsEx(aygit, ref dm, IntPtr.Zero, kalici ? CDS_UPDATEREGISTRY : 0, IntPtr.Zero);
            Gunluk.Bilgi("Ekran modu " + (kalici ? "kalıcı " : "geçici ") + "uygulandı: " + aygit + " " + mod + " sonuç " + r);
            if (r == DISP_CHANGE_SUCCESSFUL) return ModSonucu.Tamam;
            if (r == DISP_CHANGE_RESTART) return ModSonucu.YenidenBaslatmaGerekli;
            return ModSonucu.Hata;
        }

        // Kayıtlı (kalıcı) ayara geri döner.
        public static void GeriAl(string aygit)
        {
            int r = ChangeDisplaySettingsEx(aygit, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
            Gunluk.Bilgi("Ekran modu geri alındı: " + aygit + " sonuç " + r);
        }
    }
}
