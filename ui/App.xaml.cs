using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using WF = System.Windows.Forms;

namespace EkranIsikKontrol
{
    // Uygulamanın merkezi: monitör (DDC/CI), gece ışığı, fare ışığı, profiller, sistem tepsisi.
    // Hızlı panel ve ana pencere aynı durumu buradan okur ve buraya yazar.
    public partial class App : Application
    {
        public static App Ana => (App)Current;
        public const string Surum = "1.0.0";

        public Ayarlar Ayarlar { get; private set; }
        public DdcIsci Ddc { get; private set; }
        public FareIsigi Fare { get; private set; }

        public List<FizikselMonitor> Monitorler { get; private set; } = new List<FizikselMonitor>();
        public FizikselMonitor Monitor { get; private set; }
        public bool Taraniyor { get; private set; } = true;

        // Seçili monitörün son okunan değerleri (kod -> değer).
        private readonly Dictionary<byte, VcpDeger> degerler = new Dictionary<byte, VcpDeger>();

        public bool GeceAcik { get; private set; }
        public int GeceSicakligi { get; private set; } = GeceIsigi.Varsayilan;
        public bool GeceKullanilabilir { get; private set; }

        // Durum değişince (arayüz iş parçacığında) tetiklenir.
        public event Action DurumDegisti;

        private Mutex tekKopya;
        private EventWaitHandle gosterSinyali;
        private WF.NotifyIcon tepsi;
        private AnaPencere pencere;
        private HizliPanel panel;
        private DispatcherTimer kayitZamanlayici, geceZamanlayici, geceIzleyici, taramaZamanlayici;
        private SonDegerIsci<int> geceSicaklikIsci;
        private SonDegerIsci<(Rgb, int)> fareIsci;
        private DateTime panelKapanisi;

        private const string TekKopyaAdi = "EkranIsikKontrol.TekKopya";
        private const string GosterAdi = "EkranIsikKontrol.Goster";

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            bool yeni;
            tekKopya = new Mutex(true, TekKopyaAdi, out yeni);
            if (!yeni)
            {
                try { EventWaitHandle.OpenExisting(GosterAdi).Set(); } catch (Exception) { }
                Shutdown();
                return;
            }
            gosterSinyali = new EventWaitHandle(false, EventResetMode.AutoReset, GosterAdi);
            var dinleyici = new Thread(() => { while (gosterSinyali.WaitOne()) Dispatcher.BeginInvoke(new Action(PencereyiGoster)); })
            { IsBackground = true };
            dinleyici.Start();

            DispatcherUnhandledException += (s, a) =>
            {
                Gunluk.Hata("Beklenmeyen hata", a.Exception);
                a.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, a) => Gunluk.Hata("Beklenmeyen hata (arka plan)", a.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, a) => { Gunluk.Hata("Görev hatası", a.Exception); a.SetObserved(); };

            Gunluk.Bilgi("Ekran Işık Kontrol " + Surum + " başladı. " + Environment.OSVersion);
            Ayarlar = Ayarlar.Yukle();
            try { Baslangic.YoluTazele(); } catch (Exception) { }

            kayitZamanlayici = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            kayitZamanlayici.Tick += delegate { kayitZamanlayici.Stop(); Ayarlar.Kaydet(); };

            Ddc = new DdcIsci();
            Ddc.YazmaBitti += (m, kod, tamam) => { if (!tamam) Dispatcher.BeginInvoke(new Action(Bildir)); };

            geceSicaklikIsci = new SonDegerIsci<int>("Gece ışığı sıcaklığı", k =>
            {
                GeceIsigi.SicaklikAyarla(k);
                if (GeceIsigi.Acik) GeceIsigi.Durt();
            }, 350);

            Fare = new FareIsigi();
            fareIsci = new SonDegerIsci<(Rgb, int)>("Fare ışığı", r => Fare.RenkUygula(r.Item1, r.Item2), 30);

            GeceyiOku();
            // Gece ışığı Windows'tan ya da zamanlamayla da değişebilir: arada bir kontrol et.
            geceIzleyici = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            geceIzleyici.Tick += delegate { if (GeceyiOku()) Bildir(); };
            geceIzleyici.Start();

