using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace EkranIsikKontrol
{
    // Donanım gerektirmeyen testler. derle.cmd her derlemede çalıştırır; biri bile başarısız olursa derleme durur.
    public static class BirimTesti
    {
        private static int basarili, basarisiz;

        public static int Main()
        {
            Gunluk.DosyayaYaz = false;
            Console.OutputEncoding = Encoding.UTF8;

            Test("Gece ışığı: bu bilgisayarın durum verisi (etkin, zamanlama)", () =>
            {
                byte[] d = Hex("43 42 01 00 0A 02 01 00 2A 06 B1 96 FC D7 06 2A 2B 0E 12 43 42 01 00 10 00 C6 14 BD BA 94 CF C6 86 DE EE 01 00 00 00 00");
                var s = GeceIsigi.DurumCoz(d);
                Esit(true, s.Etkin, "etkin");
                Esit(false, s.Elle, "elle");
                Esit(1795099441u, s.OnsozSaniye, "önsöz saniyesi");
            });

            Test("Gece ışığı: sem-reg örnekleri (en kısa / en uzun durum)", () =>
            {
                var kisa = GeceIsigi.DurumCoz(Hex("43 42 01 00 0a 02 01 00 2a 06 ae 81 d2 a9 06 2a 2b 0e 10 43 42 01 00 c6 14 e6 fd 92 d6 a9 91 81 ed 01 00 00 00 00"));
                Esit(false, kisa.Etkin, "kısa etkin");
                Esit(false, kisa.Elle, "kısa elle");
                var uzun = GeceIsigi.DurumCoz(Hex("43 42 01 00 0a 02 01 00 2a 06 ae 81 d2 a9 06 2a 2b 0e 15 43 42 01 00 10 00 d0 0a 02 c6 14 e6 fd 92 d6 a9 91 81 ed 01 00 00 00 00"));
                Esit(true, uzun.Etkin, "uzun etkin");
                Esit(true, uzun.Elle, "uzun elle");
            });

            Test("Gece ışığı: durum yaz-oku ve uzunluklar (43 / 41 / 38 bayt)", () =>
            {
                var t = new DateTime(2026, 11, 19, 18, 0, 0, DateTimeKind.Utc);
                var a = new GeceIsigi.Durum { OnsozSaniye = 100, Etkin = true, Elle = true };
                byte[] b = GeceIsigi.DurumYaz(a, t);
                Esit(43, b.Length, "etkin+elle uzunluk");
                var c = GeceIsigi.DurumCoz(b);
                Esit(true, c.Etkin, "etkin");
                Esit(true, c.Elle, "elle");
                Esit(41, GeceIsigi.DurumYaz(new GeceIsigi.Durum { Elle = true }, t).Length, "kapalı+elle uzunluk");
                Esit(38, GeceIsigi.DurumYaz(new GeceIsigi.Durum(), t).Length, "kapalı uzunluk");
                // Gövde uzunluk baytı doğru olmalı (indeks 18).
                Esit((byte)(b.Length - 19 - 3 + 0), b[18], "gövde uzunluğu");
            });

            Test("Gece ışığı: önsöz zamanı eski değerden en az 2 sn ileri", () =>
            {
                var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var gelecek = new GeceIsigi.Durum { OnsozSaniye = 4000000000u };
                Esit(4000000002u, GeceIsigi.DurumCoz(GeceIsigi.DurumYaz(gelecek, t)).OnsozSaniye, "önsöz");
            });

            Test("Gece ışığı: bu bilgisayarın ayar verisi", () =>
            {
                byte[] d = Hex("43 42 01 00 0A 02 01 00 2A 06 B0 96 FC D7 06 2A 2B 0E 1F 43 42 01 00 02 01 CA 14 0E 15 00 CA 1E 0E 07 00 CA 32 0E 11 2E 2C 00 CA 3C 0E 07 2E 39 00 00 00 00 00");
                var a = GeceIsigi.AyarCoz(d);
                Esit(true, a.ZamanlamaAcik, "zamanlama");
                Esit(false, a.Saatli, "saatli");
                Esit(false, a.Sicaklik.HasValue, "sıcaklık yok");
                Esit(false, a.Onizleme, "önizleme");
                // Hiçbir şey değiştirmeden yazınca gövde aynı kalmalı.
                byte[] y = GeceIsigi.AyarYaz(a, new DateTime(2026, 11, 19, 0, 0, 0, DateTimeKind.Utc));
                Esit(BitConverter.ToString(d.Skip(19).ToArray()), BitConverter.ToString(y.Skip(19).ToArray()), "gövde");
            });

            Test("Gece ışığı: sem-reg en uzun ayar örneği (2684K, önizleme)", () =>
            {
                byte[] d = Hex("43 42 01 00 0a 02 01 00 2a 06 fe cf ee a9 06 2a 2b 0e 2d 43 42 01 00 02 01 c2 0a 00 ca 14 0e 08 2e 0f 00 ca 1e 0e 0e 2e 1e 00 cf 28 f8 29 ca 32 0e 15 2e 03 00 ca 3c 0e 06 2e 14 00 c2 46 01 00 00 00 00");
                var a = GeceIsigi.AyarCoz(d);
                Esit(2684, a.Sicaklik ?? -1, "sıcaklık");
                Esit(true, a.Onizleme, "önizleme");
                Esit(true, a.Saatli, "saatli");
                byte[] y = GeceIsigi.AyarYaz(a, DateTime.UtcNow);
                Esit(d.Length, y.Length, "uzunluk");
                Esit(BitConverter.ToString(d.Skip(19).ToArray()), BitConverter.ToString(y.Skip(19).ToArray()), "gövde");
            });

            Test("Gece ışığı: sıcaklık ekle / değiştir", () =>
            {
                byte[] d = Hex("43 42 01 00 0A 02 01 00 2A 06 B0 96 FC D7 06 2A 2B 0E 1F 43 42 01 00 02 01 CA 14 0E 15 00 CA 1E 0E 07 00 CA 32 0E 11 2E 2C 00 CA 3C 0E 07 2E 39 00 00 00 00 00");
                var a = GeceIsigi.AyarCoz(d);
                a.Sicaklik = 3400;
                var b = GeceIsigi.AyarCoz(GeceIsigi.AyarYaz(a, DateTime.UtcNow));
                Esit(3400, b.Sicaklik ?? -1, "3400");
                Esit(true, b.ZamanlamaAcik, "zamanlama korunur");
                b.Sicaklik = 1200;
                Esit(1200, GeceIsigi.AyarCoz(GeceIsigi.AyarYaz(b, DateTime.UtcNow)).Sicaklik ?? -1, "1200");
            });

            Test("Zigzag VLQ", () =>
            {
                var l = new List<byte>();
                GeceIsigi.Vlq(l, GeceIsigi.ZigzagYaz(2684));
                Esit("F8-29", BitConverter.ToString(l.ToArray()), "2684");
                Esit(6500L, GeceIsigi.ZigzagCoz(GeceIsigi.ZigzagYaz(6500)), "6500");
            });

            Test("Monitör yetenekleri: ASUS VG249Q", () =>
            {
                var y = MonitorYetenekleri.Coz("(prot(monitor)type(LCD)model(VG249Q)cmds(01 02 03 07 0C E3 F3)vcp(02 04 05 06 08 10 12 14(05 06 08 0B) 16 18 1A 52 60(01 11 0F) 62 8D AC AE B6 C0 C6 C8 C9 CC(01 02 03 04 05 06 07 08 09 0A 0C 0D 11 12 14 1A 1E 1F 23 24 30 31) D6(01 04 05) DF)mswhql(1)asset_eep(40)mccs_ver(2.1))");
                Esit("VG249Q", y.Model, "model");
                Esit(true, y.Destekler(Vcp.Parlaklik), "parlaklık");
                Esit(true, y.Destekler(Vcp.Ses), "ses");
                Esit("5,6,8,11", string.Join(",", y.Secenekler[Vcp.RenkOnAyari]), "renk ön ayarları");
                Esit("1,17,15", string.Join(",", y.Secenekler[Vcp.GirisKaynagi]), "girişler");
                Esit(false, y.Destekler(0xE3), "cmds bölümü vcp değil");
            });

            Test("Monitör arayüz yolu -> WMI örnek kimliği", () =>
            {
                Esit(@"DISPLAY\AUS24A1\5&2d1a&0&UID4352",
                     MonitorAdlari.ArayuzYolundanOrnek(@"\\?\DISPLAY#AUS24A1#5&2d1a&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"), "yol");
            });

            Test("OpenRGB: denetleyici verisi çözme (protokol 0)", () =>
            {
                byte[] v = SahteDenetleyici();
                var d = OpenRgbIstemci.DenetleyiciCoz(v);
                Esit("SteelSeries Rival 3", d.Ad, "ad");
                Esit(6, d.Tur, "tür");
                Esit(2, d.Modlar.Count, "mod sayısı");
                Esit("Direct", d.Modlar[0], "mod 0");
                Esit(3, d.LedSayisi, "LED");
            });

            Test("OpenRGB: paket başlığı", () =>
            {
                byte[] p = OpenRgbIstemci.PaketYap(2, 1050, new byte[] { 1, 2, 3 });
                Esit("ORGB", Encoding.ASCII.GetString(p, 0, 4), "imza");
                Esit(2u, BitConverter.ToUInt32(p, 4), "aygıt");
                Esit(1050u, BitConverter.ToUInt32(p, 8), "kimlik");
                Esit(3u, BitConverter.ToUInt32(p, 12), "boy");
            });

            Test("OpenRGB: yalnızca Rival 3 dedektörü açık kalır", () =>
            {
                string j = "{\n \"Detectors\": {\n  \"detectors\": {\n   \"Corsair K55 RGB PRO\": true,\n   \"SteelSeries Rival 3\": true,\n   \"SteelSeries Rival 3 (Old Firmware)\": false,\n   \"SteelSeries Rival 3 Wireless\": true,\n   \"SteelSeries Rival 300\": true,\n   \"SteelSeries Rival 310 PUBG Edition\": true,\n   \"ASUS Aura SMBus Motherboard\": true\n  }\n },\n \"Server\": { \"legacy_workaround\": true }\n}";
                string s = FareIsigi.AyarDosyasiniHazirla(j);
                Esit(true, s.Contains("\"Corsair K55 RGB PRO\": false"), "K55 kapalı");
                Esit(true, s.Contains("\"SteelSeries Rival 3\": true"), "Rival 3 açık");
                Esit(true, s.Contains("\"SteelSeries Rival 3 (Old Firmware)\": true"), "eski yazılım açık");
                Esit(true, s.Contains("\"SteelSeries Rival 3 Wireless\": false"), "kablosuz kapalı");
                Esit(true, s.Contains("\"ASUS Aura SMBus Motherboard\": false"), "anakart kapalı");
                Esit(true, s.Contains("\"SteelSeries Rival 300\": false"), "Rival 300 kapalı");
                Esit(true, s.Contains("\"SteelSeries Rival 310 PUBG Edition\": false"), "Rival 310 kapalı");
                Esit(true, s.Contains("\"legacy_workaround\": true"), "başka bölüme dokunulmaz");
            });

            Test("Renk ölçekleme ve hex", () =>
            {
                Esit("#7F3300", new Rgb(255, 102, 0).Olcekle(50).Hex, "yarım");
                Esit(true, Rgb.HexCoz("#5B8CFF", out Rgb r) && r.G == 0x8C, "hex çöz");
                Esit(false, Rgb.HexCoz("xyz", out r), "bozuk hex");
            });

            Test("Ayarlar: JSON gidiş-dönüş ve eksik alan varsayılanları", () =>
            {
                var a = new Ayarlar { Profiller = Ayarlar.VarsayilanProfiller(), SonProfil = "Gece", FareParlakligi = 40 };
                var b = Ayarlar.JsonCoz(a.JsonYap());
                Esit(3, b.Profiller.Count, "profil sayısı");
                Esit(3400, b.Bul("gece").GeceSicakligi ?? -1, "gece sıcaklığı");
                Esit(40, b.FareParlakligi, "fare parlaklığı");
                var c = Ayarlar.JsonCoz("{\"SonProfil\":\"X\"}");
                Esit(true, c.FareIsigiEtkin, "eksik alan varsayılanı");
                Esit("#5B8CFF", c.FareRengi, "eksik renk varsayılanı");
                Esit(0, c.Profiller.Count, "eksik liste boş");
                Esit("Gece 2", a.YeniAd("Gece"), "yeni ad");
            });

            Console.WriteLine();
            Console.WriteLine("Birim testi: " + basarili + " geçti, " + basarisiz + " kaldı.");
            return basarisiz == 0 ? 0 : 1;
        }

        private static byte[] SahteDenetleyici()
        {
            var b = new List<byte>();
            void U32(uint v) => b.AddRange(BitConverter.GetBytes(v));
            void I32(int v) => b.AddRange(BitConverter.GetBytes(v));
            void U16(ushort v) => b.AddRange(BitConverter.GetBytes(v));
            void Dz(string s) { byte[] x = Encoding.UTF8.GetBytes(s + "\0"); U16((ushort)x.Length); b.AddRange(x); }
            U32(0); I32(6); Dz("SteelSeries Rival 3"); Dz("SteelSeries Mouse Device"); Dz(""); Dz(""); Dz("HID: x");
            U16(2); I32(0);
            foreach (string m in new[] { "Direct", "Breathing" })
            {
                Dz(m); I32(0); for (int i = 0; i < 8; i++) U32(0); U16(1); U32(0xFF);
            }
            U16(2);
            Dz("Logo"); I32(0); U32(1); U32(1); U32(1); U16(0);
            Dz("Strip"); I32(1); U32(2); U32(2); U32(2); U16(16); U32(1); U32(2); U32(0); U32(1);
            U16(3);
            for (int i = 0; i < 3; i++) { Dz("LED " + i); U32((uint)i); }
            U16(3); for (int i = 0; i < 3; i++) U32(0);
            return b.ToArray();
        }

        private static void Test(string ad, Action a)
        {
            try { a(); basarili++; Console.WriteLine("  [+] " + ad); }
            catch (Exception ex) { basarisiz++; Console.WriteLine("  [X] " + ad + " -> " + ex.Message); }
        }

        private static void Esit<T>(T beklenen, T gercek, string ne)
        {
            if (!EqualityComparer<T>.Default.Equals(beklenen, gercek))
                throw new Exception(ne + ": beklenen " + beklenen + ", gelen " + gercek);
        }

        private static byte[] Hex(string s) =>
            s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => Convert.ToByte(x, 16)).ToArray();
    }
}
