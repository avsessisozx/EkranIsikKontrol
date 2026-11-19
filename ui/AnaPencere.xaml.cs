using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace EkranIsikKontrol
{
    public partial class AnaPencere : Window
    {
        private bool guncelleniyor;
        private App A => App.Ana;

        // Çözünürlük onayı
        private DispatcherTimer onayZamanlayici;
        private int onayKalan;
        private string onayAygiti;
        private EkranModu onayModu;

        public AnaPencere()
        {
            InitializeComponent();

            KucultDugmesi.Click += delegate { WindowState = WindowState.Minimized; };
            KapatDugmesi.Click += delegate { Hide(); };
            Closing += (s, e) => { if (!A.Kapaniyor) { e.Cancel = true; Hide(); } };
            IsVisibleChanged += delegate { if (IsVisible) { Yenile(); EkranSayfasiniDoldur(); } };

            var sayfalar = new Dictionary<RadioButton, FrameworkElement>
            {
                { MenuMonitor, SayfaMonitor }, { MenuEkran, SayfaEkran }, { MenuGece, SayfaGece },
                { MenuFare, SayfaFare }, { MenuProfil, SayfaProfil }, { MenuAyar, SayfaAyar }
            };
            foreach (var s in sayfalar)
            {
                var dugme = s.Key;
                dugme.Checked += delegate
                {
                    foreach (var x in sayfalar) x.Value.Visibility = x.Key == dugme ? Visibility.Visible : Visibility.Collapsed;
                    if (dugme == MenuEkran) EkranSayfasiniDoldur();
                };
            }

            MonitorOlaylari();
            EkranOlaylari();
            GeceOlaylari();
            FareOlaylari();
            ProfilOlaylari();
            AyarOlaylari();

            A.DurumDegisti += delegate { if (IsVisible) Yenile(); };
            SurumYazisi.Text = "Sürüm " + App.Surum + " · Ayarlar: " + Gunluk.Klasor;
        }

        // ================= Genel yenileme =================

        private void Yenile()
        {
            guncelleniyor = true;
            try
            {
                var m = A.Monitor;
                YanMonitor.Text = m?.Ad ?? (A.Taraniyor ? "Monitör aranıyor…" : "Monitör bulunamadı");
                if (A.Taraniyor) { DurumNoktasi.Fill = (Brush)FindResource("Uyari"); DurumYazisi.Text = "Monitör taranıyor…"; }
                else if (m != null && m.DdcCalisiyor) { DurumNoktasi.Fill = (Brush)FindResource("Basari"); DurumYazisi.Text = "Monitör bağlı"; }
                else { DurumNoktasi.Fill = (Brush)FindResource("Tehlike"); DurumYazisi.Text = m == null ? "Monitör yok" : "DDC/CI yok"; }
                ProfilYazisi.Text = A.Ayarlar.SonProfil != null ? "Son profil: " + A.Ayarlar.SonProfil : "";

                MonitorSayfasiniYenile();
                GeceSayfasiniYenile();
                FareSayfasiniYenile();
                ProfilSayfasiniYenile();
                AyarSayfasiniYenile();
            }
            finally { guncelleniyor = false; }
        }

        // ================= Monitör =================

        private void MonitorOlaylari()
        {
            YenidenTara.Click += async delegate { await A.MonitorleriTaraAsync(); };
            MonitorListesi.SelectionChanged += async delegate
            {
                if (!guncelleniyor && MonitorListesi.SelectedItem is FizikselMonitor m) await A.MonitorSecAsync(m);
            };
            MParlaklik.ValueChanged += delegate
            {
                MParlaklikYazi.Text = ((int)MParlaklik.Value).ToString();
                if (!guncelleniyor) A.VcpAyarla(Vcp.Parlaklik, (int)Math.Round(MParlaklik.Value));
            };
            MKontrast.ValueChanged += delegate
            {
                MKontrastYazi.Text = ((int)MKontrast.Value).ToString();
                if (!guncelleniyor) A.VcpAyarla(Vcp.Kontrast, (int)Math.Round(MKontrast.Value));
            };
            MSes.ValueChanged += delegate
            {
                MSesYazi.Text = ((int)MSes.Value).ToString();
                if (!guncelleniyor) A.VcpAyarla(Vcp.Ses, (int)Math.Round(MSes.Value));
            };
            RenkOnAyari.SelectionChanged += delegate
            {
                if (!guncelleniyor && RenkOnAyari.SelectedItem is ComboBoxItem c && c.Tag is uint v) A.VcpAyarla(Vcp.RenkOnAyari, (int)v);
            };
            GirisDegistir.Click += delegate
            {
                if (!(GirisListesi.SelectedItem is ComboBoxItem c) || !(c.Tag is uint v)) return;
                if (A.Deger(Vcp.GirisKaynagi).Simdiki == v) return;
                var cevap = MessageBox.Show(this,
                    "Monitör “" + Vcp.GirisAdi(v) + "” girişine geçecek.\n\nBu bilgisayar o girişe bağlı değilse ekran kararır; " +
                    "geri dönmek için monitörün kendi düğmelerinden girişi değiştirmeniz gerekir.\n\nDevam edilsin mi?",
                    "Giriş kaynağını değiştir", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (cevap == MessageBoxResult.Yes) A.VcpAyarla(Vcp.GirisKaynagi, (int)v);
            };
        }

        private void MonitorSayfasiniYenile()
        {
            var m = A.Monitor;
            MonitorListesi.ItemsSource = A.Monitorler;
            MonitorListesi.SelectedItem = m;
            MonitorSecimKarti.Visibility = A.Monitorler.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            DdcYokKarti.Visibility = !A.Taraniyor && (m == null || !m.DdcCalisiyor) ? Visibility.Visible : Visibility.Collapsed;
            if (A.Monitorler.Count <= 1 && DdcYokKarti.Visibility == Visibility.Visible) MonitorSecimKarti.Visibility = Visibility.Visible;

            KaydiriciDoldur(Vcp.Parlaklik, MParlaklikSatiri, MParlaklik, MParlaklikYazi);
            KaydiriciDoldur(Vcp.Kontrast, MKontrastSatiri, MKontrast, MKontrastYazi);
            SesKarti.Visibility = A.Destekler(Vcp.Ses) ? Visibility.Visible : Visibility.Collapsed;
            KaydiriciDoldur(Vcp.Ses, SesKarti, MSes, MSesYazi);
            GoruntuKarti.Visibility = A.Destekler(Vcp.Parlaklik) || A.Destekler(Vcp.Kontrast) ? Visibility.Visible : Visibility.Collapsed;

            SecenekDoldur(Vcp.RenkOnAyari, RenkSatiri, RenkOnAyari, Vcp.RenkOnAyariAdi);
            GirisKarti.Visibility = A.Destekler(Vcp.GirisKaynagi) ? Visibility.Visible : Visibility.Collapsed;
            SecenekDoldur(Vcp.GirisKaynagi, GirisKarti, GirisListesi, Vcp.GirisAdi);
        }

        private void KaydiriciDoldur(byte kod, FrameworkElement satir, Slider s, TextBlock yazi)
        {
            bool var_ = A.Destekler(kod);
            satir.IsEnabled = var_;
            if (!var_) { yazi.Text = "–"; return; }
            VcpDeger v = A.Deger(kod);
            s.Maximum = v.En;
            s.Value = v.Simdiki;
            yazi.Text = v.Simdiki.ToString();
        }

        private void SecenekDoldur(byte kod, FrameworkElement satir, ComboBox kutu, Func<uint, string> ad)
        {
            bool var_ = A.Destekler(kod);
            satir.Visibility = var_ ? Visibility.Visible : Visibility.Collapsed;
            if (!var_) return;
            uint simdiki = A.Deger(kod).Simdiki;
            var secenekler = A.Monitor.Yetenekler.Secenekler.TryGetValue(kod, out List<uint> l) ? l.ToList() : new List<uint>();
            if (!secenekler.Contains(simdiki)) secenekler.Insert(0, simdiki);
            kutu.Items.Clear();
            foreach (uint v in secenekler)
            {
                var oge = new ComboBoxItem { Content = ad(v), Tag = v };
                kutu.Items.Add(oge);
                if (v == simdiki) kutu.SelectedItem = oge;
            }
        }

        // ================= Ekran =================

        private void EkranOlaylari()
        {
            EkranListesi.SelectionChanged += delegate { if (!guncelleniyor) CozunurlukleriDoldur(); };
            CozunurlukListesi.SelectionChanged += delegate { if (!guncelleniyor) HzDoldur(null); };
            ModUygula.Click += delegate { ModuUygula(); };
            OnayKalsin.Click += delegate { OnayiBitir(true); };
            OnayGeriAl.Click += delegate { OnayiBitir(false); };
        }

        private List<EkranModu> modlar = new List<EkranModu>();

        private void EkranSayfasiniDoldur()
        {
            if (onayZamanlayici != null && onayZamanlayici.IsEnabled) return;
            guncelleniyor = true;
            try
            {
                string onceki = (EkranListesi.SelectedItem as EkranAygiti)?.AygitAdi;
                var aygitlar = EkranModlari.Aygitlar();
                EkranListesi.ItemsSource = aygitlar;
                EkranListesi.SelectedItem = aygitlar.FirstOrDefault(a => a.AygitAdi == onceki) ?? aygitlar.FirstOrDefault();
            }
            catch (Exception ex) { Gunluk.Hata("Ekran listesi alınamadı", ex); }
            finally { guncelleniyor = false; }
            CozunurlukleriDoldur();
        }

        private void CozunurlukleriDoldur()
        {
            var a = EkranListesi.SelectedItem as EkranAygiti;
            guncelleniyor = true;
            try
            {
                CozunurlukListesi.Items.Clear();
                HzListesi.Items.Clear();
                if (a == null) { SimdikiMod.Text = ""; return; }
                modlar = EkranModlari.Modlar(a.AygitAdi);
                EkranModu simdi = EkranModlari.Simdiki(a.AygitAdi);
                SimdikiMod.Text = simdi != null ? "Şu an: " + simdi : "";
                foreach (var g in modlar.GroupBy(m => m.Cozunurluk))
                {
                    var oge = new ComboBoxItem { Content = g.Key + (simdi != null && simdi.Cozunurluk == g.Key ? "   (şu an)" : ""), Tag = g.Key };
                    CozunurlukListesi.Items.Add(oge);
                    if (simdi != null && simdi.Cozunurluk == g.Key) CozunurlukListesi.SelectedItem = oge;
                }
                if (CozunurlukListesi.SelectedItem == null && CozunurlukListesi.Items.Count > 0) CozunurlukListesi.SelectedIndex = 0;
            }
            finally { guncelleniyor = false; }
            HzDoldur(EkranModlari.Simdiki(a.AygitAdi));
        }

        private void HzDoldur(EkranModu simdi)
        {
            guncelleniyor = true;
            try
            {
                HzListesi.Items.Clear();
                string coz = (CozunurlukListesi.SelectedItem as ComboBoxItem)?.Tag as string;
                if (coz == null) return;
                foreach (var m in modlar.Where(x => x.Cozunurluk == coz))
                {
                    var oge = new ComboBoxItem { Content = m.Hz + " Hz", Tag = m };
                    HzListesi.Items.Add(oge);
                    if (simdi != null && simdi.Equals(m)) HzListesi.SelectedItem = oge;
                }
                if (HzListesi.SelectedItem == null && HzListesi.Items.Count > 0) HzListesi.SelectedIndex = 0;
            }
            finally { guncelleniyor = false; }
        }

        private void ModuUygula()
        {
            var a = EkranListesi.SelectedItem as EkranAygiti;
            var mod = (HzListesi.SelectedItem as ComboBoxItem)?.Tag as EkranModu;
            if (a == null || mod == null) return;
            EkranModu simdi = EkranModlari.Simdiki(a.AygitAdi);
            if (mod.Equals(simdi)) return;

            ModSonucu r = EkranModlari.Uygula(a.AygitAdi, mod, false);
            if (r == ModSonucu.Desteklenmiyor) { MessageBox.Show(this, "Bu mod ekran tarafından desteklenmiyor.", "Ekran", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (r == ModSonucu.Hata) { MessageBox.Show(this, "Mod uygulanamadı. Ayrıntı günlükte.", "Ekran", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            onayAygiti = a.AygitAdi;
            onayModu = mod;
            SimdikiMod.Text = "Şu an: " + mod + " (deneniyor)";
            onayKalan = 15;
            OnayKarti.Visibility = Visibility.Visible;
            OnaySayaci.Text = onayKalan + " saniye içinde onaylamazsanız eski ayara dönülecek.";
            if (onayZamanlayici == null)
            {
                onayZamanlayici = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                onayZamanlayici.Tick += delegate
                {
                    onayKalan--;
                    OnaySayaci.Text = onayKalan + " saniye içinde onaylamazsanız eski ayara dönülecek.";
                    if (onayKalan <= 0) OnayiBitir(false);
                };
            }
            onayZamanlayici.Start();
            // Çözünürlük değişince pencere yeri kayabilir; öne getir ki onay düğmesi görünsün.
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        private void OnayiBitir(bool kalsin)
        {
            onayZamanlayici?.Stop();
            OnayKarti.Visibility = Visibility.Collapsed;
            if (onayAygiti == null) return;
            if (kalsin) EkranModlari.Uygula(onayAygiti, onayModu, true);
            else EkranModlari.GeriAl(onayAygiti);
            onayAygiti = null;
            onayModu = null;
            EkranSayfasiniDoldur();
        }

        // ================= Gece ışığı =================

        private void GeceOlaylari()
        {
            GGece.Click += delegate { A.GeceAcKapat(GGece.IsChecked == true); };
            GSicaklik.ValueChanged += delegate
            {
                int k = (int)(Math.Round(GSicaklik.Value / 100) * 100);
                GSicaklikYazi.Text = k + "K";
                if (!guncelleniyor) A.GeceSicakligiAyarla(k);
            };
            WindowsGeceAyarlari.Click += delegate
            {
                try { Process.Start(new ProcessStartInfo("ms-settings:nightlight") { UseShellExecute = true }); }
                catch (Exception ex) { Gunluk.Uyari("Windows ayarları açılamadı: " + ex.Message); }
            };
        }

        private void GeceSayfasiniYenile()
        {
            GeceYokKarti.Visibility = A.GeceKullanilabilir ? Visibility.Collapsed : Visibility.Visible;
            GeceKarti.IsEnabled = A.GeceKullanilabilir;
            GGece.IsChecked = A.GeceAcik;
            GSicaklik.Value = A.GeceSicakligi;
            GSicaklikYazi.Text = A.GeceSicakligi + "K";
        }

        // ================= Fare ışığı =================

        private void FareOlaylari()
        {
            FEtkin.Click += delegate
            {
                A.Ayarlar.FareIsigiEtkin = FEtkin.IsChecked == true;
                A.AyarlariKaydet();
                if (A.Ayarlar.FareIsigiEtkin) A.FareyiBaslat(); else A.FareyiDurdur();
                A.Bildir();
            };
            RoutedPropertyChangedEventHandler<double> rgb = delegate
            {
                FRYazi.Text = ((int)FR.Value).ToString();
                FGYazi.Text = ((int)FG.Value).ToString();
                FBYazi.Text = ((int)FB.Value).ToString();
                if (guncelleniyor) return;
                var r = new Rgb((byte)FR.Value, (byte)FG.Value, (byte)FB.Value);
                FOnizleme.Background = UiOrtak.Firca(r);
                A.FareRengiAyarla(r, A.Ayarlar.FareParlakligi);
            };
            FR.ValueChanged += rgb;
            FG.ValueChanged += rgb;
            FB.ValueChanged += rgb;
            FParlaklik.ValueChanged += delegate
            {
                FParlaklikYazi.Text = ((int)FParlaklik.Value).ToString();
                if (!guncelleniyor) A.FareRengiAyarla(A.FareRengi, (int)Math.Round(FParlaklik.Value));
            };
            FHex.LostKeyboardFocus += delegate { HexUygula(); };
            FHex.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) HexUygula(); };
            FAcilis.Click += delegate { A.Ayarlar.AcilistaFareRenginiUygula = FAcilis.IsChecked == true; A.AyarlariKaydet(); };
        }

        private void HexUygula()
        {
            if (Rgb.HexCoz(FHex.Text, out Rgb r)) A.FareRengiAyarla(r, A.Ayarlar.FareParlakligi);
            else FHex.Text = A.Ayarlar.FareRengi;
        }

        private void FareSayfasiniYenile()
        {
            FEtkin.IsChecked = A.Ayarlar.FareIsigiEtkin;
            FDurum.Text = A.FareDurumu;
            FDurumNoktasi.Fill = (Brush)FindResource(A.FareHazir ? "Basari" : A.Ayarlar.FareIsigiEtkin ? "Uyari" : "SolukMetin");
            GgUyarisi.Visibility = A.Ayarlar.FareIsigiEtkin && Process.GetProcessesByName("SteelSeriesGG").Length > 0
                ? Visibility.Visible : Visibility.Collapsed;
            FareRenkKarti.IsEnabled = A.FareHazir;
            FareRenkKarti.Opacity = A.FareHazir ? 1 : 0.5;

            Rgb r = A.FareRengi;
            FR.Value = r.R; FG.Value = r.G; FB.Value = r.B;
            FRYazi.Text = r.R.ToString(); FGYazi.Text = r.G.ToString(); FBYazi.Text = r.B.ToString();
            FOnizleme.Background = UiOrtak.Firca(r);
            if (!FHex.IsKeyboardFocused) FHex.Text = r.Hex;
            FParlaklik.Value = A.Ayarlar.FareParlakligi;
            FParlaklikYazi.Text = A.Ayarlar.FareParlakligi.ToString();
            FAcilis.IsChecked = A.Ayarlar.AcilistaFareRenginiUygula;

            FRenkler.Children.Clear();
            foreach (Rgb h in UiOrtak.HazirRenkler)
                FRenkler.Children.Add(UiOrtak.RenkDugmesi(h, string.Equals(h.Hex, r.Hex, StringComparison.OrdinalIgnoreCase),
                    x => A.FareRengiAyarla(x, A.Ayarlar.FareParlakligi), 34));
        }

        // ================= Profiller =================

        private void ProfilOlaylari()
        {
            ProfilKaydet.Click += delegate
            {
                string ad = (YeniProfilAdi.Text ?? "").Trim();
                if (ad.Length == 0) ad = A.Ayarlar.YeniAd("Profil");
                else if (A.Ayarlar.Bul(ad) != null)
                {
                    if (MessageBox.Show(this, "“" + ad + "” adında bir profil var. Şu anki ayarlarla güncellensin mi?", "Profil",
                            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    A.Ayarlar.Profiller[A.Ayarlar.Profiller.IndexOf(A.Ayarlar.Bul(ad))] = A.SimdikiAyarlar(A.Ayarlar.Bul(ad).Ad);
                    A.AyarlariKaydet();
                    YeniProfilAdi.Text = "";
                    A.Bildir();
                    return;
                }
                A.Ayarlar.Profiller.Add(A.SimdikiAyarlar(ad));
                A.Ayarlar.SonProfil = ad;
                A.AyarlariKaydet();
                YeniProfilAdi.Text = "";
                A.Bildir();
            };
        }

        private void ProfilSayfasiniYenile()
        {
            ProfilListesi.Children.Clear();
            foreach (Profil p in A.Ayarlar.Profiller.ToList())
            {
                Profil pr = p;
                bool secili = p.Ad == A.Ayarlar.SonProfil;
                var kart = new Border
                {
                    Style = (Style)FindResource("KartKutusu"),
                    Margin = new Thickness(0, 0, 0, 10),
                    Padding = new Thickness(18, 14, 18, 14),
                    BorderBrush = (Brush)FindResource(secili ? "Vurgu" : "Cizgi")
                };
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var yazi = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                yazi.Children.Add(new TextBlock { Text = p.Ad, FontSize = 15, FontWeight = FontWeights.SemiBold });
                yazi.Children.Add(new TextBlock { Text = p.Ozet, FontSize = 12.5, Foreground = (Brush)FindResource("SolukMetin"), Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
                g.Children.Add(yazi);

                var dugmeler = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var uygula = new Button { Content = "Uygula", Style = (Style)FindResource("BirincilDugme"), Margin = new Thickness(0, 0, 8, 0) };
                uygula.Click += delegate { A.ProfilUygula(pr); };
                var guncelle = new Button { Content = "Şu anki ayarlarla güncelle", Style = (Style)FindResource("TemelDugme"), Margin = new Thickness(0, 0, 8, 0) };
                guncelle.Click += delegate
                {
                    int i = A.Ayarlar.Profiller.IndexOf(pr);
                    if (i < 0) return;
                    A.Ayarlar.Profiller[i] = A.SimdikiAyarlar(pr.Ad);
                    A.AyarlariKaydet();
                    A.Bildir();
                };
                var sil = new Button { Content = "Sil", Style = (Style)FindResource("TehlikeDugme") };
                sil.Click += delegate
                {
                    if (MessageBox.Show(this, "“" + pr.Ad + "” profili silinsin mi?", "Profil", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    A.Ayarlar.Profiller.Remove(pr);
                    if (A.Ayarlar.SonProfil == pr.Ad) A.Ayarlar.SonProfil = null;
                    A.AyarlariKaydet();
                    A.Bildir();
                };
                dugmeler.Children.Add(uygula);
                dugmeler.Children.Add(guncelle);
                dugmeler.Children.Add(sil);
                Grid.SetColumn(dugmeler, 1);
                g.Children.Add(dugmeler);
                kart.Child = g;
                ProfilListesi.Children.Add(kart);
            }
        }

        // ================= Ayarlar =================

        private void AyarOlaylari()
        {
            WindowsIleBaslat.Click += delegate
            {
                try { Baslangic.Acik = WindowsIleBaslat.IsChecked == true; }
                catch (Exception ex) { Gunluk.Hata("Başlangıç ayarı değiştirilemedi", ex); }
                A.Bildir();
            };
            OpenRgbSec.Click += delegate
            {
                var d = new Microsoft.Win32.OpenFileDialog { Filter = "OpenRGB|OpenRGB.exe|Program (*.exe)|*.exe", Title = "OpenRGB.exe dosyasını seçin" };
                if (d.ShowDialog(this) == true)
                {
                    A.Ayarlar.OpenRgbYolu = d.FileName;
                    A.AyarlariKaydet();
                    if (A.Ayarlar.FareIsigiEtkin) { A.FareyiDurdur(); A.FareyiBaslat(); }
                    A.Bildir();
                }
            };
            OpenRgbSifirla.Click += delegate
            {
                A.Ayarlar.OpenRgbYolu = null;
                A.AyarlariKaydet();
                A.Bildir();
            };
            RaporKopyala.Click += delegate
            {
                try
                {
                    Clipboard.SetText(A.HataRaporu());
                    RaporDurumu.Text = "Rapor panoya kopyalandı. Sohbete yapıştırabilirsiniz.";
                }
                catch (Exception ex) { RaporDurumu.Text = "Kopyalanamadı: " + ex.Message; }
            };
            GunlukAc.Click += delegate
            {
                try { Process.Start(new ProcessStartInfo(Gunluk.Klasor) { UseShellExecute = true }); }
                catch (Exception ex) { Gunluk.Uyari("Klasör açılamadı: " + ex.Message); }
            };
            ProgramdanCik.Click += delegate { A.Cik(); };
        }

        private void AyarSayfasiniYenile()
        {
            try { WindowsIleBaslat.IsChecked = Baslangic.Acik; } catch (Exception) { }
            string yol = FareIsigi.OpenRgbBul(A.Ayarlar.OpenRgbYolu);
            OpenRgbYolu.Text = yol != null ? yol : "OpenRGB bulunamadı. Fare ışığı için OpenRGB.exe dosyasını seçin.";
        }
    }
}