            // Monitör takılıp çıkarılınca ya da çözünürlük değişince yeniden tara (olay arka arkaya gelebilir).
            taramaZamanlayici = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            taramaZamanlayici.Tick += delegate { taramaZamanlayici.Stop(); _ = MonitorleriTaraAsync(); };
            SystemEvents.DisplaySettingsChanged += EkranAyarlariDegisti;
            SystemEvents.PowerModeChanged += GucDegisti;

            TepsiyiKur();
            panel = new HizliPanel();
            pencere = new AnaPencere();
            if (!e.Args.Any(a => a.Equals("/gizli", StringComparison.OrdinalIgnoreCase)))
                pencere.Show();

            _ = MonitorleriTaraAsync();
            if (Ayarlar.FareIsigiEtkin) FareyiBaslat();
        }

        private void EkranAyarlariDegisti(object s, EventArgs e) =>
            Dispatcher.BeginInvoke(new Action(delegate { taramaZamanlayici.Stop(); taramaZamanlayici.Start(); }));

        private void GucDegisti(object s, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                taramaZamanlayici.Stop(); taramaZamanlayici.Start();
                // Uykudan sonra fare kendi rengine dönebilir.
                if (Ayarlar.FareIsigiEtkin) FareyiBaslat();
            }));
        }

        // Kaydırıcı sürüklenirken saniyede onlarca bildirim gelir; bunlar tek yenilemede birleştirilir.
        private bool bildirimBekliyor;
        public void Bildir()
        {
            if (bildirimBekliyor) return;
            bildirimBekliyor = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
            {
                bildirimBekliyor = false;
                DurumDegisti?.Invoke();
            }));
        }

        public void AyarlariKaydet()
        {
            kayitZamanlayici.Stop();
            kayitZamanlayici.Start();
        }

        // ================= Monitör =================

        public async Task MonitorleriTaraAsync()
        {
            Taraniyor = true;
            Bildir();
            string onceki = Monitor?.EkranAygiti;
            try
            {
                Monitorler = await Ddc.TaraAsync();
                Monitor = Monitorler.FirstOrDefault(m => m.EkranAygiti == onceki && m.DdcCalisiyor)
                          ?? Monitorler.FirstOrDefault(m => m.DdcCalisiyor)
                          ?? Monitorler.FirstOrDefault();
                await DegerleriOkuAsync();
            }
            catch (Exception ex) { Gunluk.Hata("Monitör taraması başarısız", ex); }
            Taraniyor = false;
            Bildir();
        }

        public async Task MonitorSecAsync(FizikselMonitor m)
        {
            if (m == null || m == Monitor) return;
            Monitor = m;
            await DegerleriOkuAsync();
            Bildir();
        }

        public async Task DegerleriOkuAsync()
        {
            var m = Monitor;
            lock (degerler) degerler.Clear();
            if (m == null || !m.DdcCalisiyor) return;
            foreach (byte kod in new[] { Vcp.Parlaklik, Vcp.Kontrast, Vcp.RenkOnAyari, Vcp.Ses, Vcp.GirisKaynagi })
            {
                if (!m.Yetenekler.Destekler(kod)) continue;
                VcpDeger v = await Ddc.OkuAsync(m, kod);
                lock (degerler) degerler[kod] = v;
            }
        }

        public VcpDeger Deger(byte kod)
        {
            lock (degerler) return degerler.TryGetValue(kod, out VcpDeger v) ? v : VcpDeger.Yok;
        }

        public bool Destekler(byte kod) => Monitor != null && Monitor.DdcCalisiyor && Monitor.Yetenekler.Destekler(kod) && Deger(kod).Basarili;

        // Kaydırıcıdan gelen 0-100 değeri (monitörün en büyük değerine göre ölçeklenir).
        public void VcpAyarla(byte kod, int deger, bool bildir = true)
        {
            if (Monitor == null) return;
            VcpDeger v = Deger(kod);
            uint en = v.Basarili && v.En > 0 ? v.En : 100;
            uint yeni = (uint)Math.Max(0, Math.Min(en, deger));
            lock (degerler) degerler[kod] = new VcpDeger { Basarili = true, Simdiki = yeni, En = en };
            Ddc.Yaz(Monitor, kod, yeni);
            if (bildir) Bildir();
        }

        // ================= Gece ışığı =================

        // Değer değiştiyse true döner.
        private bool GeceyiOku()
        {
            bool kullanilabilir = false, acik = false;
            int sicaklik = GeceSicakligi;
            try
            {
                kullanilabilir = GeceIsigi.Kullanilabilir;
                if (kullanilabilir) { acik = GeceIsigi.Acik; sicaklik = GeceIsigi.Sicaklik; }
            }
            catch (Exception ex) { Gunluk.Uyari("Gece ışığı okunamadı: " + ex.Message); }
            // Kullanıcı kaydırıcıyı sürüklerken okunan eski değer arayüzü geri itmesin.
            if (geceZamanlayici != null && geceZamanlayici.IsEnabled) sicaklik = GeceSicakligi;
            bool degisti = kullanilabilir != GeceKullanilabilir || acik != GeceAcik || sicaklik != GeceSicakligi;
            GeceKullanilabilir = kullanilabilir;
            GeceAcik = acik;
            GeceSicakligi = sicaklik;
            return degisti;
        }

        public void GeceAcKapat(bool acik)
        {
            try
            {
                GeceIsigi.AcKapat(acik);
                GeceAcik = acik;
            }
            catch (Exception ex)
            {
                Gunluk.Hata("Gece ışığı değiştirilemedi", ex);
                MessageBox.Show(ex.Message, "Gece ışığı", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Bildir();
        }

        public void GeceSicakligiAyarla(int kelvin, bool bildir = true)
        {
            GeceSicakligi = Math.Max(GeceIsigi.EnSicak, Math.Min(GeceIsigi.EnSoguk, kelvin));
            // İzleyici bu arada eski değeri okumasın diye kısa bir "yazılıyor" süresi.
            if (geceZamanlayici == null)
            {
                geceZamanlayici = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                geceZamanlayici.Tick += delegate { geceZamanlayici.Stop(); };
            }
            geceZamanlayici.Stop();
            geceZamanlayici.Start();
            geceSicaklikIsci.Iste(GeceSicakligi);
            if (bildir) Bildir();
        }

        // ================= Fare ışığı =================

        public string FareDurumu => !Ayarlar.FareIsigiEtkin ? "Kapalı" : Fare.Durum;
        public bool FareHazir => Ayarlar.FareIsigiEtkin && Fare.Hazir;

        public void FareyiBaslat()
        {
            Task.Run(() =>
            {
                bool tamam = Fare.Baslat(Ayarlar.OpenRgbYolu);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (tamam && Ayarlar.AcilistaFareRenginiUygula) FareRengiUygula();
                    Bildir();
                }));
            });
            Bildir();
        }

        public void FareyiDurdur()
        {
            Task.Run(() => { Fare.Dispose(); Dispatcher.BeginInvoke(new Action(Bildir)); });
        }

        public Rgb FareRengi => Rgb.HexCoz(Ayarlar.FareRengi, out Rgb r) ? r : new Rgb(91, 140, 255);

        public void FareRengiAyarla(Rgb renk, int parlaklik, bool bildir = true)
        {
            Ayarlar.FareRengi = renk.Hex;
            Ayarlar.FareParlakligi = Math.Max(0, Math.Min(100, parlaklik));
            AyarlariKaydet();
            FareRengiUygula();
            if (bildir) Bildir();
        }

        private void FareRengiUygula()
        {
            if (FareHazir) fareIsci.Iste((FareRengi, Ayarlar.FareParlakligi));
        }

        // ================= Profiller =================

        public async void ProfilUygula(Profil p)
        {
            if (p == null) return;
            Gunluk.Bilgi("Profil uygulanıyor: " + p.Ad);
            Ayarlar.SonProfil = p.Ad;
            AyarlariKaydet();
            if (p.Parlaklik.HasValue && Destekler(Vcp.Parlaklik)) VcpAyarla(Vcp.Parlaklik, p.Parlaklik.Value, false);
            if (p.Kontrast.HasValue && Destekler(Vcp.Kontrast)) VcpAyarla(Vcp.Kontrast, p.Kontrast.Value, false);
            if (p.RenkOnAyari.HasValue && Destekler(Vcp.RenkOnAyari)) VcpAyarla(Vcp.RenkOnAyari, p.RenkOnAyari.Value, false);
            if (p.GeceSicakligi.HasValue) GeceSicakligiAyarla(p.GeceSicakligi.Value, false);
            if (p.GeceIsigi.HasValue && p.GeceIsigi.Value != GeceAcik)
            {
                // Sıcaklık yazıldıktan sonra açılsın ki doğru renkle açılsın.
                if (p.GeceSicakligi.HasValue && p.GeceIsigi.Value) await Task.Delay(700);
                GeceAcKapat(p.GeceIsigi.Value);
            }
            if (p.FareRengi != null || p.FareParlakligi.HasValue)
            {
                Rgb r = p.FareRengi != null && Rgb.HexCoz(p.FareRengi, out Rgb pr) ? pr : FareRengi;
                FareRengiAyarla(r, p.FareParlakligi ?? Ayarlar.FareParlakligi, false);
            }
            Bildir();
        }

        // Şu anki ayarlardan profil oluşturur.
        public Profil SimdikiAyarlar(string ad)
        {
            var p = new Profil { Ad = ad };
            if (Destekler(Vcp.Parlaklik)) p.Parlaklik = (int)Deger(Vcp.Parlaklik).Simdiki;
            if (Destekler(Vcp.Kontrast)) p.Kontrast = (int)Deger(Vcp.Kontrast).Simdiki;
            if (Destekler(Vcp.RenkOnAyari)) p.RenkOnAyari = (int)Deger(Vcp.RenkOnAyari).Simdiki;
            if (GeceKullanilabilir)
            {
                p.GeceIsigi = GeceAcik;
                p.GeceSicakligi = GeceSicakligi;
            }
            if (Ayarlar.FareIsigiEtkin)
            {
                p.FareRengi = Ayarlar.FareRengi;
                p.FareParlakligi = Ayarlar.FareParlakligi;
            }
            return p;
        }

        // ================= Hata raporu =================

        public string HataRaporu()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Ekran Işık Kontrol hata raporu ===");
            sb.AppendLine("Sürüm: " + Surum + " | Tarih: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Windows: " + Environment.OSVersion + " | 64 bit: " + Environment.Is64BitOperatingSystem);
            sb.AppendLine(".NET: " + Environment.Version);
            sb.AppendLine("Monitörler:");
            foreach (var m in Monitorler)
                sb.AppendLine("  " + m.Ad + " | " + m.EkranAygiti + " | DDC " + m.DdcCalisiyor + " | model " + m.Yetenekler.Model +
                              " | kodlar " + string.Join(" ", m.Yetenekler.Kodlar.OrderBy(k => k).Select(k => k.ToString("X2"))));
            foreach (byte k in new[] { Vcp.Parlaklik, Vcp.Kontrast, Vcp.RenkOnAyari, Vcp.Ses, Vcp.GirisKaynagi })
            {
                VcpDeger v = Deger(k);
                sb.AppendLine("  0x" + k.ToString("X2") + ": " + (v.Basarili ? v.Simdiki + "/" + v.En : "-"));
            }
            try
            {
                foreach (var a in EkranModlari.Aygitlar())
                    sb.AppendLine("Ekran: " + a + " | " + a.AygitAdi + " | " + EkranModlari.Simdiki(a.AygitAdi));
            }
            catch (Exception ex) { sb.AppendLine("Ekran listesi hatası: " + ex.Message); }
            sb.AppendLine("Gece ışığı: kullanılabilir " + GeceKullanilabilir + " | açık " + GeceAcik + " | " + GeceSicakligi + "K");
            sb.AppendLine("Fare: " + FareDurumu + " | OpenRGB: " + (FareIsigi.OpenRgbBul(Ayarlar.OpenRgbYolu) ?? "bulunamadı"));
            sb.AppendLine("Windows ile başlat: " + SafeBaslangic());
            sb.AppendLine("--- Son günlük satırları ---");
            foreach (string s in Gunluk.SonSatirlar(120)) sb.AppendLine(s);
            return sb.ToString();
        }

        private static string SafeBaslangic()
        {
            try { return Baslangic.Acik.ToString(); } catch (Exception ex) { return "? " + ex.Message; }
        }

        // ================= Pencereler ve tepsi =================

        public void PencereyiGoster()
        {
            panel?.Hide();
            if (pencere == null) pencere = new AnaPencere();
            if (!pencere.IsVisible) pencere.Show();
            if (pencere.WindowState == WindowState.Minimized) pencere.WindowState = WindowState.Normal;
            pencere.Activate();
        }

        public void PaneliGoster()
        {
            if (panel.IsVisible) { panel.Hide(); return; }
            // Simgeye tıklamak paneli kapatır (odak kaybı); aynı tıklama paneli hemen yeniden açmasın.
            if ((DateTime.Now - panelKapanisi).TotalMilliseconds < 300) return;
            panel.Goster();
        }

        public void PanelKapandi() => panelKapanisi = DateTime.Now;

        private void TepsiyiKur()
        {
            tepsi = new WF.NotifyIcon();
            try
            {
                using (var akis = GetResourceStream(new Uri("pack://application:,,,/assets/EkranIsikKontrol.ico")).Stream)
                    tepsi.Icon = new System.Drawing.Icon(akis, WF.SystemInformation.SmallIconSize);
            }
            catch (Exception) { tepsi.Icon = System.Drawing.SystemIcons.Application; }
            tepsi.Text = "Ekran Işık Kontrol";

            var menu = new WF.ContextMenuStrip();
            menu.Opening += delegate
            {
                menu.Items.Clear();
                WF.ToolStripItem ac = menu.Items.Add("Ekran Işık Kontrol'ü aç", null, delegate { PencereyiGoster(); });
                ac.Font = new System.Drawing.Font(ac.Font, System.Drawing.FontStyle.Bold);
                menu.Items.Add(new WF.ToolStripSeparator());
                foreach (Profil p in Ayarlar.Profiller)
                {
                    Profil secilen = p;
                    var oge = new WF.ToolStripMenuItem(p.Ad, null, delegate { ProfilUygula(secilen); })
                    { Checked = p.Ad == Ayarlar.SonProfil };
                    menu.Items.Add(oge);
                }
                menu.Items.Add(new WF.ToolStripSeparator());
                if (GeceKullanilabilir)
                    menu.Items.Add(GeceAcik ? "Gece ışığını kapat" : "Gece ışığını aç", null, delegate { GeceAcKapat(!GeceAcik); });
                menu.Items.Add("Çıkış", null, delegate { Cik(); });
            };
            tepsi.ContextMenuStrip = menu;
            tepsi.MouseClick += (s, a) => { if (a.Button == WF.MouseButtons.Left) PaneliGoster(); };
            tepsi.MouseDoubleClick += (s, a) => { if (a.Button == WF.MouseButtons.Left) PencereyiGoster(); };
            tepsi.Visible = true;
        }

        public bool Kapaniyor { get; private set; }

        public void Cik()
        {
            Kapaniyor = true;
            Gunluk.Bilgi("Çıkılıyor.");
            Ayarlar.Kaydet();
            SystemEvents.DisplaySettingsChanged -= EkranAyarlariDegisti;
            SystemEvents.PowerModeChanged -= GucDegisti;
            if (tepsi != null) { tepsi.Visible = false; tepsi.Dispose(); }
            try { geceSicaklikIsci?.Dispose(); } catch (Exception) { }
            try { fareIsci?.Dispose(); } catch (Exception) { }
            try { Fare?.Dispose(); } catch (Exception) { }
            try { Ddc?.Dispose(); } catch (Exception) { }
            Shutdown();
        }
    }
}
