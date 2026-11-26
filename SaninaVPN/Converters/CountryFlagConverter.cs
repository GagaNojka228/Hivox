using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Hivox.Converters
{
    public class CountryFlagConverter : IValueConverter
    {
        private static readonly ConcurrentDictionary<string, ImageSource> Cache = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string code = (value?.ToString() ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(code) || code == "GL" || code.Length != 2)
                return null;
            if (code == "UK") code = "GB";
            return Cache.GetOrAdd(code, BuildFlagImage);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();

        private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
        private static Brush B(string hex)
        {
            var b = new SolidColorBrush(C(hex));
            b.Freeze();
            return b;
        }

        private static void Rect(DrawingGroup g, string hex, double x, double y, double w, double h)
        {
            g.Children.Add(new GeometryDrawing(B(hex), null, new RectangleGeometry(new Rect(x, y, w, h))));
        }

        private static void Poly(DrawingGroup g, string hex, params (double X, double Y)[] pts)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(pts[0].X, pts[0].Y), true, true);
                for (int i = 1; i < pts.Length; i++)
                    ctx.LineTo(new Point(pts[i].X, pts[i].Y), false, false);
            }
            geo.Freeze();
            g.Children.Add(new GeometryDrawing(B(hex), null, geo));
        }

        private static void Circle(DrawingGroup g, string hex, double cx, double cy, double r)
        {
            g.Children.Add(new GeometryDrawing(B(hex), null, new EllipseGeometry(new Point(cx, cy), r, r)));
        }

        private static void HStripes(DrawingGroup g, params string[] colors)
        {
            double h = 24.0 / colors.Length;
            for (int i = 0; i < colors.Length; i++)
                Rect(g, colors[i], 0, i * h, 36, h + 0.15);
        }

        private static void VStripes(DrawingGroup g, params string[] colors)
        {
            double w = 36.0 / colors.Length;
            for (int i = 0; i < colors.Length; i++)
                Rect(g, colors[i], i * w, 0, w + 0.15, 24);
        }

        private static void NordicCross(DrawingGroup g, string bg, string outer, string inner = null)
        {
            Rect(g, bg, 0, 0, 36, 24);
            Rect(g, outer, 10, 0, 6, 24);
            Rect(g, outer, 0, 9, 36, 6);
            if (!string.IsNullOrEmpty(inner))
            {
                Rect(g, inner, 11.5, 0, 3, 24);
                Rect(g, inner, 0, 10.5, 36, 3);
            }
        }

        private static ImageSource BuildFlagImage(string code)
        {
            var g = new DrawingGroup();
            g.ClipGeometry = new RectangleGeometry(new Rect(0, 0, 36, 24), 3, 3);

            switch (code)
            {
                case "RU": HStripes(g, "#FFFFFF", "#0039A6", "#D52B1E"); break;
                case "DE": HStripes(g, "#18181B", "#DD0000", "#FFCE00"); break;
                case "NL": HStripes(g, "#AE1C28", "#FFFFFF", "#21468B"); break;
                case "LU": HStripes(g, "#EA1D25", "#FFFFFF", "#00A1DE"); break;
                case "EE": HStripes(g, "#0072CE", "#18181B", "#FFFFFF"); break;
                case "LT": HStripes(g, "#FDB913", "#006A44", "#C1272D"); break;
                case "LV":
                    Rect(g, "#9E3039", 0, 0, 36, 24);
                    Rect(g, "#FFFFFF", 0, 9.6, 36, 4.8);
                    break;
                case "PL": HStripes(g, "#FFFFFF", "#DC143C"); break;
                case "UA": HStripes(g, "#005BBB", "#FFD500"); break;
                case "ID": HStripes(g, "#CE1126", "#FFFFFF"); break;
                case "AT": HStripes(g, "#ED2939", "#FFFFFF", "#ED2939"); break;
                case "HU": HStripes(g, "#CD2A3E", "#FFFFFF", "#436F4D"); break;
                case "BG": HStripes(g, "#FFFFFF", "#00966E", "#D62612"); break;
                case "AM": HStripes(g, "#D90012", "#0033A0", "#F2A800"); break;
                case "ES":
                    Rect(g, "#AA151B", 0, 0, 36, 24);
                    Rect(g, "#F1BF00", 0, 6, 36, 12);
                    Rect(g, "#AA151B", 8, 9, 4, 6);
                    break;
                case "AZ":
                    HStripes(g, "#00B5E2", "#EF3340", "#509E2F");
                    Circle(g, "#FFFFFF", 17.5, 12, 3.2);
                    Circle(g, "#EF3340", 18.4, 12, 2.5);
                    Circle(g, "#FFFFFF", 20.3, 12, 1.0);
                    break;
                case "UZ":
                    HStripes(g, "#0099B5", "#FFFFFF", "#1EB53A");
                    Rect(g, "#CE1126", 0, 7.6, 36, 1.0);
                    Rect(g, "#CE1126", 0, 15.4, 36, 1.0);
                    Circle(g, "#FFFFFF", 6.5, 4, 2.2);
                    Circle(g, "#0099B5", 7.3, 4, 1.8);
                    break;
                case "BY":
                    Rect(g, "#C8313E", 0, 0, 36, 16);
                    Rect(g, "#4AA657", 0, 16, 36, 8);
                    Rect(g, "#FFFFFF", 0, 0, 5, 24);
                    Rect(g, "#C8313E", 1.2, 2, 2.6, 20);
                    break;
                case "FR": VStripes(g, "#0055A4", "#FFFFFF", "#EF4135"); break;
                case "IT": VStripes(g, "#009246", "#FFFFFF", "#CE2B37"); break;
                case "BE": VStripes(g, "#18181B", "#FAE042", "#ED2939"); break;
                case "IE": VStripes(g, "#169B62", "#FFFFFF", "#FF883E"); break;
                case "RO": VStripes(g, "#002B7F", "#FCD116", "#CE1126"); break;
                case "MD":
                    VStripes(g, "#0046AE", "#FFD200", "#CC092F");
                    Circle(g, "#7C5123", 18, 12, 2.5);
                    break;
                case "MX":
                    VStripes(g, "#006847", "#FFFFFF", "#CE1126");
                    Circle(g, "#7C5123", 18, 12, 2.6);
                    break;
                case "PT":
                    Rect(g, "#006600", 0, 0, 14, 24);
                    Rect(g, "#FF0000", 14, 0, 22, 24);
                    Circle(g, "#FFCC00", 14, 12, 4.2);
                    Circle(g, "#003399", 14, 12, 2.2);
                    break;
                case "FI": NordicCross(g, "#FFFFFF", "#003580"); break;
                case "SE": NordicCross(g, "#006AA7", "#FECC00"); break;
                case "DK": NordicCross(g, "#C60C30", "#FFFFFF"); break;
                case "NO": NordicCross(g, "#BA0C2F", "#FFFFFF", "#00205B"); break;
                case "IS": NordicCross(g, "#02529C", "#FFFFFF", "#DC1E35"); break;
                case "CH":
                    Rect(g, "#D52B1E", 0, 0, 36, 24);
                    Rect(g, "#FFFFFF", 15, 5, 6, 14);
                    Rect(g, "#FFFFFF", 11, 9, 14, 6);
                    break;
                case "JP":
                    Rect(g, "#FFFFFF", 0, 0, 36, 24);
                    Circle(g, "#BC002D", 18, 12, 6.5);
                    break;
                case "TR":
                    Rect(g, "#E30A17", 0, 0, 36, 24);
                    Circle(g, "#FFFFFF", 13.5, 12, 5.5);
                    Circle(g, "#E30A17", 15.2, 12, 4.3);
                    Circle(g, "#FFFFFF", 20.5, 12, 1.8);
                    break;
                case "US":
                    for (int i = 0; i < 7; i++)
                        Rect(g, i % 2 == 0 ? "#B22234" : "#FFFFFF", 0, i * (24.0 / 7.0), 36, (24.0 / 7.0) + 0.15);
                    Rect(g, "#3C3B6E", 0, 0, 16, 13.7);
                    for (int r = 0; r < 3; r++)
                        for (int c = 0; c < 4; c++)
                            Circle(g, "#FFFFFF", 2.4 + c * 3.6, 2.6 + r * 4.1, 0.8);
                    break;
                case "GB":
                    Rect(g, "#012169", 0, 0, 36, 24);
                    Poly(g, "#FFFFFF", (0, 0), (4, 0), (36, 21), (36, 24), (32, 24), (0, 3));
                    Poly(g, "#FFFFFF", (36, 0), (32, 0), (0, 21), (0, 24), (4, 24), (36, 3));
                    Poly(g, "#C8102E", (0, 0), (2.2, 0), (36, 22.5), (36, 24), (33.8, 24), (0, 1.5));
                    Poly(g, "#C8102E", (36, 0), (33.8, 0), (0, 22.5), (0, 24), (2.2, 24), (36, 1.5));
                    Rect(g, "#FFFFFF", 14.5, 0, 7, 24);
                    Rect(g, "#FFFFFF", 0, 8.5, 36, 7);
                    Rect(g, "#C8102E", 16, 0, 4, 24);
                    Rect(g, "#C8102E", 0, 10, 36, 4);
                    break;
                case "CA":
                    Rect(g, "#FF0000", 0, 0, 9, 24);
                    Rect(g, "#FFFFFF", 9, 0, 18, 24);
                    Rect(g, "#FF0000", 27, 0, 9, 24);
                    Poly(g, "#FF0000", (18, 5), (20, 10), (24, 9.5), (21, 13), (22, 16), (18, 14.5), (14, 16), (15, 13), (12, 9.5), (16, 10));
                    Rect(g, "#FF0000", 17.3, 14, 1.4, 5);
                    break;
                case "KZ":
                    Rect(g, "#00AFCA", 0, 0, 36, 24);
                    Rect(g, "#FEC50C", 2, 2, 2.2, 20);
                    Circle(g, "#FEC50C", 19, 11.5, 4.5);
                    break;
                case "GE":
                    Rect(g, "#FFFFFF", 0, 0, 36, 24);
                    Rect(g, "#FF0000", 15.5, 0, 5, 24);
                    Rect(g, "#FF0000", 0, 9.5, 36, 5);
                    foreach (var (cx, cy) in new[] { (7.5, 4.8), (28.5, 4.8), (7.5, 19.2), (28.5, 19.2) })
                    {
                        Rect(g, "#FF0000", cx - 0.8, cy - 2.3, 1.6, 4.6);
                        Rect(g, "#FF0000", cx - 2.3, cy - 0.8, 4.6, 1.6);
                    }
                    break;
                case "AE":
                    Rect(g, "#00732F", 9, 0, 27, 8);
                    Rect(g, "#FFFFFF", 9, 8, 27, 8);
                    Rect(g, "#18181B", 9, 16, 27, 8);
                    Rect(g, "#FF0000", 0, 0, 9.2, 24);
                    break;
                case "CZ":
                    Rect(g, "#FFFFFF", 0, 0, 36, 12);
                    Rect(g, "#D7141A", 0, 12, 36, 12);
                    Poly(g, "#11457E", (0, 0), (17, 12), (0, 24));
                    break;
                case "SK":
                    HStripes(g, "#FFFFFF", "#0B4EA2", "#EE1C25");
                    Rect(g, "#EE1C25", 8, 6.5, 6.5, 10.5);
                    Rect(g, "#FFFFFF", 10.6, 8, 1.3, 7);
                    Rect(g, "#FFFFFF", 9.3, 10, 3.9, 1.2);
                    break;
                case "SI":
                    HStripes(g, "#FFFFFF", "#005DA4", "#ED1C24");
                    Rect(g, "#005DA4", 7, 5, 5, 6);
                    break;
                case "HR":
                    HStripes(g, "#FF0000", "#FFFFFF", "#171796");
                    Rect(g, "#FF0000", 15, 7, 6, 8);
                    break;
                case "RS":
                    HStripes(g, "#C6363C", "#0C4076", "#FFFFFF");
                    Rect(g, "#F1C40F", 9.5, 7.5, 5, 8);
                    break;
                case "GR":
                    for (int i = 0; i < 9; i++)
                        Rect(g, i % 2 == 0 ? "#0D5EAF" : "#FFFFFF", 0, i * (24.0 / 9.0), 36, (24.0 / 9.0) + 0.15);
                    Rect(g, "#0D5EAF", 0, 0, 14, 13.4);
                    Rect(g, "#FFFFFF", 5.5, 0, 3, 13.4);
                    Rect(g, "#FFFFFF", 0, 5.2, 14, 3);
                    break;
                case "SG":
                    HStripes(g, "#EF3340", "#FFFFFF");
                    Circle(g, "#FFFFFF", 8, 6, 3.8);
                    Circle(g, "#EF3340", 9.5, 6, 3.2);
                    break;
                case "HK":
                    Rect(g, "#DE2910", 0, 0, 36, 24);
                    Circle(g, "#FFFFFF", 18, 12, 4.8);
                    Circle(g, "#DE2910", 18, 12, 1.5);
                    break;
                case "KR":
                    Rect(g, "#FFFFFF", 0, 0, 36, 24);
                    Circle(g, "#CD2E3A", 18, 10.5, 5.2);
                    Circle(g, "#0047A0", 18, 13.5, 5.2);
                    Rect(g, "#18181B", 5, 4, 5, 1.6);
                    Rect(g, "#18181B", 26, 4, 5, 1.6);
                    Rect(g, "#18181B", 5, 18.4, 5, 1.6);
                    Rect(g, "#18181B", 26, 18.4, 5, 1.6);
                    break;
                case "TW":
                    Rect(g, "#FE0000", 0, 0, 36, 24);
                    Rect(g, "#000095", 0, 0, 18, 12);
                    Circle(g, "#FFFFFF", 9, 6, 3.2);
                    break;
                case "VN":
                    Rect(g, "#DA251D", 0, 0, 36, 24);
                    Poly(g, "#FFFF00", (18, 6), (19.6, 10.5), (24.3, 10.5), (20.5, 13.2), (22, 17.8), (18, 15), (14, 17.8), (15.5, 13.2), (11.7, 10.5), (16.4, 10.5));
                    break;
                case "TH":
                    Rect(g, "#A51931", 0, 0, 36, 24);
                    Rect(g, "#F4F5F8", 0, 4, 36, 16);
                    Rect(g, "#2D2A4A", 0, 8, 36, 8);
                    break;
                case "MY":
                    for (int i = 0; i < 7; i++)
                        Rect(g, i % 2 == 0 ? "#CC0001" : "#FFFFFF", 0, i * (24.0 / 7.0), 36, (24.0 / 7.0) + 0.15);
                    Rect(g, "#010066", 0, 0, 18, 13.7);
                    Circle(g, "#FFCC00", 7.5, 6.8, 3.5);
                    Circle(g, "#010066", 8.8, 6.8, 2.8);
                    break;
                case "IL":
                    Rect(g, "#FFFFFF", 0, 0, 36, 24);
                    Rect(g, "#0038B8", 0, 2.5, 36, 3.5);
                    Rect(g, "#0038B8", 0, 18, 36, 3.5);
                    Circle(g, "#0038B8", 18, 12, 3.8);
                    Circle(g, "#FFFFFF", 18, 12, 2.4);
                    break;
                case "IN":
                    HStripes(g, "#FF9933", "#FFFFFF", "#138808");
                    Circle(g, "#000080", 18, 12, 2.8);
                    break;
                case "BR":
                    Rect(g, "#009C3B", 0, 0, 36, 24);
                    Poly(g, "#FFDF00", (18, 3), (32, 12), (18, 21), (4, 12));
                    Circle(g, "#002776", 18, 12, 4.5);
                    break;
                case "AR":
                    HStripes(g, "#74ACDF", "#FFFFFF", "#74ACDF");
                    Circle(g, "#F6B40E", 18, 12, 2.8);
                    break;
                case "AU":
                    Rect(g, "#00008B", 0, 0, 36, 24);
                    Rect(g, "#FFFFFF", 6.5, 0, 3, 12);
                    Rect(g, "#FFFFFF", 0, 4.5, 16, 3);
                    Rect(g, "#FF0000", 7.2, 0, 1.6, 12);
                    Rect(g, "#FF0000", 0, 5.2, 16, 1.6);
                    Circle(g, "#FFFFFF", 8, 17.5, 2.2);
                    Circle(g, "#FFFFFF", 27, 7, 1.4);
                    Circle(g, "#FFFFFF", 24, 13, 1.4);
                    Circle(g, "#FFFFFF", 30, 13, 1.4);
                    Circle(g, "#FFFFFF", 27, 18.5, 1.4);
                    break;
                case "CY":
                    Rect(g, "#FFFFFF", 0, 0, 36, 24);
                    Circle(g, "#D57800", 18, 11, 5.0);
                    Rect(g, "#4E5B31", 12, 17, 12, 1.8);
                    break;
                case "EU":
                    Rect(g, "#003399", 0, 0, 36, 24);
                    for (int i = 0; i < 12; i++)
                    {
                        double angle = i * Math.PI * 2.0 / 12.0;
                        Circle(g, "#FFCC00", 18 + Math.Cos(angle) * 7.2, 12 + Math.Sin(angle) * 7.2, 1.15);
                    }
                    break;
                case "FM":
                    Rect(g, "#75B2DD", 0, 0, 36, 24);
                    foreach (var (cx, cy) in new[] { (18.0, 5.2), (18.0, 18.8), (11.2, 12.0), (24.8, 12.0) })
                        Poly(g, "#FFFFFF", (cx, cy - 2.4), (cx + 2.2, cy), (cx, cy + 2.4), (cx - 2.2, cy));
                    break;
                case "ZA":
                    Rect(g, "#E03C31", 0, 0, 36, 12);
                    Rect(g, "#001489", 0, 12, 36, 12);
                    Poly(g, "#FFFFFF", (0, 0), (16, 12), (0, 24));
                    Poly(g, "#007749", (0, 2), (13.5, 12), (0, 22));
                    Rect(g, "#007749", 12, 9.5, 24, 5);
                    Poly(g, "#18181B", (0, 5), (9, 12), (0, 19));
                    break;
                default:
                    return null;
            }

            g.Freeze();
            var img = new DrawingImage(g);
            img.Freeze();
            return img;
        }
    }
}
