using Microsoft.Maui.Graphics;

namespace Hivox.Mobile.Controls;

public class BrandLogo : GraphicsView
{
    public static readonly BindableProperty CornerRadiusProperty =
        BindableProperty.Create(nameof(CornerRadius), typeof(float), typeof(BrandLogo), 10f,
            propertyChanged: (b, _, _) => ((BrandLogo)b).Invalidate());

    public float CornerRadius
    {
        get => (float)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public BrandLogo()
    {
        Drawable = new BrandLogoDrawable(this);
    }

    private sealed class BrandLogoDrawable : IDrawable
    {
        private readonly BrandLogo _owner;
        public BrandLogoDrawable(BrandLogo owner) => _owner = owner;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            float w = dirtyRect.Width;
            float h = dirtyRect.Height;
            if (w <= 2 || h <= 2) return;

            float size = Math.Min(w, h);
            float left = (w - size) / 2f;
            float top = (h - size) / 2f;
            float radius = Math.Min(_owner.CornerRadius, size * 0.45f);
            float borderThickness = Math.Max(1.2f, size * 0.04f);

            var outerRect = new RectF(left, top, size, size);
            var innerRect = new RectF(
                left + borderThickness,
                top + borderThickness,
                size - borderThickness * 2f,
                size - borderThickness * 2f);
            float innerRadius = Math.Max(1f, radius - borderThickness * 0.6f);

            // Gradient border matching desktop BrandLogo.xaml (#9D78FF -> #7C4DFF -> #4B2B85)
            var borderGrad = new LinearGradientPaint
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                [
                    new PaintGradientStop(0f, Color.FromArgb("#9D78FF")),
                    new PaintGradientStop(0.6f, Color.FromArgb("#7C4DFF")),
                    new PaintGradientStop(1f, Color.FromArgb("#4B2B85"))
                ]
            };
            canvas.SetFillPaint(borderGrad, outerRect);
            canvas.FillRoundedRectangle(outerRect, radius);

            // Background gradient matching desktop BrandLogo.xaml (#2A1B3D -> #140D22)
            var bgGrad = new LinearGradientPaint
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                [
                    new PaintGradientStop(0f, Color.FromArgb("#2A1B3D")),
                    new PaintGradientStop(1f, Color.FromArgb("#140D22"))
                ]
            };
            canvas.SetFillPaint(bgGrad, innerRect);
            canvas.FillRoundedRectangle(innerRect, innerRadius);

            // Inner 100x100 vector space (matching Viewbox Margin=4 in ~32dp desktop container)
            float pad = size * 0.125f;
            float inner = size - pad * 2f;
            float sx = inner / 100f;
            float ox = left + pad;
            float oy = top + pad;

            float X(float v) => ox + v * sx;
            float Y(float v) => oy + v * sx;
            float halfStroke = 7f * sx; // StrokeThickness="14" in 100x100 space

