using Android.Content.Res;
using Android.Graphics.Drawables;
using Android.Views;
using AndroidX.Core.View;
using Google.Android.Material.BottomNavigation;
using Microsoft.Maui.Controls.Handlers.Compatibility;
using Microsoft.Maui.Controls.Platform.Compatibility;
using Microsoft.Maui.Platform;
using AView = Android.Views.View;

namespace Hivox.Mobile.PlatformAndroid;

public class HivoxShellRenderer : ShellRenderer
{
    protected override IShellBottomNavViewAppearanceTracker CreateBottomNavViewAppearanceTracker(ShellItem shellItem)
    {
        return new HivoxBottomNavAppearanceTracker(this, shellItem);
    }

    private sealed class HivoxBottomNavInsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        private readonly Func<float, int> _dp;

        public HivoxBottomNavInsetsListener(Func<float, int> dp)
        {
            _dp = dp;
        }

        public WindowInsetsCompat? OnApplyWindowInsets(AView? v, WindowInsetsCompat? insets)
        {
            if (v == null || insets == null)
                return WindowInsetsCompat.Consumed;

            int bottomInset = insets.GetInsets(WindowInsetsCompat.Type.SystemBars())?.Bottom ?? 0;

            int px = _dp(6);
            int py = _dp(4);
            if (v.PaddingLeft != px || v.PaddingTop != py || v.PaddingRight != px || v.PaddingBottom != py)
                v.SetPadding(px, py, px, py);

            if (v.LayoutParameters is ViewGroup.MarginLayoutParams lp)
            {
                int mx = _dp(14);
                int mt = _dp(4);
                int mb = bottomInset + _dp(8);
                int h = _dp(68);
                if (lp.LeftMargin != mx || lp.RightMargin != mx || lp.TopMargin != mt || lp.BottomMargin != mb || lp.Height != h)
                {
                    lp.SetMargins(mx, mt, mx, mb);
                    lp.Height = h;
                    v.LayoutParameters = lp;
                }
            }

            return WindowInsetsCompat.Consumed;
        }
    }

    private sealed class HivoxBottomNavAppearanceTracker : ShellBottomNavViewAppearanceTracker
    {
        public HivoxBottomNavAppearanceTracker(IShellContext shellContext, ShellItem shellItem)
            : base(shellContext, shellItem)
        {
        }

        public override void SetAppearance(BottomNavigationView bottomView, IShellAppearanceElement appearance)
        {
            base.SetAppearance(bottomView, appearance);

            var ctx = bottomView.Context;
            if (ctx?.Resources?.DisplayMetrics == null) return;
            float density = ctx.Resources.DisplayMetrics.Density;
            int Dp(float v) => (int)Math.Round(v * density);

            var surfaceDim = ResolveThemeColor("M3SurfaceDim", Color.FromArgb("#0D0817"));
            var tabBg = ResolveThemeColor("M3TabBarBg", Color.FromArgb("#1A102A"));
            var outlineVariant = ResolveThemeColor("M3OutlineVariant", Color.FromArgb("#3A2856"));
            var primaryContainer = ResolveThemeColor("M3PrimaryContainer", Color.FromArgb("#2A1B3D"));
            var primary = ResolveThemeColor("M3Primary", Color.FromArgb("#7C4DFF"));

            // Ensure surrounding container outside rounded corners matches page canvas seamlessly
            if (bottomView.Parent is AView parentView)
                parentView.SetBackgroundColor(surfaceDim.ToPlatform());

            // Floating rounded dock capsule
            var dockBg = new GradientDrawable();
            dockBg.SetShape(ShapeType.Rectangle);
            dockBg.SetCornerRadius(Dp(26));
            dockBg.SetColor(tabBg.ToPlatform());
            dockBg.SetStroke(Dp(1), outlineVariant.ToPlatform());
            bottomView.Background = dockBg;
            bottomView.ClipToOutline = true;
            bottomView.Elevation = Dp(6);
            bottomView.SetPadding(Dp(6), Dp(4), Dp(6), Dp(4));

            if (bottomView.LayoutParameters is ViewGroup.MarginLayoutParams lp)
            {
                int mx = Dp(14);
                int mt = Dp(4);
                int h = Dp(68);
                if (lp.LeftMargin != mx || lp.RightMargin != mx || lp.TopMargin != mt || lp.Height != h || lp.BottomMargin < Dp(8))
                {
                    int mb = Math.Max(lp.BottomMargin, Dp(10));
                    lp.SetMargins(mx, mt, mx, mb);
                    lp.Height = h;
                    bottomView.LayoutParameters = lp;
                }
            }

            bottomView.LabelVisibilityMode = LabelVisibilityMode.LabelVisibilityLabeled;
            bottomView.ItemIconSize = Dp(21);
            bottomView.ItemPaddingTop = Dp(8);
            bottomView.ItemPaddingBottom = Dp(8);
            bottomView.ItemActiveIndicatorEnabled = false;
            bottomView.ItemRippleColor = ColorStateList.ValueOf(primary.WithAlpha(0.16f).ToPlatform());

            ViewCompat.SetOnApplyWindowInsetsListener(bottomView, new HivoxBottomNavInsetsListener(Dp));
            ViewCompat.RequestApplyInsets(bottomView);

            // Full-tab rounded active pill around icon + label (matching floating dock style)
            if (bottomView.GetChildAt(0) is ViewGroup menuView)
            {
                for (int i = 0; i < menuView.ChildCount; i++)
                {
                    var child = menuView.GetChildAt(i);
                    if (child == null) continue;
                    child.Background = BuildTabItemBackground(primaryContainer, primary, Dp);
                }
            }
        }

        private static StateListDrawable BuildTabItemBackground(
            Color activeBgColor, Color accentColor, Func<float, int> dp)
        {
            Drawable MakePill()
            {
                var pill = new GradientDrawable();
                pill.SetShape(ShapeType.Rectangle);
                pill.SetCornerRadius(dp(20));
                pill.SetColor(activeBgColor.ToPlatform());
                pill.SetStroke(dp(1), accentColor.WithAlpha(0.32f).ToPlatform());
                return new InsetDrawable(pill, dp(4), dp(3), dp(4), dp(3));
            }

            var stateList = new StateListDrawable();
            stateList.AddState(new[] { global::Android.Resource.Attribute.StateChecked }, MakePill());
            stateList.AddState(new[] { global::Android.Resource.Attribute.StateSelected }, MakePill());
            stateList.AddState(Array.Empty<int>(), new ColorDrawable(global::Android.Graphics.Color.Transparent));
            return stateList;
        }

        private static Color ResolveThemeColor(string key, Color fallback)
        {
            if (Application.Current?.Resources.TryGetValue(key, out var val) == true && val is Color c)
                return c;
            return fallback;
        }
    }
}
