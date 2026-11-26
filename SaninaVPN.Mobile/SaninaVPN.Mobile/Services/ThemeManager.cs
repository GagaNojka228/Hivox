using Hivox.Mobile.Models;

namespace Hivox.Mobile.Services;

public static class ThemeManager
{
    public static bool IsEffectiveLightTheme(ThemeMode theme)
    {
        if (theme == ThemeMode.Light) return true;
        if (theme != ThemeMode.MaterialYou) return false;
#if ANDROID
        try
        {
            var ctx = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity ?? global::Android.App.Application.Context;
            var uiMode = ctx.Resources?.Configuration?.UiMode ?? global::Android.Content.Res.UiMode.NightUndefined;
            if ((uiMode & global::Android.Content.Res.UiMode.NightMask) == global::Android.Content.Res.UiMode.NightYes)
                return false;
            if ((uiMode & global::Android.Content.Res.UiMode.NightMask) == global::Android.Content.Res.UiMode.NightNo)
                return true;
        }
        catch { }
#endif
        return Application.Current?.RequestedTheme == AppTheme.Light;
    }

    public static void ApplyThemeResources(ThemeMode theme)
    {
        var app = Application.Current;
        if (app == null) return;

        app.UserAppTheme = theme switch
        {
            ThemeMode.Light => AppTheme.Light,
            ThemeMode.MaterialYou => AppTheme.Unspecified,
            _ => AppTheme.Dark
        };
        var res = app.Resources;

        static void SetColorAndBrush(ResourceDictionary dict, string keyPrefix, string hex)
        {
            var c = Color.FromArgb(hex);
            dict[keyPrefix] = c;
            dict[keyPrefix + "Brush"] = new SolidColorBrush(c);
        }

        Color tabBg, tabActive, tabInactive, statusBar;
        bool isEffectiveLight = IsEffectiveLightTheme(theme);

        if (theme == ThemeMode.MaterialYou && TryApplyMaterialYou(res, isEffectiveLight, out tabBg, out tabActive, out tabInactive, out statusBar))
        {
            // Applied dynamic Android 12+ Material You palette
        }
        else if (theme == ThemeMode.Dark || (theme == ThemeMode.MaterialYou && !isEffectiveLight))
        {
            SetColorAndBrush(res, "M3Primary", "#7C4DFF");
            SetColorAndBrush(res, "M3OnPrimary", "#FFFFFF");
            SetColorAndBrush(res, "M3PrimaryContainer", "#2A1B3D");
            SetColorAndBrush(res, "M3OnPrimaryContainer", "#EADDFF");
            SetColorAndBrush(res, "M3Secondary", "#B388FF");
            SetColorAndBrush(res, "M3OnSecondary", "#1A102A");
            SetColorAndBrush(res, "M3SecondaryContainer", "#33204C");
            SetColorAndBrush(res, "M3OnSecondaryContainer", "#EADDFF");
            SetColorAndBrush(res, "M3Tertiary", "#FF80E1");
            SetColorAndBrush(res, "M3SurfaceDim", "#0D0817");
            SetColorAndBrush(res, "M3Surface", "#140D22");
            SetColorAndBrush(res, "M3SurfaceContainerLow", "#1A102A");
            SetColorAndBrush(res, "M3SurfaceContainer", "#201533");
            SetColorAndBrush(res, "M3SurfaceContainerHigh", "#2A1B3D");
            SetColorAndBrush(res, "M3SurfaceContainerHighest", "#35234E");
            SetColorAndBrush(res, "M3OnSurface", "#E6E1F5");
            SetColorAndBrush(res, "M3OnSurfaceVariant", "#B9B0D8");
            SetColorAndBrush(res, "M3Outline", "#8A7FA8");
            SetColorAndBrush(res, "M3OutlineVariant", "#3A2856");
            SetColorAndBrush(res, "M3Green", "#4ADE80");
            SetColorAndBrush(res, "M3OnGreen", "#062112");
            SetColorAndBrush(res, "M3GreenContainer", "#123426");
            SetColorAndBrush(res, "M3Amber", "#FBBF24");
            SetColorAndBrush(res, "M3Cyan", "#22D3EE");
            SetColorAndBrush(res, "M3Orange", "#FB923C");
            SetColorAndBrush(res, "M3Red", "#F87171");
            SetColorAndBrush(res, "M3RedContainer", "#3D1724");
            res["M3SwitchThumb"] = Color.FromArgb("#E6E1F5");
            tabBg = Color.FromArgb("#1A102A");
            tabActive = Color.FromArgb("#B388FF");
            tabInactive = Color.FromArgb("#8A7FA8");
            statusBar = Color.FromArgb("#0D0817");
        }
        else if (theme == ThemeMode.Black)
        {
            SetColorAndBrush(res, "M3Primary", "#7C4DFF");
            SetColorAndBrush(res, "M3OnPrimary", "#FFFFFF");
            SetColorAndBrush(res, "M3PrimaryContainer", "#1D1330");
            SetColorAndBrush(res, "M3OnPrimaryContainer", "#EADDFF");
            SetColorAndBrush(res, "M3Secondary", "#B388FF");
            SetColorAndBrush(res, "M3OnSecondary", "#000000");
            SetColorAndBrush(res, "M3SecondaryContainer", "#1C152B");
            SetColorAndBrush(res, "M3OnSecondaryContainer", "#EADDFF");
            SetColorAndBrush(res, "M3Tertiary", "#FF80E1");
            SetColorAndBrush(res, "M3SurfaceDim", "#000000");
            SetColorAndBrush(res, "M3Surface", "#050507");
            SetColorAndBrush(res, "M3SurfaceContainerLow", "#0A0A0E");
            SetColorAndBrush(res, "M3SurfaceContainer", "#0F0F14");
            SetColorAndBrush(res, "M3SurfaceContainerHigh", "#17171F");
            SetColorAndBrush(res, "M3SurfaceContainerHighest", "#21212D");
            SetColorAndBrush(res, "M3OnSurface", "#F1EEFA");
            SetColorAndBrush(res, "M3OnSurfaceVariant", "#A8A2C0");
            SetColorAndBrush(res, "M3Outline", "#78728E");
            SetColorAndBrush(res, "M3OutlineVariant", "#23232F");
            SetColorAndBrush(res, "M3Green", "#4ADE80");
            SetColorAndBrush(res, "M3OnGreen", "#062112");
            SetColorAndBrush(res, "M3GreenContainer", "#0C291C");
            SetColorAndBrush(res, "M3Amber", "#FBBF24");
            SetColorAndBrush(res, "M3Cyan", "#22D3EE");
            SetColorAndBrush(res, "M3Orange", "#FB923C");
            SetColorAndBrush(res, "M3Red", "#F87171");
            SetColorAndBrush(res, "M3RedContainer", "#2D1018");
            res["M3SwitchThumb"] = Color.FromArgb("#F1EEFA");
            tabBg = Color.FromArgb("#0A0A0E");
            tabActive = Color.FromArgb("#B388FF");
            tabInactive = Color.FromArgb("#78728E");
            statusBar = Color.FromArgb("#000000");
        }
        else
        {
            SetColorAndBrush(res, "M3Primary", "#6750A4");
            SetColorAndBrush(res, "M3OnPrimary", "#FFFFFF");
            SetColorAndBrush(res, "M3PrimaryContainer", "#EADDFF");
            SetColorAndBrush(res, "M3OnPrimaryContainer", "#21005D");
            SetColorAndBrush(res, "M3Secondary", "#625B71");
            SetColorAndBrush(res, "M3OnSecondary", "#FFFFFF");
            SetColorAndBrush(res, "M3SecondaryContainer", "#E8DEF8");
            SetColorAndBrush(res, "M3OnSecondaryContainer", "#1D192B");
            SetColorAndBrush(res, "M3Tertiary", "#7D5260");
            SetColorAndBrush(res, "M3SurfaceDim", "#F2ECF9");
            SetColorAndBrush(res, "M3Surface", "#FAF7FF");
            SetColorAndBrush(res, "M3SurfaceContainerLow", "#F5EFFB");
            SetColorAndBrush(res, "M3SurfaceContainer", "#FFFFFF");
            SetColorAndBrush(res, "M3SurfaceContainerHigh", "#ECE6F4");
            SetColorAndBrush(res, "M3SurfaceContainerHighest", "#E2DAEE");
            SetColorAndBrush(res, "M3OnSurface", "#1D1B20");
            SetColorAndBrush(res, "M3OnSurfaceVariant", "#49454F");
            SetColorAndBrush(res, "M3Outline", "#79747E");
            SetColorAndBrush(res, "M3OutlineVariant", "#D5CEE0");
            SetColorAndBrush(res, "M3Green", "#15803D");
            SetColorAndBrush(res, "M3OnGreen", "#FFFFFF");
            SetColorAndBrush(res, "M3GreenContainer", "#DCFCE7");
            SetColorAndBrush(res, "M3Amber", "#B45309");
            SetColorAndBrush(res, "M3Cyan", "#0E7490");
            SetColorAndBrush(res, "M3Orange", "#C2410C");
            SetColorAndBrush(res, "M3Red", "#DC2626");
            SetColorAndBrush(res, "M3RedContainer", "#FEE2E2");
            res["M3SwitchThumb"] = Color.FromArgb("#FFFFFF");
            tabBg = Color.FromArgb("#FFFFFF");
            tabActive = Color.FromArgb("#6750A4");
            tabInactive = Color.FromArgb("#79747E");
            statusBar = Color.FromArgb("#F2ECF9");
        }

        res["M3TabBarBg"] = tabBg;
        res["M3TabActive"] = tabActive;
        res["M3TabInactive"] = tabInactive;

        if (Shell.Current != null)
        {
            Shell.SetBackgroundColor(Shell.Current, statusBar);
            Shell.SetTabBarBackgroundColor(Shell.Current, tabBg);
            Shell.SetTabBarForegroundColor(Shell.Current, tabActive);
            Shell.SetTabBarTitleColor(Shell.Current, tabActive);
            Shell.SetTabBarUnselectedColor(Shell.Current, tabInactive);
        }

        SyncAndroidSystemBars(statusBar, isEffectiveLight);
    }

