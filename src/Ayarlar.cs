using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Win32;

namespace EkranIsikKontrol
{
    // Tek tıkla uygulanan ayar paketi. Boş (null) alanlara dokunulmaz.
    [DataContract]
    public sealed class Profil
    {
        [DataMember(Order = 0)] public string Ad = "Yeni profil";
        [DataMember(Order = 1)] public int? Parlaklik;
        [DataMember(Order = 2)] public int? Kontrast;
        [DataMember(Order = 3)] public bool? GeceIsigi;
        [DataMember(Order = 4)] public int? GeceSicakligi;
        [DataMember(Order = 5)] public string FareRengi;     // "#RRGGBB"
        [DataMember(Order = 6)] public int? FareParlakligi;
        [DataMember(Order = 7)] public int? RenkOnAyari;     // VCP 0x14 değeri

        public Profil Kopyala(string ad) => new Profil
        {
            Ad = ad, Parlaklik = Parlaklik, Kontrast = Kontrast, GeceIsigi = GeceIsigi, GeceSicakligi = GeceSicakligi,
            FareRengi = FareRengi, FareParlakligi = FareParlakligi, RenkOnAyari = RenkOnAyari
        };

        public string Ozet
        {
            get
            {
                var p = new List<string>();
                if (Parlaklik.HasValue) p.Add("Parlaklık " + Parlaklik);
                if (Kontrast.HasValue) p.Add("Kontrast " + Kontrast);
                if (RenkOnAyari.HasValue) p.Add("Renk " + Vcp.RenkOnAyariAdi((uint)RenkOnAyari.Value));
                if (GeceIsigi.HasValue) p.Add(GeceIsigi.Value ? "Gece ışığı açık" + (GeceSicakligi.HasValue ? " (" + GeceSicakligi + "K)" : "") : "Gece ışığı kapalı");
                if (FareRengi != null) p.Add("Fare " + FareRengi + (FareParlakligi.HasValue ? " %" + FareParlakligi : ""));
                else if (FareParlakligi.HasValue) p.Add("Fare ışığı %" + FareParlakligi);
                return p.Count == 0 ? "Boş profil" : string.Join(" · ", p);
            }
        }
    }

    [DataContract]
    public sealed class Ayarlar
    {
        [DataMember(Order = 0)] public List<Profil> Profiller = new List<Profil>();
        [DataMember(Order = 1)] public string SonProfil;
        [DataMember(Order = 2)] public bool FareIsigiEtkin = true;
        [DataMember(Order = 3)] public string OpenRgbYolu;
        [DataMember(Order = 4)] public string FareRengi = "#5B8CFF";
        [DataMember(Order = 5)] public int FareParlakligi = 100;
        [DataMember(Order = 6)] public bool AcilistaFareRenginiUygula = true;

        // Seri çözme alan ilk değerlerini çalıştırmaz; dosyada olmayan alanlar için varsayılanları burada ver.
        [OnDeserializing]
        private void SeriCozmeOncesi(StreamingContext c)
        {
            Profiller = new List<Profil>();
            FareIsigiEtkin = true;
            FareRengi = "#5B8CFF";
            FareParlakligi = 100;
            AcilistaFareRenginiUygula = true;
        }

        public static string DosyaYolu => Path.Combine(Gunluk.Klasor, "ayarlar.json");

        public static List<Profil> VarsayilanProfiller() => new List<Profil>
        {
            new Profil { Ad = "Gündüz", Parlaklik = 70, Kontrast = 80, GeceIsigi = false },
            new Profil { Ad = "Gece", Parlaklik = 20, Kontrast = 70, GeceIsigi = true, GeceSicakligi = 3400, FareParlakligi = 30 },
            new Profil { Ad = "Oyun", Parlaklik = 100, Kontrast = 80, GeceIsigi = false, FareParlakligi = 100 }
        };

        public Profil Bul(string ad) => Profiller.FirstOrDefault(p => string.Equals(p.Ad, ad, StringComparison.CurrentCultureIgnoreCase));

        public string YeniAd(string temel)
        {
            if (Bul(temel) == null) return temel;
            for (int i = 2; ; i++) if (Bul(temel + " " + i) == null) return temel + " " + i;
        }

        public static Ayarlar Yukle()
        {
            try
            {
                if (File.Exists(DosyaYolu))
                {
                    using (var akis = File.OpenRead(DosyaYolu))
                    {
                        var a = (Ayarlar)new DataContractJsonSerializer(typeof(Ayarlar)).ReadObject(akis);
                        if (a.Profiller == null) a.Profiller = VarsayilanProfiller();
                        return a;
                    }
                }
            }
            catch (Exception ex)
            {
                Gunluk.Hata("Ayarlar okunamadı, varsayılanlar kullanılıyor", ex);
                try { File.Copy(DosyaYolu, DosyaYolu + ".bozuk", true); } catch (Exception) { }
            }
            return new Ayarlar { Profiller = VarsayilanProfiller() };
        }

        public void Kaydet()
        {
            try
            {
                string gecici = DosyaYolu + ".tmp";
                using (var akis = File.Create(gecici))
                using (var yazici = JsonReaderWriterFactory.CreateJsonWriter(akis, Encoding.UTF8, true, true, "  "))
                    new DataContractJsonSerializer(typeof(Ayarlar)).WriteObject(yazici, this);
                if (File.Exists(DosyaYolu)) File.Replace(gecici, DosyaYolu, null);
                else File.Move(gecici, DosyaYolu);
            }
            catch (Exception ex) { Gunluk.Hata("Ayarlar kaydedilemedi", ex); }
        }

        public string JsonYap()
        {
            using (var ms = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(Ayarlar)).WriteObject(ms, this);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static Ayarlar JsonCoz(string json)
        {
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (Ayarlar)new DataContractJsonSerializer(typeof(Ayarlar)).ReadObject(ms);
        }
    }

    // Windows ile başlatma (HKCU\...\Run).
    public static class Baslangic
    {
        private const string Anahtar = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Ad = "EkranIsikKontrol";

        private static string Komut => "\"" + System.Reflection.Assembly.GetEntryAssembly().Location + "\" /gizli";

        public static bool Acik
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Anahtar))
                    return k?.GetValue(Ad) is string s && s.Length > 0;
            }
            set
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Anahtar))
                {
                    if (value) k.SetValue(Ad, Komut);
                    else k.DeleteValue(Ad, false);
                }
            }
        }

        // Program taşınmışsa kayıttaki yolu güncelle.
        public static void YoluTazele()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Anahtar, true))
            {
                if (k?.GetValue(Ad) is string s && s.Length > 0 && s != Komut) k.SetValue(Ad, Komut);
            }
        }
    }
}
