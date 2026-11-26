using Microsoft.Maui.Graphics;

namespace Hivox.Mobile.Controls;

public class SpeedGraph : GraphicsView
{
    private readonly Queue<(double Upload, double Download)> _speedHistory = new();
    private const int MaxHistory = 45;

    public SpeedGraph()
    {
        Drawable = new SpeedGraphDrawable(this);
    }

    public void AddDataPoint(double uploadSpeedKbps, double downloadSpeedKbps)
    {
        _speedHistory.Enqueue((uploadSpeedKbps, downloadSpeedKbps));
        while (_speedHistory.Count > MaxHistory)
            _speedHistory.Dequeue();
        Invalidate();
    }

    public void Clear()
    {
        _speedHistory.Clear();
        Invalidate();
    }

    private static Color ResolveColor(string key, Color fallback)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var obj) == true && obj is Color c)
            return c;
        return fallback;
    }

    private sealed class SpeedGraphDrawable : IDrawable
    {
        private readonly SpeedGraph _owner;
        public SpeedGraphDrawable(SpeedGraph owner) => _owner = owner;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            float width = dirtyRect.Width;
            float height = dirtyRect.Height;
            if (width < 10 || height < 10) return;

            var gridColor = ResolveColor("M3OutlineVariant", Color.FromArgb("#3A2856"));
            var primaryColor = ResolveColor("M3Primary", Color.FromArgb("#7C4DFF"));
            var secondaryColor = ResolveColor("M3Secondary", Color.FromArgb("#B388FF"));
            var tertiaryColor = ResolveColor("M3Tertiary", Color.FromArgb("#FF80E1"));

            // Subtle dashed grid
            canvas.StrokeColor = gridColor.WithAlpha(0.35f);
            canvas.StrokeSize = 1f;
            canvas.StrokeDashPattern = [3f, 4f];
            for (int row = 1; row <= 2; row++)
            {
                float y = height * row / 3f;
                canvas.DrawLine(0, y, width, y);
            }
            canvas.StrokeDashPattern = null;

            if (_owner._speedHistory.Count < 2)
            {
                // Baseline wave when idle at 0 B/s
                canvas.StrokeColor = primaryColor.WithAlpha(0.35f);
                canvas.StrokeSize = 1.6f;
                canvas.DrawLine(0, height - 4, width, height - 4);
                return;
            }

            var items = _owner._speedHistory.ToArray();
            double maxSpeed = Math.Max(1.0, items.Max(s => Math.Max(s.Upload, s.Download)) * 1.18);

            DrawWave(canvas, items, isDownload: false, maxSpeed, width, height, secondaryColor.WithAlpha(0.75f), tertiaryColor);
            DrawWave(canvas, items, isDownload: true, maxSpeed, width, height, primaryColor, tertiaryColor);
        }

        private static void DrawWave(
            ICanvas canvas,
            (double Upload, double Download)[] items,
            bool isDownload,
            double maxSpeed,
            float width,
            float height,
            Color cStart,
            Color cEnd)
        {
            var pts = new PointF[items.Length];
            float xStep = width / Math.Max(items.Length - 1, 1);

            for (int i = 0; i < items.Length; i++)
            {
                float x = i * xStep;
                double val = isDownload ? items[i].Download : items[i].Upload;
                float y = height - (float)(val / maxSpeed * (height - 10)) - 4f;
                pts[i] = new PointF(x, Math.Clamp(y, 3f, height - 3f));
            }

            var strokePath = new PathF();
            strokePath.MoveTo(pts[0]);
            for (int k = 0; k < pts.Length - 1; k++)
            {
                float midX = (pts[k].X + pts[k + 1].X) / 2f;
                strokePath.CurveTo(midX, pts[k].Y, midX, pts[k + 1].Y, pts[k + 1].X, pts[k + 1].Y);
            }

            if (isDownload)
            {
                var fillPath = new PathF();
                fillPath.MoveTo(pts[0].X, height);
                fillPath.LineTo(pts[0]);
                for (int k = 0; k < pts.Length - 1; k++)
                {
                    float midX = (pts[k].X + pts[k + 1].X) / 2f;
                    fillPath.CurveTo(midX, pts[k].Y, midX, pts[k + 1].Y, pts[k + 1].X, pts[k + 1].Y);
                }
                fillPath.LineTo(pts[^1].X, height);
                fillPath.Close();

                var fillPaint = new LinearGradientPaint
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1),
                    GradientStops =
                    [
                        new PaintGradientStop(0f, cStart.WithAlpha(0.32f)),
                        new PaintGradientStop(0.6f, cEnd.WithAlpha(0.10f)),
                        new PaintGradientStop(1f, cStart.WithAlpha(0.0f))
                    ]
                };
                canvas.SetFillPaint(fillPaint, new RectF(0, 0, width, height));
                canvas.FillPath(fillPath);
            }

            canvas.StrokeColor = cStart;
            canvas.StrokeSize = isDownload ? 2.4f : 1.3f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.DrawPath(strokePath);
        }
    }
}