    private static bool TryApplyMaterialYou(
        ResourceDictionary res,
        bool isLight,
        out Color tabBg,
        out Color tabActive,
        out Color tabInactive,
        out Color statusBar)
    {
        tabBg = tabActive = tabInactive = statusBar = Colors.Black;
#if ANDROID
        try
        {
            var ctx = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity ?? global::Android.App.Application.Context;

            static Color FromAndroidColorInt(int argb)
                => Color.FromRgba((argb >> 16) & 0xFF, (argb >> 8) & 0xFF, argb & 0xFF, 255);

            static (float H, float S, float L) ColorToHsl(Color c)
            {
                float r = c.Red, g = c.Green, b = c.Blue;
                float max = MathF.Max(r, MathF.Max(g, b));
                float min = MathF.Min(r, MathF.Min(g, b));
                float l = (max + min) * 0.5f;
                if (MathF.Abs(max - min) < 0.0001f)
                    return (0f, 0f, l);

                float d = max - min;
                float s = l > 0.5f ? d / (2f - max - min) : d / (max + min);
                float h;
                if (max == r) h = ((g - b) / d) + (g < b ? 6f : 0f);
                else if (max == g) h = ((b - r) / d) + 2f;
                else h = ((r - g) / d) + 4f;
                h *= 60f;
                return (h, s, l);
            }

            static Color HslToColor(float h, float s, float l)
            {
                h = ((h % 360f) + 360f) % 360f;
                s = Math.Clamp(s, 0f, 1f);
                l = Math.Clamp(l, 0f, 1f);

                if (s < 0.0001f)
                    return new Color(l, l, l, 1f);

                float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
                float p = 2f * l - q;
                static float Hue2Rgb(float p, float q, float t)
                {
                    if (t < 0f) t += 1f;
                    if (t > 1f) t -= 1f;
                    if (t < 1f / 6f) return p + (q - p) * 6f * t;
                    if (t < 0.5f) return q;
                    if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
                    return p;
                }
                float hk = h / 360f;
                return new Color(
                    Hue2Rgb(p, q, hk + 1f / 3f),
                    Hue2Rgb(p, q, hk),
                    Hue2Rgb(p, q, hk - 1f / 3f),
                    1f);
            }

            void SetColor(string prefix, Color c)
            {
                res[prefix] = c;
                res[prefix + "Brush"] = new SolidColorBrush(c);
            }

            Color seed = Color.FromArgb("#7C4DFF");
            string seedSource = "fallback";

            // 1. Android 12+ (API 31+) SystemAccent1500 / 1400 (full-chroma Monet accent)
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                try
                {
                    var c500 = FromAndroidColorInt(AndroidX.Core.Content.ContextCompat.GetColor(ctx, global::Android.Resource.Color.SystemAccent1500));
                    var c400 = FromAndroidColorInt(AndroidX.Core.Content.ContextCompat.GetColor(ctx, global::Android.Resource.Color.SystemAccent1400));
                    var (_, s500, _) = ColorToHsl(c500);
                    var (_, s400, _) = ColorToHsl(c400);
                    if (s500 >= 0.12f)
                    {
                        seed = c500;
                        seedSource = "SystemAccent1500";
                    }
                    else if (s400 >= 0.12f)
                    {
                        seed = c400;
                        seedSource = "SystemAccent1400";
                    }
                }
                catch { }
            }

            // 2. Fallback to WallpaperColors (works on Xiaomi HyperOS/MIUI, Samsung, and Android 8.1+)
            if (seedSource == "fallback" && OperatingSystem.IsAndroidVersionAtLeast(27))
            {
                try
                {
                    var wm = global::Android.App.WallpaperManager.GetInstance(ctx);
                    var wc = wm?.GetWallpaperColors((int)global::Android.App.WallpaperManagerFlags.System);
                    if (wc != null)
                    {
                        var candidates = new List<Color>();
                        if (wc.PrimaryColor != null) candidates.Add(FromAndroidColorInt(wc.PrimaryColor.ToArgb()));
                        if (wc.SecondaryColor != null) candidates.Add(FromAndroidColorInt(wc.SecondaryColor.ToArgb()));
                        if (wc.TertiaryColor != null) candidates.Add(FromAndroidColorInt(wc.TertiaryColor.ToArgb()));

                        foreach (var cand in candidates)
                        {
                            var (_, cs, _) = ColorToHsl(cand);
                            if (cs >= 0.12f)
                            {
                                seed = cand;
                                seedSource = "WallpaperColors";
                                break;
                            }
                        }
                        if (seedSource == "fallback" && candidates.Count > 0)
                        {
                            seed = candidates[0];
                            seedSource = "WallpaperColorsLowChroma";
                        }
                    }
                }
                catch { }
            }

            var (h, rawS, _) = ColorToHsl(seed);
            float accentS = Math.Clamp(rawS < 0.12f ? 0.55f : rawS, 0.45f, 0.85f);
            float surfS = Math.Clamp(accentS * 0.40f, 0.18f, 0.34f);

            if (isLight)
            {
                var primary = HslToColor(h, accentS, 0.44f);
                var surfaceDim = HslToColor(h, 0.24f, 0.93f);
                var outline = HslToColor(h, 0.12f, 0.48f);

                SetColor("M3Primary", primary);
                SetColor("M3OnPrimary", Colors.White);
                SetColor("M3PrimaryContainer", HslToColor(h, accentS * 0.75f, 0.89f));
                SetColor("M3OnPrimaryContainer", HslToColor(h, accentS * 0.85f, 0.16f));
                SetColor("M3Secondary", HslToColor(h, accentS * 0.65f, 0.42f));
                SetColor("M3OnSecondary", Colors.White);
                SetColor("M3SecondaryContainer", HslToColor(h, accentS * 0.65f, 0.91f));
                SetColor("M3OnSecondaryContainer", HslToColor(h, accentS * 0.75f, 0.18f));
                SetColor("M3Tertiary", HslToColor((h + 35f) % 360f, accentS * 0.70f, 0.44f));
                SetColor("M3SurfaceDim", surfaceDim);
                SetColor("M3Surface", HslToColor(h, 0.22f, 0.97f));
                SetColor("M3SurfaceContainerLow", HslToColor(h, 0.22f, 0.95f));
                SetColor("M3SurfaceContainer", Colors.White);
                SetColor("M3SurfaceContainerHigh", HslToColor(h, 0.24f, 0.91f));
                SetColor("M3SurfaceContainerHighest", HslToColor(h, 0.24f, 0.87f));
                SetColor("M3OnSurface", HslToColor(h, 0.18f, 0.12f));
                SetColor("M3OnSurfaceVariant", HslToColor(h, 0.14f, 0.30f));
                SetColor("M3Outline", outline);
                SetColor("M3OutlineVariant", HslToColor(h, 0.22f, 0.82f));

                res["M3Green"] = Color.FromArgb("#15803D");
                res["M3GreenBrush"] = new SolidColorBrush(Color.FromArgb("#15803D"));
                res["M3OnGreen"] = Color.FromArgb("#FFFFFF");
                res["M3GreenContainer"] = Color.FromArgb("#DCFCE7");
                res["M3GreenContainerBrush"] = new SolidColorBrush(Color.FromArgb("#DCFCE7"));
                res["M3Amber"] = Color.FromArgb("#B45309");
                res["M3AmberBrush"] = new SolidColorBrush(Color.FromArgb("#B45309"));
                res["M3Cyan"] = Color.FromArgb("#0E7490");
                res["M3CyanBrush"] = new SolidColorBrush(Color.FromArgb("#0E7490"));
                res["M3Orange"] = Color.FromArgb("#C2410C");
                res["M3OrangeBrush"] = new SolidColorBrush(Color.FromArgb("#C2410C"));
                res["M3Red"] = Color.FromArgb("#DC2626");
                res["M3RedBrush"] = new SolidColorBrush(Color.FromArgb("#DC2626"));
                res["M3RedContainer"] = Color.FromArgb("#FEE2E2");
                res["M3RedContainerBrush"] = new SolidColorBrush(Color.FromArgb("#FEE2E2"));
                res["M3SwitchThumb"] = Colors.White;

                tabBg = Colors.White;
                tabActive = primary;
                tabInactive = outline;
                statusBar = surfaceDim;
                Logger.Log($"Material You (Light, {seedSource}) seed={seed.ToHex()} primary={primary.ToHex()} surface={surfaceDim.ToHex()}");
            }
            else
            {
                var primary = HslToColor(h, accentS, 0.64f);
                var secondary = HslToColor(h, accentS * 0.85f, 0.76f);
                var surfaceDim = HslToColor(h, surfS, 0.08f);
                var surfaceLow = HslToColor(h, surfS, 0.13f);
                var onSurface = HslToColor(h, 0.16f, 0.93f);
                var outline = HslToColor(h, 0.20f, 0.56f);

                SetColor("M3Primary", primary);
                SetColor("M3OnPrimary", HslToColor(h, 0.35f, 0.12f));
                SetColor("M3PrimaryContainer", HslToColor(h, accentS * 0.65f, 0.22f));
                SetColor("M3OnPrimaryContainer", HslToColor(h, accentS * 0.50f, 0.90f));
                SetColor("M3Secondary", secondary);
                SetColor("M3OnSecondary", HslToColor(h, 0.35f, 0.14f));
                SetColor("M3SecondaryContainer", HslToColor(h, accentS * 0.55f, 0.26f));
                SetColor("M3OnSecondaryContainer", HslToColor(h, accentS * 0.50f, 0.90f));
                SetColor("M3Tertiary", HslToColor((h + 35f) % 360f, accentS * 0.85f, 0.74f));
                SetColor("M3SurfaceDim", surfaceDim);
                SetColor("M3Surface", HslToColor(h, surfS, 0.11f));
                SetColor("M3SurfaceContainerLow", surfaceLow);
                SetColor("M3SurfaceContainer", HslToColor(h, surfS + 0.02f, 0.16f));
                SetColor("M3SurfaceContainerHigh", HslToColor(h, surfS + 0.02f, 0.21f));
                SetColor("M3SurfaceContainerHighest", HslToColor(h, surfS + 0.02f, 0.26f));
                SetColor("M3OnSurface", onSurface);
                SetColor("M3OnSurfaceVariant", HslToColor(h, 0.18f, 0.75f));
                SetColor("M3Outline", outline);
                SetColor("M3OutlineVariant", HslToColor(h, surfS + 0.04f, 0.29f));

                res["M3Green"] = Color.FromArgb("#4ADE80");
                res["M3GreenBrush"] = new SolidColorBrush(Color.FromArgb("#4ADE80"));
                res["M3OnGreen"] = Color.FromArgb("#062112");
                res["M3GreenContainer"] = Color.FromArgb("#123426");
                res["M3GreenContainerBrush"] = new SolidColorBrush(Color.FromArgb("#123426"));
                res["M3Amber"] = Color.FromArgb("#FBBF24");
                res["M3AmberBrush"] = new SolidColorBrush(Color.FromArgb("#FBBF24"));
                res["M3Cyan"] = Color.FromArgb("#22D3EE");
                res["M3CyanBrush"] = new SolidColorBrush(Color.FromArgb("#22D3EE"));
                res["M3Orange"] = Color.FromArgb("#FB923C");
                res["M3OrangeBrush"] = new SolidColorBrush(Color.FromArgb("#FB923C"));
                res["M3Red"] = Color.FromArgb("#F87171");
                res["M3RedBrush"] = new SolidColorBrush(Color.FromArgb("#F87171"));
                res["M3RedContainer"] = Color.FromArgb("#3D1724");
                res["M3RedContainerBrush"] = new SolidColorBrush(Color.FromArgb("#3D1724"));
                res["M3SwitchThumb"] = onSurface;

                tabBg = surfaceLow;
                tabActive = secondary;
                tabInactive = outline;
                statusBar = surfaceDim;
                Logger.Log($"Material You (Dark, {seedSource}) seed={seed.ToHex()} primary={primary.ToHex()} surface={surfaceDim.ToHex()}");
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Material You error: {ex.Message}");
            return false;
        }
#else
        return false;
#endif
    }

    private static void SyncAndroidSystemBars(Color barColor, bool isLightTheme)
    {
        try
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            var window = activity?.Window;
            if (window == null) return;

            if (!OperatingSystem.IsAndroidVersionAtLeast(35))
            {
#pragma warning disable CA1422
                var nativeColor = global::Android.Graphics.Color.Argb(
                    255,
                    (int)(barColor.Red * 255),
                    (int)(barColor.Green * 255),
                    (int)(barColor.Blue * 255));
                window.SetStatusBarColor(nativeColor);
                window.SetNavigationBarColor(nativeColor);
#pragma warning restore CA1422
            }

            if (OperatingSystem.IsAndroidVersionAtLeast(23))
            {
                var controller = AndroidX.Core.View.WindowCompat.GetInsetsController(window, window.DecorView);
                if (controller != null)
                {
                    controller.AppearanceLightStatusBars = isLightTheme;
                    controller.AppearanceLightNavigationBars = isLightTheme;
                }
            }
        }
        catch { }
    }
}
