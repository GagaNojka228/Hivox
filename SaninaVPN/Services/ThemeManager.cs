using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Hivox.Models;

namespace Hivox.Services
{
    public static class ThemeManager
    {
        public static void ApplyThemeResources(AppTheme theme)
        {
            var res = Application.Current?.Resources;
            if (res == null) return;

            static void SetBrush(ResourceDictionary dict, string key, string hex, double opacity = 1.0)
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                dict[key] = new SolidColorBrush(c) { Opacity = opacity };
            }

            if (theme == AppTheme.Dark)
            {
                SetBrush(res, "M3PrimaryBrush", "#7C4DFF");
                SetBrush(res, "M3OnPrimaryBrush", "#FFFFFF");
                SetBrush(res, "M3PrimaryContainerBrush", "#2A1B3D");
                SetBrush(res, "M3OnPrimaryContainerBrush", "#EADDFF");
                SetBrush(res, "M3SecondaryBrush", "#B388FF");
                SetBrush(res, "M3OnSecondaryBrush", "#1A102A");
                SetBrush(res, "M3SecondaryContainerBrush", "#33204C");
                SetBrush(res, "M3OnSecondaryContainerBrush", "#EADDFF");
                SetBrush(res, "M3TertiaryBrush", "#FF80E1");
                SetBrush(res, "M3SurfaceDimBrush", "#0D0817");
                SetBrush(res, "M3SurfaceBrush", "#140D22");
                SetBrush(res, "M3SurfaceContainerLowBrush", "#1A102A");
                SetBrush(res, "M3SurfaceContainerBrush", "#201533");
                SetBrush(res, "M3SurfaceContainerHighBrush", "#2A1B3D");
                SetBrush(res, "M3SurfaceContainerHighestBrush", "#35234E");
                SetBrush(res, "M3SurfaceVariantBrush", "#43325E");
                SetBrush(res, "M3OnSurfaceBrush", "#E6E1F5");
                SetBrush(res, "M3OnSurfaceVariantBrush", "#B9B0D8");
                SetBrush(res, "M3OutlineBrush", "#8A7FA8");
                SetBrush(res, "M3OutlineVariantBrush", "#3A2856");
                SetBrush(res, "M3PrimaryHoverBrush", "#936BFF");
                SetBrush(res, "M3PrimaryPressedBrush", "#6535EE");
                SetBrush(res, "M3GreenBrush", "#4ADE80");
                SetBrush(res, "M3OnGreenBrush", "#062112");
                SetBrush(res, "M3GreenContainerBrush", "#123426");
                SetBrush(res, "M3AmberBrush", "#FBBF24");
                SetBrush(res, "M3RedBrush", "#F87171");
                SetBrush(res, "M3RedContainerBrush", "#3D1724");
            }
            else if (theme == AppTheme.Black)
            {
                SetBrush(res, "M3PrimaryBrush", "#7C4DFF");
                SetBrush(res, "M3OnPrimaryBrush", "#FFFFFF");
                SetBrush(res, "M3PrimaryContainerBrush", "#1D1330");
                SetBrush(res, "M3OnPrimaryContainerBrush", "#EADDFF");
                SetBrush(res, "M3SecondaryBrush", "#B388FF");
                SetBrush(res, "M3OnSecondaryBrush", "#000000");
                SetBrush(res, "M3SecondaryContainerBrush", "#1C152B");
                SetBrush(res, "M3OnSecondaryContainerBrush", "#EADDFF");
                SetBrush(res, "M3TertiaryBrush", "#FF80E1");
                SetBrush(res, "M3SurfaceDimBrush", "#000000");
                SetBrush(res, "M3SurfaceBrush", "#050507");
                SetBrush(res, "M3SurfaceContainerLowBrush", "#0A0A0E");
                SetBrush(res, "M3SurfaceContainerBrush", "#0F0F14");
                SetBrush(res, "M3SurfaceContainerHighBrush", "#17171F");
                SetBrush(res, "M3SurfaceContainerHighestBrush", "#21212D");
                SetBrush(res, "M3SurfaceVariantBrush", "#2A2A36");
                SetBrush(res, "M3OnSurfaceBrush", "#F1EEFA");
                SetBrush(res, "M3OnSurfaceVariantBrush", "#A8A2C0");
                SetBrush(res, "M3OutlineBrush", "#78728E");
                SetBrush(res, "M3OutlineVariantBrush", "#23232F");
                SetBrush(res, "M3PrimaryHoverBrush", "#936BFF");
                SetBrush(res, "M3PrimaryPressedBrush", "#6535EE");
                SetBrush(res, "M3GreenBrush", "#4ADE80");
                SetBrush(res, "M3OnGreenBrush", "#062112");
                SetBrush(res, "M3GreenContainerBrush", "#0C291C");
                SetBrush(res, "M3AmberBrush", "#FBBF24");
                SetBrush(res, "M3RedBrush", "#F87171");
                SetBrush(res, "M3RedContainerBrush", "#2D1018");
            }
            else
            {
                SetBrush(res, "M3PrimaryBrush", "#6750A4");
                SetBrush(res, "M3OnPrimaryBrush", "#FFFFFF");
                SetBrush(res, "M3PrimaryContainerBrush", "#EADDFF");
                SetBrush(res, "M3OnPrimaryContainerBrush", "#21005D");
                SetBrush(res, "M3SecondaryBrush", "#625B71");
                SetBrush(res, "M3OnSecondaryBrush", "#FFFFFF");
                SetBrush(res, "M3SecondaryContainerBrush", "#E8DEF8");
                SetBrush(res, "M3OnSecondaryContainerBrush", "#1D192B");
                SetBrush(res, "M3TertiaryBrush", "#7D5260");
                SetBrush(res, "M3SurfaceDimBrush", "#F2ECF9");
                SetBrush(res, "M3SurfaceBrush", "#FAF7FF");
                SetBrush(res, "M3SurfaceContainerLowBrush", "#F5EFFB");
                SetBrush(res, "M3SurfaceContainerBrush", "#FFFFFF");
                SetBrush(res, "M3SurfaceContainerHighBrush", "#ECE6F4");
                SetBrush(res, "M3SurfaceContainerHighestBrush", "#E2DAEE");
                SetBrush(res, "M3SurfaceVariantBrush", "#E7E0EC");
                SetBrush(res, "M3OnSurfaceBrush", "#1D1B20");
                SetBrush(res, "M3OnSurfaceVariantBrush", "#49454F");
                SetBrush(res, "M3OutlineBrush", "#79747E");
                SetBrush(res, "M3OutlineVariantBrush", "#D5CEE0");
                SetBrush(res, "M3PrimaryHoverBrush", "#7962B8");
                SetBrush(res, "M3PrimaryPressedBrush", "#553E91");
                SetBrush(res, "M3GreenBrush", "#16A34A");
                SetBrush(res, "M3OnGreenBrush", "#FFFFFF");
                SetBrush(res, "M3GreenContainerBrush", "#DCFCE7");
                SetBrush(res, "M3AmberBrush", "#D97706");
                SetBrush(res, "M3RedBrush", "#DC2626");
                SetBrush(res, "M3RedContainerBrush", "#FEE2E2");
            }
        }