            // 1. Left vertical node pillar: M26,22 L26,78 with #EADDFF -> #7C4DFF vertical gradient
            var leftRect = new RectF(X(19f), Y(15f), 14f * sx, 70f * sx);
            var leftGrad = new LinearGradientPaint
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops =
                [
                    new PaintGradientStop(0f, Color.FromArgb("#EADDFF")),
                    new PaintGradientStop(1f, Color.FromArgb("#7C4DFF"))
                ]
            };
            canvas.SetFillPaint(leftGrad, leftRect);
            canvas.FillRoundedRectangle(leftRect, halfStroke);

            // 2. Right vertical node pillar: M74,22 L74,78 with #B388FF -> #FF80E1 vertical gradient
            var rightRect = new RectF(X(67f), Y(15f), 14f * sx, 70f * sx);
            var rightGrad = new LinearGradientPaint
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops =
                [
                    new PaintGradientStop(0f, Color.FromArgb("#B388FF")),
                    new PaintGradientStop(1f, Color.FromArgb("#FF80E1"))
                ]
            };
            canvas.SetFillPaint(rightGrad, rightRect);
            canvas.FillRoundedRectangle(rightRect, halfStroke);

            // 3. Intertwined tunnel flow wave: M26,64 C26,42 74,58 74,36 with #7C4DFF -> #B388FF -> #FF80E1
            var waveRibbon = BuildThickCubicBezierRibbon(
                new PointF(X(26f), Y(64f)),
                new PointF(X(26f), Y(42f)),
                new PointF(X(74f), Y(58f)),
                new PointF(X(74f), Y(36f)),
                halfStroke);
            var waveBounds = new RectF(X(19f), Y(29f), 62f * sx, 42f * sx);
            var waveGrad = new LinearGradientPaint
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops =
                [
                    new PaintGradientStop(0f, Color.FromArgb("#7C4DFF")),
                    new PaintGradientStop(0.5f, Color.FromArgb("#B388FF")),
                    new PaintGradientStop(1f, Color.FromArgb("#FF80E1"))
                ]
            };
            canvas.SetFillPaint(waveGrad, waveBounds);
            canvas.FillPath(waveRibbon);

            // 4. Connection dots: (50, 28.5) #EADDFF and (50, 71.5) #B388FF, r = 5.5
            float dotR = 5.5f * sx;
            canvas.FillColor = Color.FromArgb("#EADDFF");
            canvas.FillCircle(X(50f), Y(28.5f), dotR);
            canvas.FillColor = Color.FromArgb("#B388FF");
            canvas.FillCircle(X(50f), Y(71.5f), dotR);
        }

        private static PathF BuildThickCubicBezierRibbon(
            PointF p0, PointF p1, PointF p2, PointF p3, float radius)
        {
            const int steps = 32;
            const int capSteps = 10;

            var upper = new PointF[steps + 1];
            var lower = new PointF[steps + 1];
            var tangents = new PointF[steps + 1];

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float u = 1f - t;

                float px = u * u * u * p0.X + 3f * u * u * t * p1.X + 3f * u * t * t * p2.X + t * t * t * p3.X;
                float py = u * u * u * p0.Y + 3f * u * u * t * p1.Y + 3f * u * t * t * p2.Y + t * t * t * p3.Y;

                float dx = 3f * u * u * (p1.X - p0.X) + 6f * u * t * (p2.X - p1.X) + 3f * t * t * (p3.X - p2.X);
                float dy = 3f * u * u * (p1.Y - p0.Y) + 6f * u * t * (p2.Y - p1.Y) + 3f * t * t * (p3.Y - p2.Y);
                float len = MathF.Sqrt(dx * dx + dy * dy);
                if (len < 1e-5f) { dx = 0f; dy = -1f; len = 1f; }

                float tx = dx / len;
                float ty = dy / len;
                float nx = -ty;
                float ny = tx;

                tangents[i] = new PointF(tx, ty);
                upper[i] = new PointF(px + nx * radius, py + ny * radius);
                lower[i] = new PointF(px - nx * radius, py - ny * radius);
            }

            var path = new PathF();
            path.MoveTo(upper[0]);
            for (int i = 1; i <= steps; i++)
                path.LineTo(upper[i]);

            // Round cap around p3 from upper[steps] (+normal) to lower[steps] (-normal)
            float endAngleStart = MathF.Atan2(upper[steps].Y - p3.Y, upper[steps].X - p3.X);
            float endAngleEnd = endAngleStart - MathF.PI;
            float crossEnd = tangents[steps].X * (upper[steps].Y - p3.Y) - tangents[steps].Y * (upper[steps].X - p3.X);
            float sweepEnd = crossEnd < 0 ? MathF.PI : -MathF.PI;
            for (int c = 1; c < capSteps; c++)
            {
                float a = endAngleStart + sweepEnd * (c / (float)capSteps);
                path.LineTo(p3.X + MathF.Cos(a) * radius, p3.Y + MathF.Sin(a) * radius);
            }

            for (int i = steps; i >= 0; i--)
                path.LineTo(lower[i]);

            // Round cap around p0 from lower[0] to upper[0]
            float startAngleBegin = MathF.Atan2(lower[0].Y - p0.Y, lower[0].X - p0.X);
            float crossStart = (-tangents[0].X) * (lower[0].Y - p0.Y) - (-tangents[0].Y) * (lower[0].X - p0.X);
            float sweepStart = crossStart < 0 ? MathF.PI : -MathF.PI;
            for (int c = 1; c < capSteps; c++)
            {
                float a = startAngleBegin + sweepStart * (c / (float)capSteps);
                path.LineTo(p0.X + MathF.Cos(a) * radius, p0.Y + MathF.Sin(a) * radius);
            }

            path.Close();
            return path;
        }
    }
}
