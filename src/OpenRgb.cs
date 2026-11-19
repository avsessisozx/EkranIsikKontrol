using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace EkranIsikKontrol
{
    public struct Rgb
    {
        public byte R, G, B;
        public Rgb(byte r, byte g, byte b) { R = r; G = g; B = b; }
        public Rgb Olcekle(int yuzde)
        {
            yuzde = Math.Max(0, Math.Min(100, yuzde));
            return new Rgb((byte)(R * yuzde / 100), (byte)(G * yuzde / 100), (byte)(B * yuzde / 100));
        }
        public string Hex => "#" + R.ToString("X2") + G.ToString("X2") + B.ToString("X2");
        public static bool HexCoz(string s, out Rgb r)
        {
            r = default(Rgb);
            s = (s ?? "").Trim().TrimStart('#');
            if (s.Length != 6) return false;
            try { r = new Rgb(Convert.ToByte(s.Substring(0, 2), 16), Convert.ToByte(s.Substring(2, 2), 16), Convert.ToByte(s.Substring(4, 2), 16)); return true; }
            catch (Exception) { return false; }
        }
    }

    public sealed class RgbDenetleyici
    {
        public int Sira;
        public int Tur;
        public string Ad = "";
        public string Aciklama = "";
        public List<string> Modlar = new List<string>();
        public int EtkinMod;
        public int LedSayisi;
        public override string ToString() => Ad;
    }

    // OpenRGB SDK istemcisi (protokol sürümü 0: en basit ve tüm sunucularca desteklenir).
    // Paket başlığı: "ORGB" + u32 aygıt + u32 paket kimliği + u32 veri boyu (küçük sonlu).
    public sealed class OpenRgbIstemci : IDisposable
    {
        public const int VarsayilanPort = 6742;

        private const uint REQUEST_CONTROLLER_COUNT = 0;
        private const uint REQUEST_CONTROLLER_DATA = 1;
        private const uint SET_CLIENT_NAME = 50;
        private const uint RGBCONTROLLER_UPDATELEDS = 1050;
        private const uint RGBCONTROLLER_SETCUSTOMMODE = 1100;

        private TcpClient tcp;
        private NetworkStream akis;

        public bool Bagli => tcp != null && tcp.Connected;

        public void Baglan(string ana = "127.0.0.1", int port = VarsayilanPort, int zamanAsimiMs = 1500)
        {
            Kapat();
            var t = new TcpClient();
            var sonuc = t.BeginConnect(ana, port, null, null);
            if (!sonuc.AsyncWaitHandle.WaitOne(zamanAsimiMs) || !t.Connected)
            {
                try { t.Close(); } catch (Exception) { }
                throw new IOException("OpenRGB sunucusuna bağlanılamadı (" + ana + ":" + port + ").");
            }
            t.EndConnect(sonuc);
            t.NoDelay = true;
            t.ReceiveTimeout = 4000;
            t.SendTimeout = 2000;
            tcp = t;
            akis = t.GetStream();
            Gonder(0, SET_CLIENT_NAME, Encoding.ASCII.GetBytes("EkranIsikKontrol\0"));
        }

        public int DenetleyiciSayisi()
        {
            Gonder(0, REQUEST_CONTROLLER_COUNT, new byte[0]);
            byte[] veri = Al(REQUEST_CONTROLLER_COUNT);
            return (int)BitConverter.ToUInt32(veri, 0);
        }

        public RgbDenetleyici Denetleyici(int sira)
        {
            Gonder((uint)sira, REQUEST_CONTROLLER_DATA, new byte[0]);
            byte[] veri = Al(REQUEST_CONTROLLER_DATA);
            RgbDenetleyici d = DenetleyiciCoz(veri);
            d.Sira = sira;
            return d;
        }

        public List<RgbDenetleyici> Denetleyiciler()
        {
            int n = DenetleyiciSayisi();
            var liste = new List<RgbDenetleyici>();
            for (int i = 0; i < n; i++) liste.Add(Denetleyici(i));
            return liste;
        }

        public void OzelModaGec(int sira) => Gonder((uint)sira, RGBCONTROLLER_SETCUSTOMMODE, new byte[0]);

        public void LedleriAyarla(int sira, IList<Rgb> renkler)
        {
            int boy = 4 + 2 + 4 * renkler.Count;
            var b = new byte[boy];
            BitConverter.GetBytes((uint)boy).CopyTo(b, 0);
            BitConverter.GetBytes((ushort)renkler.Count).CopyTo(b, 4);
            for (int i = 0; i < renkler.Count; i++)
            {
                b[6 + i * 4] = renkler[i].R;
                b[7 + i * 4] = renkler[i].G;
                b[8 + i * 4] = renkler[i].B;
            }
            Gonder((uint)sira, RGBCONTROLLER_UPDATELEDS, b);
        }

        public static byte[] PaketYap(uint aygit, uint kimlik, byte[] veri)
        {
            var b = new byte[16 + veri.Length];
            Encoding.ASCII.GetBytes("ORGB").CopyTo(b, 0);
            BitConverter.GetBytes(aygit).CopyTo(b, 4);
            BitConverter.GetBytes(kimlik).CopyTo(b, 8);
            BitConverter.GetBytes((uint)veri.Length).CopyTo(b, 12);
            veri.CopyTo(b, 16);
            return b;
        }

        private void Gonder(uint aygit, uint kimlik, byte[] veri)
        {
            if (akis == null) throw new IOException("OpenRGB bağlı değil.");
            byte[] p = PaketYap(aygit, kimlik, veri);
            akis.Write(p, 0, p.Length);
        }

        private byte[] Al(uint beklenen)
        {
            while (true)
            {
                byte[] baslik = TamOku(16);
                if (baslik[0] != 'O' || baslik[1] != 'R' || baslik[2] != 'G' || baslik[3] != 'B')
                    throw new IOException("OpenRGB yanıtı tanınmadı.");
                uint kimlik = BitConverter.ToUInt32(baslik, 8);
                uint boy = BitConverter.ToUInt32(baslik, 12);
                byte[] veri = TamOku((int)boy);
                if (kimlik == beklenen) return veri;
                // Sunucunun kendiliğinden gönderdiği bildirimleri (ör. aygıt listesi değişti) atla.
            }
        }

        private byte[] TamOku(int n)
        {
            var b = new byte[n];
            int alinan = 0;
            while (alinan < n)
            {
                int r = akis.Read(b, alinan, n - alinan);
                if (r <= 0) throw new IOException("OpenRGB bağlantısı kapandı.");
                alinan += r;
            }
            return b;
        }

        // Protokol 0 denetleyici verisi.
        public static RgbDenetleyici DenetleyiciCoz(byte[] v)
        {
            var o = new Ikili(v);
            var d = new RgbDenetleyici();
            o.U32(); // toplam boy
            d.Tur = o.I32();
            d.Ad = o.Dizge();
            d.Aciklama = o.Dizge();
            o.Dizge(); // sürüm
            o.Dizge(); // seri
            o.Dizge(); // konum
            int modSayisi = o.U16();
            d.EtkinMod = o.I32();
            for (int i = 0; i < modSayisi; i++)
            {
                d.Modlar.Add(o.Dizge());
                o.I32(); o.U32(); o.U32(); o.U32(); o.U32(); o.U32(); o.U32(); o.U32(); o.U32();
                int renk = o.U16();
                o.Atla(renk * 4);
            }
            int bolge = o.U16();
            for (int i = 0; i < bolge; i++)
            {
                o.Dizge(); o.I32(); o.U32(); o.U32(); o.U32();
                int matris = o.U16();
                if (matris > 0) o.Atla(matris);
            }
            d.LedSayisi = o.U16();
            return d;
        }

        private sealed class Ikili
        {
            private readonly byte[] d;
            private int p;
            public Ikili(byte[] d) { this.d = d; }
            private void Gerekli(int n) { if (p + n > d.Length) throw new FormatException("OpenRGB verisi eksik."); }
            public uint U32() { Gerekli(4); uint v = BitConverter.ToUInt32(d, p); p += 4; return v; }
            public int I32() { Gerekli(4); int v = BitConverter.ToInt32(d, p); p += 4; return v; }
            public ushort U16() { Gerekli(2); ushort v = BitConverter.ToUInt16(d, p); p += 2; return v; }
            public void Atla(int n) { Gerekli(n); p += n; }
            public string Dizge()
            {
                int n = U16();
                Gerekli(n);
                string s = Encoding.UTF8.GetString(d, p, n).TrimEnd('\0');
                p += n;
                return s;
            }
        }

        public void Kapat()
        {
            try { akis?.Dispose(); } catch (Exception) { }
            try { tcp?.Close(); } catch (Exception) { }
            akis = null;
            tcp = null;
        }

        public void Dispose() => Kapat();
    }

    // OpenRGB'yi arka planda (pencere açmadan) sunucu olarak çalıştırır ve fareyi denetler.
    // Kendi ayar klasörünü kullanır: yalnızca fare dedektörü açık kalır (klavye K55Kontrol'de, anakart/RAM taraması kapalı).
    public sealed class FareIsigi : IDisposable
    {
        private readonly object kilit = new object();
        private readonly OpenRgbIstemci istemci = new OpenRgbIstemci();
        private Process sunucu;
        private RgbDenetleyici fare;
        private int port = OpenRgbIstemci.VarsayilanPort;

        public string Durum { get; private set; } = "Başlatılmadı";
        public RgbDenetleyici Fare { get { lock (kilit) return fare; } }
        public bool Hazir { get { lock (kilit) return fare != null && istemci.Bagli; } }

        public static string AyarKlasoru => Path.Combine(Gunluk.Klasor, "openrgb");

        // Aranacak OpenRGB.exe yolları (ilk bulunan kullanılır).
        public static string OpenRgbBul(string ayardakiYol)
        {
            var adaylar = new List<string>();
            if (!string.IsNullOrWhiteSpace(ayardakiYol)) adaylar.Add(ayardakiYol);
            string uygulama = AppDomain.CurrentDomain.BaseDirectory;
            adaylar.Add(Path.Combine(uygulama, "OpenRGB", "OpenRGB.exe"));
            adaylar.Add(Path.Combine(uygulama, "..", "araclar", "openrgb-1.1", "app", "OpenRGB Windows 64-bit", "OpenRGB.exe"));
            adaylar.Add(@"E:\cloude\EkranIsikKontrol\araclar\openrgb-1.1\app\OpenRGB Windows 64-bit\OpenRGB.exe");
            adaylar.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenRGB", "OpenRGB.exe"));
            foreach (string a in adaylar)
            {
                try { if (File.Exists(a)) return Path.GetFullPath(a); } catch (Exception) { }
            }
            return null;
        }

        // Ayar dosyasında yalnızca fare dedektörlerini açık bırakır.
        public static string DedektorleriSuz(string json)
        {
            return Regex.Replace(json, "(\"[^\"\\r\\n]+\")(\\s*:\\s*)(true|false)", m =>
            {
                string ad = m.Groups[1].Value.Trim('"');
                // "SteelSeries Rival 3" ve "SteelSeries Rival 3 (Old Firmware)"; Rival 300/310 ve kablosuz model değil.
                bool ac = ad.Equals("SteelSeries Rival 3", StringComparison.OrdinalIgnoreCase) ||
                          ad.StartsWith("SteelSeries Rival 3 (", StringComparison.OrdinalIgnoreCase);
                return m.Groups[1].Value + m.Groups[2].Value + (ac ? "true" : "false");
            });
        }

        // Yalnızca "Detectors" bölümündeki değerlere dokunur.
        public static string AyarDosyasiniHazirla(string json)
        {
            int bas = json.IndexOf("\"detectors\"", StringComparison.Ordinal);
            if (bas < 0) return null;
            int ac = json.IndexOf('{', bas);
            int kapa = json.IndexOf('}', ac);
            if (ac < 0 || kapa < 0) return null;
            string blok = json.Substring(ac, kapa - ac + 1);
            return json.Substring(0, ac) + DedektorleriSuz(blok) + json.Substring(kapa + 1);
        }

        public bool Baslat(string openRgbYolu)
        {
            lock (kilit)
            {
                if (fare != null && istemci.Bagli) return true;
                try
                {
                    // Zaten çalışan bir OpenRGB sunucusu varsa (ör. kullanıcının açtığı) onu kullan.
                    if (!BaglanmayiDene(300))
                    {
                        string exe = OpenRgbBul(openRgbYolu);
                        if (exe == null) { Durum = "OpenRGB bulunamadı. Ayarlar sayfasından OpenRGB.exe yolunu seçin."; return false; }
                        AyarlariHazirla(exe);
                        SunucuyuBaslat(exe);
                        bool baglandi = false;
                        for (int i = 0; i < 40 && !baglandi; i++)
                        {
                            Thread.Sleep(250);
                            if (sunucu != null && sunucu.HasExited) break;
                            baglandi = BaglanmayiDene(500);
                        }
                        if (!baglandi) { Durum = "OpenRGB sunucusu başlamadı."; Gunluk.Uyari(Durum); return false; }
                    }
                    FareyiBul();
                    return fare != null;
                }
                catch (Exception ex)
                {
                    Durum = "OpenRGB hatası: " + ex.Message;
                    Gunluk.Hata("Fare ışığı başlatılamadı", ex);
                    istemci.Kapat();
                    return false;
                }
            }
        }

        private bool BaglanmayiDene(int ms)
        {
            try { istemci.Baglan("127.0.0.1", port, ms); return true; }
            catch (Exception) { return false; }
        }

        private void FareyiBul()
        {
            // Sunucu ilk açıldığında aygıt taraması birkaç saniye sürebilir.
            for (int i = 0; i < 20; i++)
            {
                var liste = istemci.Denetleyiciler();
                fare = liste.FirstOrDefault(d => d.Ad.IndexOf("Rival", StringComparison.OrdinalIgnoreCase) >= 0) ??
                       liste.FirstOrDefault(d => d.Tur == 6 /* fare */);
                if (fare != null)
                {
                    Durum = fare.Ad + " bağlı (" + fare.LedSayisi + " LED)";
                    Gunluk.Bilgi("Fare ışığı: " + Durum + " modlar: " + string.Join(", ", fare.Modlar));
                    istemci.OzelModaGec(fare.Sira);
                    return;
                }
                Thread.Sleep(500);
            }
            Durum = "OpenRGB çalışıyor ama fare bulunamadı.";
            Gunluk.Uyari(Durum);
        }

        private static void AyarlariHazirla(string exe)
        {
            Directory.CreateDirectory(AyarKlasoru);
            string hedef = Path.Combine(AyarKlasoru, "OpenRGB.json");
            if (File.Exists(hedef)) return;
            // Tam dedektör listesi, OpenRGB'nin kendi genel ayar dosyasından alınır.
            // Yoksa OpenRGB bir kez kendi klasörümüzde çalıştırılıp dosyayı üretmesi beklenir.
            string genel = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenRGB", "OpenRGB.json");
            string kaynak = File.Exists(genel) ? File.ReadAllText(genel) : null;
            if (kaynak == null)
            {
                var p = Process.Start(new ProcessStartInfo(exe, "--server --server-port 6799 --config \"" + AyarKlasoru + "\"")
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
                for (int i = 0; i < 40 && !File.Exists(hedef); i++) Thread.Sleep(250);
                try { if (!p.HasExited) p.Kill(); } catch (Exception) { }
                Thread.Sleep(300);
                if (File.Exists(hedef)) kaynak = File.ReadAllText(hedef);
            }
            string hazir = kaynak == null ? null : AyarDosyasiniHazirla(kaynak);
            if (hazir != null) File.WriteAllText(hedef, hazir, new UTF8Encoding(false));
            else Gunluk.Uyari("OpenRGB dedektör listesi hazırlanamadı; tüm aygıtlar taranacak.");
        }

        private void SunucuyuBaslat(string exe)
        {
            var psi = new ProcessStartInfo(exe, "--server --server-port " + port + " --config \"" + AyarKlasoru + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(exe)
            };
            sunucu = Process.Start(psi);
            Gunluk.Bilgi("OpenRGB sunucusu başlatıldı: " + exe + " (pid " + sunucu.Id + ")");
        }

        public bool RenkUygula(Rgb renk, int parlaklik)
        {
            lock (kilit)
            {
                if (fare == null) return false;
                try
                {
                    if (!istemci.Bagli) { istemci.Baglan("127.0.0.1", port, 800); istemci.OzelModaGec(fare.Sira); }
                    Rgb r = renk.Olcekle(parlaklik);
                    istemci.LedleriAyarla(fare.Sira, Enumerable.Repeat(r, Math.Max(1, fare.LedSayisi)).ToList());
                    return true;
                }
                catch (Exception ex)
                {
                    Gunluk.Uyari("Fare rengi gönderilemedi: " + ex.Message);
                    istemci.Kapat();
                    return false;
                }
            }
        }

        public void Dispose()
        {
            lock (kilit)
            {
                istemci.Dispose();
                try
                {
                    if (sunucu != null && !sunucu.HasExited) sunucu.Kill();
                }
                catch (Exception) { }
                sunucu = null;
                fare = null;
                Durum = "Durduruldu";
            }
        }
    }
}
