using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Hivox.Controls
{
    public partial class SpeedGraph : UserControl
    {
        private readonly Queue<(double Upload, double Download)> _speedHistory = new();
        private const int MaxHistory = 60;

        public SpeedGraph()
        {
            InitializeComponent();
            SizeChanged += (s, e) => DrawGraph();
        }

        public void AddDataPoint(double uploadSpeed, double downloadSpeed)
        {
            _speedHistory.Enqueue((uploadSpeed, downloadSpeed));
            while (_speedHistory.Count > MaxHistory)
                _speedHistory.Dequeue();
            DrawGraph();
        }

        public void Clear()
        {
            _speedHistory.Clear();
            GraphCanvas.Children.Clear();
        }

        private Color ResolveThemeColor(string resourceKey, Color fallback)
        {
            if (TryFindResource(resourceKey) is SolidColorBrush b)
                return b.Color;
            return fallback;
        }

        private void DrawGraph()
        {
            GraphCanvas.Children.Clear();
            double width = GraphCanvas.ActualWidth;
            double height = GraphCanvas.ActualHeight;
            if (width < 10 || height < 10 || _speedHistory.Count < 2) return;

            var gridColor = ResolveThemeColor("M3OutlineVariantBrush", Color.FromRgb(0x3A, 0x28, 0x56));
            var primaryColor = ResolveThemeColor("M3PrimaryBrush", Color.FromRgb(0x7C, 0x4D, 0xFF));
            var secondaryColor = ResolveThemeColor("M3SecondaryBrush", Color.FromRgb(0xB3, 0x88, 0xFF));
            var tertiaryColor = ResolveThemeColor("M3TertiaryBrush", Color.FromRgb(0xFF, 0x80, 0xE1));

            for (int row = 1; row <= 2; row++)
            {
                double y = height * row / 3.0;
                GraphCanvas.Children.Add(new Line
                {
                    X1 = 0, Y1 = y, X2 = width, Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromArgb(0x45, gridColor.R, gridColor.G, gridColor.B)),
                    StrokeThickness = 0.8,
                    StrokeDashArray = new DoubleCollection { 3, 4 }
                });
            }

            double maxSpeed = Math.Max(1.0, _speedHistory.Max(s => Math.Max(s.Upload, s.Download)) * 1.18);

            DrawSmoothWave(0, maxSpeed, height, width, secondaryColor, tertiaryColor, isPrimary: false);
            DrawSmoothWave(1, maxSpeed, height, width, primaryColor, tertiaryColor, isPrimary: true);
        }

        private void DrawSmoothWave(int index, double maxSpeed, double height, double width, Color cStart, Color cEnd, bool isPrimary)
        {
            var pts = new List<Point>(_speedHistory.Count);
            double xStep = width / Math.Max(_speedHistory.Count - 1, 1);
            int i = 0;
            foreach (var speeds in _speedHistory)
            {
                double x = i * xStep;
                double value = index == 0 ? speeds.Upload : speeds.Download;
                double y = height - (value / maxSpeed * (height - 10)) - 5;
                pts.Add(new Point(x, Math.Clamp(y, 3, height - 3)));
                i++;
            }
            if (pts.Count < 2) return;

            // Smooth cubic Bezier path for organic Material 3 wave motion
            var strokeGeo = new StreamGeometry();
            using (var ctx = strokeGeo.Open())
            {
                ctx.BeginFigure(pts[0], false, false);
                for (int k = 0; k < pts.Count - 1; k++)
                {
                    double midX = (pts[k].X + pts[k + 1].X) / 2.0;
                    ctx.BezierTo(new Point(midX, pts[k].Y), new Point(midX, pts[k + 1].Y), pts[k + 1], true, false);
                }
            }
            strokeGeo.Freeze();

            if (isPrimary)
            {
                var fillGeo = new StreamGeometry();
                using (var ctx = fillGeo.Open())
                {
                    ctx.BeginFigure(new Point(pts[0].X, height), true, true);
                    ctx.LineTo(pts[0], false, false);
                    for (int k = 0; k < pts.Count - 1; k++)
                    {
                        double midX = (pts[k].X + pts[k + 1].X) / 2.0;
                        ctx.BezierTo(new Point(midX, pts[k].Y), new Point(midX, pts[k + 1].Y), pts[k + 1], true, false);
                    }
                    ctx.LineTo(new Point(pts[^1].X, height), false, false);
                }
                fillGeo.Freeze();

                GraphCanvas.Children.Add(new Path
                {
                    Data = fillGeo,
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(0, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(0x4A, cStart.R, cStart.G, cStart.B), 0),
                            new GradientStop(Color.FromArgb(0x18, cEnd.R, cEnd.G, cEnd.B), 0.5),
                            new GradientStop(Color.FromArgb(0x00, cStart.R, cStart.G, cStart.B), 1)
                        }
                    }
                });
            }

            GraphCanvas.Children.Add(new Path
            {
                Data = strokeGeo,
                Stroke = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0.5),
                    EndPoint = new Point(1, 0.5),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(isPrimary ? cStart : Color.FromArgb(0x99, cStart.R, cStart.G, cStart.B), 0),
                        new GradientStop(isPrimary ? cEnd : Color.FromArgb(0x88, cEnd.R, cEnd.G, cEnd.B), 1)
                    }
                },
                StrokeThickness = isPrimary ? 2.4 : 1.3,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });
        }
    }
}
