using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EkranIsikKontrol
{
    // Hızlı panel ve ana pencerenin ortak parçaları.
    public static class UiOrtak
    {
        public static readonly Rgb[] HazirRenkler =
        {
            new Rgb(255, 0, 0), new Rgb(255, 106, 0), new Rgb(255, 200, 0), new Rgb(0, 220, 80),
            new Rgb(0, 200, 255), new Rgb(91, 140, 255), new Rgb(170, 60, 255), new Rgb(255, 60, 170),
            new Rgb(255, 255, 255)
        };

        public static SolidColorBrush Firca(Rgb r) => new SolidColorBrush(Color.FromRgb(r.R, r.G, r.B));

        // Yuvarlak renk düğmesi.
        public static Button RenkDugmesi(Rgb renk, bool secili, Action<Rgb> tiklandi, double boyut = 26)
        {
            var b = new Button
            {
                Width = boyut,
                Height = boyut,
                Margin = new Thickness(0, 0, boyut < 26 ? 6 : 8, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = renk.Hex,
                Focusable = false
            };
            var sablon = new ControlTemplate(typeof(Button));
            var kenar = new FrameworkElementFactory(typeof(Border));
            kenar.SetValue(Border.CornerRadiusProperty, new CornerRadius(boyut / 2));
            kenar.SetValue(Border.BackgroundProperty, Firca(renk));
            kenar.SetValue(Border.BorderThicknessProperty, new Thickness(secili ? 3 : 1));
            kenar.SetValue(Border.BorderBrushProperty, secili ? Brushes.White : (Brush)Application.Current.Resources["Cizgi"]);
            sablon.VisualTree = kenar;
            b.Template = sablon;
            b.Click += delegate { tiklandi(renk); };
            return b;
        }

        public static Button ProfilDugmesi(Profil p, bool secili, Action<Profil> tiklandi)
        {
            var b = new Button
            {
                Content = p.Ad,
                Style = (Style)Application.Current.Resources[secili ? "BirincilDugme" : "TemelDugme"],
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0, 0, 8, 8),
                ToolTip = p.Ozet
            };
            b.Click += delegate { tiklandi(p); };
            return b;
        }
    }
}
