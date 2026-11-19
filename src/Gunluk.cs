using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace EkranIsikKontrol
{
    // Basit günlük: %APPDATA%\EkranIsikKontrol\gunluk.txt. "Hata raporunu kopyala" bu dosyanın sonunu kullanır.
    public static class Gunluk
    {
        private static readonly object kilit = new object();
        private static readonly LinkedList<string> sonSatirlar = new LinkedList<string>();
        private const int BellekSiniri = 400;
        private const long DosyaSiniri = 512 * 1024;

        public static string Klasor
        {
            get
            {
                string k = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EkranIsikKontrol");
                try { Directory.CreateDirectory(k); } catch (Exception) { }
                return k;
            }
        }

        public static string DosyaYolu => Path.Combine(Klasor, "gunluk.txt");

        // Testler dosyaya yazmasın diye kapatılabilir.
        public static bool DosyayaYaz = true;

        public static void Bilgi(string mesaj) => Yaz("Bilgi", mesaj);
        public static void Uyari(string mesaj) => Yaz("Uyarı", mesaj);
        public static void Hata(string mesaj, Exception ex = null) =>
            Yaz("Hata", ex == null ? mesaj : mesaj + " | " + ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace);

        private static void Yaz(string seviye, string mesaj)
        {
            string satir = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + seviye + "] " + mesaj;
            lock (kilit)
            {
                sonSatirlar.AddLast(satir);
                while (sonSatirlar.Count > BellekSiniri) sonSatirlar.RemoveFirst();
                if (!DosyayaYaz) return;
                try
                {
                    string yol = DosyaYolu;
                    if (File.Exists(yol) && new FileInfo(yol).Length > DosyaSiniri)
                    {
                        string eski = yol + ".1";
                        if (File.Exists(eski)) File.Delete(eski);
                        File.Move(yol, eski);
                    }
                    File.AppendAllText(yol, satir + Environment.NewLine, Encoding.UTF8);
                }
                catch (Exception) { }
            }
        }

        public static string[] SonSatirlar(int adet)
        {
            lock (kilit) return sonSatirlar.Skip(Math.Max(0, sonSatirlar.Count - adet)).ToArray();
        }
    }
}