        public static Dictionary<string, string> GetColors(AppTheme theme)
        {
            if (theme == AppTheme.Dark)
            {
                return new Dictionary<string, string>
                {
                    ["bg_main"] = "#0D0817", ["bg_card"] = "#140D22", ["bg_card_elevated"] = "#201533",
                    ["bg_hover"] = "#2A1B3D", ["bg_active"] = "#7C4DFF",
                    ["accent"] = "#7C4DFF", ["accent_hover"] = "#936BFF",
                    ["green"] = "#4ADE80", ["green_light"] = "#86EFAC", ["red"] = "#F87171",
                    ["red_light"] = "#FCA5A5", ["yellow"] = "#FBBF24", ["yellow_light"] = "#FDE68A",
                    ["blue"] = "#60A5FA", ["cyan"] = "#67E8F9", ["orange"] = "#FB923C",
                    ["text"] = "#E6E1F5", ["text_secondary"] = "#B9B0D8", ["text_muted"] = "#8A7FA8",
                    ["border"] = "#3A2856", ["border_light"] = "#563E7A", ["divider"] = "#2A1B3D",
                    ["graph_upload"] = "#FF80E1", ["graph_download"] = "#7C4DFF",
                    ["graph_background"] = "#1A102A", ["graph_grid"] = "#2A1B3D"
                };
            }
            if (theme == AppTheme.Black)
            {
                return new Dictionary<string, string>
                {
                    ["bg_main"] = "#000000", ["bg_card"] = "#0F0F14", ["bg_card_elevated"] = "#17171F",
                    ["bg_hover"] = "#21212D", ["bg_active"] = "#7C4DFF",
                    ["accent"] = "#7C4DFF", ["accent_hover"] = "#936BFF",
                    ["green"] = "#4ADE80", ["green_light"] = "#86EFAC", ["red"] = "#F87171",
                    ["red_light"] = "#FCA5A5", ["yellow"] = "#FBBF24", ["yellow_light"] = "#FDE68A",
                    ["blue"] = "#60A5FA", ["cyan"] = "#67E8F9", ["orange"] = "#FB923C",
                    ["text"] = "#F1EEFA", ["text_secondary"] = "#A8A2C0", ["text_muted"] = "#78728E",
                    ["border"] = "#23232F", ["border_light"] = "#373748", ["divider"] = "#17171F",
                    ["graph_upload"] = "#FF80E1", ["graph_download"] = "#7C4DFF",
                    ["graph_background"] = "#0A0A0E", ["graph_grid"] = "#17171F"
                };
            }
            else
            {
                return new Dictionary<string, string>
                {
                    ["bg_main"] = "#F2ECF9", ["bg_card"] = "#FFFFFF", ["bg_card_elevated"] = "#ECE6F4",
                    ["bg_hover"] = "#E2DAEE", ["bg_active"] = "#EADDFF",
                    ["accent"] = "#6750A4", ["accent_hover"] = "#7962B8",
                    ["green"] = "#16A34A", ["green_light"] = "#22C55E", ["red"] = "#DC2626",
                    ["red_light"] = "#EF4444", ["yellow"] = "#CA8A04", ["yellow_light"] = "#EAB308",
                    ["blue"] = "#2563EB", ["cyan"] = "#0891B2", ["orange"] = "#EA580C",
                    ["text"] = "#1D1B20", ["text_secondary"] = "#49454F", ["text_muted"] = "#79747E",
                    ["border"] = "#D5CEE0", ["border_light"] = "#CAC4D0", ["divider"] = "#ECE6F4",
                    ["graph_upload"] = "#16A34A", ["graph_download"] = "#6750A4",
                    ["graph_background"] = "#F5EFFB", ["graph_grid"] = "#E2DAEE"
                };
            }
        }
    }
}
