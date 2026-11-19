using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Win32;

namespace EkranIsikKontrol
{
    // Windows'un kendi gece ışığı. Resmi API yok; ayarlar CloudStore kayıt değerlerinde ikili biçimde durur.
    // Biçim, açık kaynak sem-reg kütüphanesindeki (MIT/Apache-2.0) çözümlemeye göre yazıldı:
    //   önsöz: 43 42 01 00 0A 02 01 00 2A 06 <vlq unix saniye> 2A 2B 0E <vlq gövde uzunluğu> 43 42 01
    //   durum gövdesi:  00 [10 00 = etkin] [D0 0A 02 = elle] C6 14 <vlq FILETIME> 00 00 00 00
    //   ayar gövdesi:   00 [02 01 = zamanlama açık] [C2 0A 00 = saatli] CA 14 <saat> 00 CA 1E <saat> 00
    //                   [CF 28 <zigzag vlq kelvin>] CA 32 <saat> 00 CA 3C <saat> 00 [C2 46 01 = önizleme] 00 00 00 00
    //   saat: [0E <sa>] [2E <dk>]  (sıfır olan kısım yazılmaz)
    public static class GeceIsigi
    {
        public const int EnSicak = 1200;
        public const int EnSoguk = 6500;
        public const int Varsayilan = 4000;

        private const string Kok = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\";
        public const string DurumAnahtari = Kok + @"default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";
        public const string AyarAnahtari = Kok + @"default$windows.data.bluelightreduction.settings\windows.data.bluelightreduction.settings";

        private static readonly byte[] OnsozBasi = { 0x43, 0x42, 0x01, 0x00, 0x0A, 0x02, 0x01, 0x00, 0x2A, 0x06 };
        private static readonly byte[] OnsozOrta = { 0x2A, 0x2B, 0x0E };
        private static readonly byte[] OnsozSonu = { 0x43, 0x42, 0x01 };

        // ================= Kayıt erişimi =================

        public static bool Kullanilabilir => Oku(DurumAnahtari) != null && Oku(AyarAnahtari) != null;

        public static bool Acik
        {
            get
            {
                byte[] d = Oku(DurumAnahtari);
                return d != null && DurumCoz(d).Etkin;
            }
        }

        public static int Sicaklik
        {
            get
            {
                byte[] d = Oku(AyarAnahtari);
                if (d == null) return Varsayilan;
                return AyarCoz(d).Sicaklik ?? Varsayilan;
            }
        }

        public static void AcKapat(bool acik)
        {
            byte[] d = Oku(DurumAnahtari);
            if (d == null) throw new InvalidOperationException("Gece ışığı kayıt değeri bulunamadı. Windows Ayarlar'da gece ışığını bir kez açıp kapatın.");
            Durum durum = DurumCoz(d);
            if (durum.Etkin == acik) return;
            durum.Etkin = acik;
            durum.Elle = true;
            Yaz(DurumAnahtari, DurumYaz(durum, DateTime.UtcNow));
            Gunluk.Bilgi("Gece ışığı " + (acik ? "açıldı" : "kapatıldı"));
        }

        public static void SicaklikAyarla(int kelvin)
        {
            kelvin = Math.Max(EnSicak, Math.Min(EnSoguk, kelvin));
            byte[] d = Oku(AyarAnahtari);
            if (d == null) throw new InvalidOperationException("Gece ışığı ayar değeri bulunamadı.");
            Ayar ayar = AyarCoz(d);
            if (ayar.Sicaklik == kelvin) return;
            ayar.Sicaklik = kelvin;
            ayar.Onizleme = false;
            Yaz(AyarAnahtari, AyarYaz(ayar, DateTime.UtcNow));
        }

