using System;
using System.Linq;
using System.Text;
using System.Threading;

namespace EkranIsikKontrol
{
    // Gerçek donanımda deneme. Her değişikliği geri alır.
    // Kullanım: DonanimTesti.exe [monitor] [modlar] [gece] [gece-dene] [fare]
    public static class DonanimTesti
    {
        public static int Main(string[] args)
        {
            Gunluk.DosyayaYaz = false;
            Console.OutputEncoding = Encoding.UTF8;
            var secim = args.Length == 0 ? new[] { "monitor", "modlar", "gece" } : args;
            int hata = 0;

            if (secim.Contains("monitor"))
            {
                Console.WriteLine("== Monitörler (DDC/CI)");
                using (var isci = new DdcIsci())
                {
                    var liste = isci.TaraAsync().Result;
                    if (liste.Count == 0) { Console.WriteLine("  fiziksel monitör yok"); hata++; }
                    foreach (var m in liste)
                    {
                        Console.WriteLine("  " + m.Ad + " | " + m.EkranAygiti + " | DDC " + m.DdcCalisiyor + " | model " + m.Yetenekler.Model);
                        if (!m.DdcCalisiyor) continue;
                        foreach (byte k in new[] { Vcp.Parlaklik, Vcp.Kontrast, Vcp.RenkOnAyari, Vcp.GirisKaynagi, Vcp.Ses })
                        {
                            var v = isci.OkuAsync(m, k).Result;
                            Console.WriteLine("    0x" + k.ToString("X2") + " = " + (v.Basarili ? v.Simdiki + "/" + v.En : "okunamadı"));
                        }
                        var once = isci.OkuAsync(m, Vcp.Parlaklik).Result;
                        uint hedef = once.Simdiki >= 40 ? once.Simdiki - 25 : once.Simdiki + 25;
                        // Hızlı kaydırıcı benzetimi: 10 değer arka arkaya; yalnızca sonuncusu gitmeli.
                        for (uint i = 0; i < 10; i++) isci.Yaz(m, Vcp.Parlaklik, hedef + (i % 3));
                        isci.Yaz(m, Vcp.Parlaklik, hedef);
                        isci.BekleAsync().Wait();
                        Thread.Sleep(300);
                        var sonra = isci.OkuAsync(m, Vcp.Parlaklik).Result;
                        isci.Yaz(m, Vcp.Parlaklik, once.Simdiki);
                        isci.BekleAsync().Wait();
                        Thread.Sleep(300);
                        var geri = isci.OkuAsync(m, Vcp.Parlaklik).Result;
                        bool tamam = sonra.Simdiki == hedef && geri.Simdiki == once.Simdiki;
                        Console.WriteLine("    yazma testi: " + once.Simdiki + " -> " + sonra.Simdiki + " (beklenen " + hedef + ") -> " + geri.Simdiki + "  " + (tamam ? "TAMAM" : "HATA"));
                        if (!tamam) hata++;
                    }
                }
            }

            if (secim.Contains("modlar"))
            {
                Console.WriteLine("== Ekran aygıtları ve modlar");
                foreach (var a in EkranModlari.Aygitlar())
                {
                    var simdiki = EkranModlari.Simdiki(a.AygitAdi);
                    var modlar = EkranModlari.Modlar(a.AygitAdi);
                    Console.WriteLine("  " + a + " | " + a.AygitAdi + (a.Sanal ? " | sanal" : "") + " | şimdi " + simdiki + " | " + modlar.Count + " mod");
                    foreach (var g in modlar.GroupBy(m => m.Cozunurluk).Take(4))
                        Console.WriteLine("    " + g.Key + ": " + string.Join(", ", g.Select(m => m.Hz)) + " Hz");
                }
            }

            if (secim.Contains("gece") || secim.Contains("gece-dene"))
            {
                Console.WriteLine("== Gece ışığı");
                Console.WriteLine("  kullanılabilir: " + GeceIsigi.Kullanilabilir + " | açık: " + GeceIsigi.Acik + " | sıcaklık: " + GeceIsigi.Sicaklik + "K");
                if (secim.Contains("gece-dene"))
                {
                    bool once = GeceIsigi.Acik;
                    int sicaklikOnce = GeceIsigi.Sicaklik;
                    GeceIsigi.AcKapat(!once);
                    Thread.Sleep(3000);
                    bool ara = GeceIsigi.Acik;
                    Console.WriteLine("  çevrildi: " + once + " -> " + ara + " (3 sn sonra Windows'un kayıttaki değeri)");
                    if (ara == once) hata++;
                    GeceIsigi.AcKapat(once);
                    Thread.Sleep(2500);
                    Console.WriteLine("  geri alındı: " + GeceIsigi.Acik);
                    if (GeceIsigi.Acik != once) hata++;
                }
            }

            if (secim.Contains("fare"))
            {
                Console.WriteLine("== Fare ışığı (OpenRGB)");
                using (var f = new FareIsigi())
                {
                    bool ok = f.Baslat(null);
                    Console.WriteLine("  " + f.Durum);
                    if (!ok) hata++;
                    else
                    {
                        foreach (var r in new[] { new Rgb(255, 0, 0), new Rgb(0, 255, 0), new Rgb(0, 80, 255) })
                        {
                            Console.WriteLine("  renk " + r.Hex + ": " + (f.RenkUygula(r, 100) ? "gönderildi" : "HATA"));
                            Thread.Sleep(1500);
                        }
                    }
                }
                Console.WriteLine("  OpenRGB sunucusu kapatıldı.");
            }

            // Değer geri yükleme: "vcp:10=50" (monitör kodu=değer), "gece-sicaklik=4000"
            foreach (string a in secim)
            {
                if (a.StartsWith("vcp:"))
                {
                    string[] p = a.Substring(4).Split('=');
                    byte kod = Convert.ToByte(p[0], 16);
                    using (var isci = new DdcIsci())
                    {
                        var m = isci.TaraAsync().Result.FirstOrDefault(x => x.DdcCalisiyor);
                        if (m == null) { hata++; continue; }
                        isci.Yaz(m, kod, uint.Parse(p[1]));
                        isci.BekleAsync().Wait();
                        Thread.Sleep(200);
                        Console.WriteLine("VCP 0x" + p[0] + " = " + isci.OkuAsync(m, kod).Result.Simdiki);
                    }
                }
                else if (a.StartsWith("gece-sicaklik="))
                {
                    GeceIsigi.SicaklikAyarla(int.Parse(a.Substring(14)));
                    if (GeceIsigi.Acik) GeceIsigi.Durt();
                    Console.WriteLine("Gece ışığı sıcaklığı = " + GeceIsigi.Sicaklik + "K");
                }
            }

            Console.WriteLine(hata == 0 ? "Donanım testi: TAMAM" : "Donanım testi: " + hata + " sorun");
            return hata == 0 ? 0 : 1;
        }
    }
}
