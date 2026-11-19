using System;
using System.Windows;
using System.Windows.Input;

namespace EkranIsikKontrol
{
    // Tepsi simgesine tıklayınca saatin üstünde açılan küçük panel.
    public partial class HizliPanel : Window
    {
        private bool guncelleniyor;
        private App A => App.Ana;

        public HizliPanel()
        {
            InitializeComponent();

            TumAyarlar.Click += delegate { A.PencereyiGoster(); };

            Parlaklik.ValueChanged += (s, e) =>
            {
                ParlaklikYazi.Text = ((int)Parlaklik.Value).ToString();
                if (!guncelleniyor) A.VcpAyarla(Vcp.Parlaklik, (int)Math.Round(Parlaklik.Value));
            };
            Kontrast.ValueChanged += (s, e) =>
            {
                KontrastYazi.Text = ((int)Kontrast.Value).ToString();
                if (!guncelleniyor) A.VcpAyarla(Vcp.Kontrast, (int)Math.Round(Kontrast.Value));
            };
            Gece.Click += delegate { A.GeceAcKapat(Gece.IsChecked == true); };
            GeceSicaklik.ValueChanged += (s, e) =>
            {
                int k = (int)(Math.Round(GeceSicaklik.Value / 100) * 100);
                GeceSicaklikYazi.Text = k + "K";
                if (!guncelleniyor) A.GeceSicakligiAyarla(k);
            };
            FareParlaklik.ValueChanged += (s, e) =>
            {
                FareParlaklikYazi.Text = ((int)FareParlaklik.Value).ToString();
                if (!guncelleniyor) A.FareRengiAyarla(A.FareRengi, (int)Math.Round(FareParlaklik.Value));
            };

            // Fare tekerleği: üzerinde durulan kaydırıcıyı 5'er adım oynatır.
            PreviewMouseWheel += (s, e) =>
            {
                if (e.OriginalSource is DependencyObject d)
                {
                    var k = BulKaydirici(d);
                    if (k != null)
                    {
                        double adim = k == GeceSicaklik ? 100 : 5;
                        k.Value = Math.Max(k.Minimum, Math.Min(k.Maximum, k.Value + (e.Delta > 0 ? adim : -adim) * (k.IsDirectionReversed ? -1 : 1)));
                        e.Handled = true;
                    }
                }
            };

            Deactivated += delegate { Hide(); A.PanelKapandi(); };
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Hide(); };
            A.DurumDegisti += delegate { if (IsVisible) Yenile(); };
        }

        private static System.Windows.Controls.Slider BulKaydirici(DependencyObject d)
        {
            while (d != null && !(d is System.Windows.Controls.Slider))
                d = System.Windows.Media.VisualTreeHelper.GetParent(d);
            return d as System.Windows.Controls.Slider;
        }

        public void Goster()
        {
            Yenile();
            Opacity = 0;
            Show();
            UpdateLayout();
            Rect alan = SystemParameters.WorkArea;
            Left = alan.Right - ActualWidth - 4;
            Top = alan.Bottom - ActualHeight - 4;
            Opacity = 1;
            Activate();
            Focus();
        }

        public void Yenile()
        {
            guncelleniyor = true;
            try
            {
                var m = A.Monitor;
                if (A.Taraniyor && m == null) { MonitorAdi.Text = "Monitör aranıyor…"; MonitorAlti.Text = ""; }
                else if (m == null) { MonitorAdi.Text = "Monitör bulunamadı"; MonitorAlti.Text = ""; }
                else
                {
                    MonitorAdi.Text = m.Ad;
                    string profil = A.Ayarlar.SonProfil != null ? " · Profil: " + A.Ayarlar.SonProfil : "";
                    MonitorAlti.Text = (A.Monitorler.Count > 1 ? A.Monitorler.Count + " monitör" : "DDC/CI") + profil;
                }

                bool p = A.Destekler(Vcp.Parlaklik), k = A.Destekler(Vcp.Kontrast);
                ParlaklikSatiri.IsEnabled = p;
                KontrastSatiri.IsEnabled = k;
                if (p) { var v = A.Deger(Vcp.Parlaklik); Parlaklik.Maximum = v.En; Parlaklik.Value = v.Simdiki; ParlaklikYazi.Text = v.Simdiki.ToString(); }
                else ParlaklikYazi.Text = "–";
                if (k) { var v = A.Deger(Vcp.Kontrast); Kontrast.Maximum = v.En; Kontrast.Value = v.Simdiki; KontrastYazi.Text = v.Simdiki.ToString(); }
                else KontrastYazi.Text = "–";
                DdcUyari.Visibility = !A.Taraniyor && m != null && !m.DdcCalisiyor ? Visibility.Visible : Visibility.Collapsed;

                GeceBolumu.Visibility = A.GeceKullanilabilir ? Visibility.Visible : Visibility.Collapsed;
                Gece.IsChecked = A.GeceAcik;
                GeceSicaklik.Value = A.GeceSicakligi;
                GeceSicaklikYazi.Text = A.GeceSicakligi + "K";

                FareBolumu.Visibility = A.Ayarlar.FareIsigiEtkin ? Visibility.Visible : Visibility.Collapsed;
                FareDurum.Text = A.FareHazir ? "" : A.FareDurumu;
                FareRenkleri.IsEnabled = FareParlaklik.IsEnabled = A.FareHazir;
                FareParlaklik.Value = A.Ayarlar.FareParlakligi;
                FareParlaklikYazi.Text = A.Ayarlar.FareParlakligi.ToString();
                FareRenkleri.Children.Clear();
                string secili = A.Ayarlar.FareRengi;
                foreach (Rgb r in UiOrtak.HazirRenkler)
                    FareRenkleri.Children.Add(UiOrtak.RenkDugmesi(r, string.Equals(r.Hex, secili, StringComparison.OrdinalIgnoreCase),
                        renk => A.FareRengiAyarla(renk, A.Ayarlar.FareParlakligi), 22));

                Profiller.Children.Clear();
                foreach (Profil pr in A.Ayarlar.Profiller)
                    Profiller.Children.Add(UiOrtak.ProfilDugmesi(pr, pr.Ad == A.Ayarlar.SonProfil, x => A.ProfilUygula(x)));
            }
            finally { guncelleniyor = false; }
        }
    }
}