        // Oturum açıldıktan sonra sıcaklık değişikliği, önizleme bir kez açılıp kapanmadan uygulanmayabiliyor.
        // Gece ışığı açıkken sıcaklık değişince kısa bir önizleme yaparak Windows'u dürteriz.
        public static void Durt()
        {
            byte[] d = Oku(AyarAnahtari);
            if (d == null) return;
            Ayar ayar = AyarCoz(d);
            ayar.Onizleme = true;
            Yaz(AyarAnahtari, AyarYaz(ayar, DateTime.UtcNow));
            Thread.Sleep(250);
            d = Oku(AyarAnahtari);
            if (d == null) return;
            ayar = AyarCoz(d);
            ayar.Onizleme = false;
            Yaz(AyarAnahtari, AyarYaz(ayar, DateTime.UtcNow.AddSeconds(2)));
        }

        private static byte[] Oku(string anahtar)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(anahtar))
                    return k?.GetValue("Data") as byte[];
            }
            catch (Exception) { return null; }
        }

        private static void Yaz(string anahtar, byte[] veri)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(anahtar))
                k.SetValue("Data", veri, RegistryValueKind.Binary);
        }

        // ================= Biçim =================

        public sealed class Durum
        {
            public uint OnsozSaniye;
            public bool Etkin;
            public bool Elle;
        }

        public sealed class Ayar
        {
            public uint OnsozSaniye;
            public bool ZamanlamaAcik;
            public bool Saatli;
            public byte[] Baslangic = new byte[0], Bitis = new byte[0];          // ham saat baytları
            public int? Sicaklik;
            public byte[] GunBatimi = new byte[0], GunDogumu = new byte[0];
            public bool Onizleme;
        }

        public static Durum DurumCoz(byte[] d)
        {
            var o = new Okuyucu(d);
            var durum = new Durum { OnsozSaniye = OnsozOku(o) };
            o.Sifir();
            durum.Etkin = o.Sabit(0x10, 0x00);
            durum.Elle = o.Sabit(0xD0, 0x0A, 0x02);
            return durum;
        }

        public static byte[] DurumYaz(Durum durum, DateTime utc)
        {
            var g = new List<byte> { 0x00 };
            if (durum.Etkin) g.AddRange(new byte[] { 0x10, 0x00 });
            if (durum.Elle) g.AddRange(new byte[] { 0xD0, 0x0A, 0x02 });
            g.AddRange(new byte[] { 0xC6, 0x14 });
            Vlq(g, (ulong)utc.ToFileTimeUtc());
            g.AddRange(new byte[] { 0, 0, 0, 0 });
            return Birlestir(OnsozZamani(durum.OnsozSaniye, utc), g);
        }

        public static Ayar AyarCoz(byte[] d)
        {
            var o = new Okuyucu(d);
            var a = new Ayar { OnsozSaniye = OnsozOku(o) };
            o.Sifir();
            a.ZamanlamaAcik = o.Sabit(0x02, 0x01);
            a.Saatli = o.Sabit(0xC2, 0x0A, 0x00);
            if (o.Sabit(0xCA, 0x14)) a.Baslangic = o.Saat();
            o.Sifir();
            if (o.Sabit(0xCA, 0x1E)) a.Bitis = o.Saat();
            o.Sifir();
            if (o.Sabit(0xCF, 0x28)) a.Sicaklik = (int)ZigzagCoz(o.Vlq());
            if (o.Sabit(0xCA, 0x32)) a.GunBatimi = o.Saat();
            o.Sifir();
            if (o.Sabit(0xCA, 0x3C)) a.GunDogumu = o.Saat();
            o.Sifir();
            a.Onizleme = o.Sabit(0xC2, 0x46, 0x01);
            return a;
        }

        public static byte[] AyarYaz(Ayar a, DateTime utc)
        {
            var g = new List<byte> { 0x00 };
            if (a.ZamanlamaAcik) g.AddRange(new byte[] { 0x02, 0x01 });
            if (a.Saatli) g.AddRange(new byte[] { 0xC2, 0x0A, 0x00 });
            g.AddRange(new byte[] { 0xCA, 0x14 }); g.AddRange(a.Baslangic);
            g.Add(0x00);
            g.AddRange(new byte[] { 0xCA, 0x1E }); g.AddRange(a.Bitis);
            g.Add(0x00);
            if (a.Sicaklik.HasValue)
            {
                g.AddRange(new byte[] { 0xCF, 0x28 });
                Vlq(g, ZigzagYaz(a.Sicaklik.Value));
            }
            g.AddRange(new byte[] { 0xCA, 0x32 }); g.AddRange(a.GunBatimi);
            g.Add(0x00);
            g.AddRange(new byte[] { 0xCA, 0x3C }); g.AddRange(a.GunDogumu);
            g.Add(0x00);
            if (a.Onizleme) g.AddRange(new byte[] { 0xC2, 0x46, 0x01 });
            g.AddRange(new byte[] { 0, 0, 0, 0 });
            return Birlestir(OnsozZamani(a.OnsozSaniye, utc), g);
        }

        // Windows yeni değeri, önsözdeki zaman eskisinden büyükse benimser.
        private static uint OnsozZamani(uint eski, DateTime utc)
        {
            uint simdi = (uint)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            return Math.Max(simdi, eski + 2);
        }

        private static uint OnsozOku(Okuyucu o)
        {
            if (!o.Sabit(OnsozBasi)) throw new FormatException("Gece ışığı verisi tanınmadı (önsöz).");
            uint saniye = (uint)o.Vlq();
            if (!o.Sabit(OnsozOrta)) throw new FormatException("Gece ışığı verisi tanınmadı (önsöz ortası).");
            o.Vlq(); // gövde uzunluğu
            if (!o.Sabit(OnsozSonu)) throw new FormatException("Gece ışığı verisi tanınmadı (gövde başı).");
            return saniye;
        }

        private static byte[] Birlestir(uint saniye, List<byte> govde)
        {
            var s = new List<byte>(OnsozBasi);
            Vlq(s, saniye);
            s.AddRange(OnsozOrta);
            Vlq(s, (ulong)govde.Count);
            s.AddRange(OnsozSonu);
            s.AddRange(govde);
            return s.ToArray();
        }

        public static void Vlq(List<byte> hedef, ulong v)
        {
            do
            {
                byte b = (byte)(v & 0x7F);
                v >>= 7;
                if (v != 0) b |= 0x80;
                hedef.Add(b);
            } while (v != 0);
        }

        public static ulong ZigzagYaz(long n) => (ulong)((n << 1) ^ (n >> 63));
        public static long ZigzagCoz(ulong z) => (long)(z >> 1) ^ -(long)(z & 1);

        private sealed class Okuyucu
        {
            private readonly byte[] d;
            private int p;
            public Okuyucu(byte[] d) { this.d = d; }

            public bool Sabit(params byte[] s)
            {
                if (p + s.Length > d.Length) return false;
                for (int i = 0; i < s.Length; i++) if (d[p + i] != s[i]) return false;
                p += s.Length;
                return true;
            }

            public void Sifir() { if (p < d.Length && d[p] == 0) p++; }

            public ulong Vlq()
            {
                ulong v = 0;
                int kaydir = 0;
                while (p < d.Length)
                {
                    byte b = d[p++];
                    v |= (ulong)(b & 0x7F) << kaydir;
                    if ((b & 0x80) == 0) return v;
                    kaydir += 7;
                    if (kaydir > 63) break;
                }
                throw new FormatException("Gece ışığı verisi tanınmadı (sayı).");
            }

            // [0E <vlq>] [2E <vlq>] kısmını ham olarak döndürür.
            public byte[] Saat()
            {
                int bas = p;
                if (Sabit(0x0E)) Vlq();
                if (Sabit(0x2E)) Vlq();
                var r = new byte[p - bas];
                Array.Copy(d, bas, r, 0, r.Length);
                return r;
            }
        }
    }
}
