using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;

namespace Hivox.Helpers
{
    public class SystemTrayHelper : IDisposable
    {
        private TaskbarIcon _trayIcon;
        private readonly Window _mainWindow;
        private Icon _connectedIcon;
        private Icon _disconnectedIcon;
        private bool _disposed;

        public SystemTrayHelper(Window mainWindow)
        {
            _mainWindow = mainWindow;
            _connectedIcon = CreateSplashIcon(connected: true);
            _disconnectedIcon = CreateSplashIcon(connected: false);
            CreateTrayIcon();
            ApplyWindowIcon(_disconnectedIcon);
        }

        private void ApplyWindowIcon(Icon icon)
        {
            try
            {
                _mainWindow.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            }
            catch { }
        }

        private static Icon CreateSplashIcon(bool connected)
        {
            using var bmp = new Bitmap(64, 64);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            float s = 64f / 128f; // scale 128x128 SVG viewBox to 64x64

            using (var bgPath = CreateRoundedRect(14 * s, 14 * s, 100 * s, 100 * s, 28 * s))
            using (var bgBrush = new LinearGradientBrush(
                new PointF(14 * s, 14 * s), new PointF(114 * s, 114 * s),
                Color.FromArgb(0x2A, 0x1B, 0x3D), Color.FromArgb(0x14, 0x0D, 0x22)))
            using (var borderBrush = new LinearGradientBrush(
                new PointF(14 * s, 14 * s), new PointF(114 * s, 114 * s),
                Color.FromArgb(0x9D, 0x78, 0xFF), Color.FromArgb(0x4B, 0x2B, 0x85)))
            {
                borderBrush.InterpolationColors = new ColorBlend
                {
                    Colors = new[]
                    {
                        Color.FromArgb(0x9D, 0x78, 0xFF),
                        Color.FromArgb(0x7C, 0x4D, 0xFF),
                        Color.FromArgb(0x4B, 0x2B, 0x85)
                    },
                    Positions = new[] { 0f, 0.6f, 1f }
                };
                using var borderPen = new Pen(borderBrush, 2.5f * s);
                g.FillPath(bgBrush, bgPath);
                g.DrawPath(borderPen, bgPath);
            }

            // Left pillar: x=33, y=29, w=14, h=70, rx=7
            using (var leftPath = CreateRoundedRect(33 * s, 29 * s, 14 * s, 70 * s, 7 * s))
            using (var leftBrush = new LinearGradientBrush(
                new PointF(40 * s, 29 * s), new PointF(40 * s, 99 * s),
                Color.FromArgb(0xEA, 0xDD, 0xFF), Color.FromArgb(0x7C, 0x4D, 0xFF)))
            {
                g.FillPath(leftBrush, leftPath);
            }

            // Right pillar: x=81, y=29, w=14, h=70, rx=7
            using (var rightPath = CreateRoundedRect(81 * s, 29 * s, 14 * s, 70 * s, 7 * s))
            using (var rightBrush = new LinearGradientBrush(
                new PointF(88 * s, 29 * s), new PointF(88 * s, 99 * s),
                Color.FromArgb(0xB3, 0x88, 0xFF), Color.FromArgb(0xFF, 0x80, 0xE1)))
            {
                g.FillPath(rightBrush, rightPath);
            }

            // Wave: M40,78 C40,56 88,72 88,50 stroke-width=14
            using (var waveBrush = new LinearGradientBrush(
                new PointF(33 * s, 64 * s), new PointF(95 * s, 64 * s),
                Color.FromArgb(0x7C, 0x4D, 0xFF), Color.FromArgb(0xFF, 0x80, 0xE1)))
            {
                waveBrush.InterpolationColors = new ColorBlend
                {
                    Colors = new[]
                    {
                        Color.FromArgb(0x7C, 0x4D, 0xFF),
                        Color.FromArgb(0xB3, 0x88, 0xFF),
                        Color.FromArgb(0xFF, 0x80, 0xE1)
                    },
                    Positions = new[] { 0f, 0.5f, 1f }
                };
                using var wavePen = new Pen(waveBrush, 14 * s)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };
                g.DrawBezier(wavePen, 40 * s, 78 * s, 40 * s, 56 * s, 88 * s, 72 * s, 88 * s, 50 * s);
            }

            // Connection dots
            using (var topDot = new SolidBrush(Color.FromArgb(0xEA, 0xDD, 0xFF)))
            using (var botDot = new SolidBrush(Color.FromArgb(0xB3, 0x88, 0xFF)))
            {
                g.FillEllipse(topDot, 58.5f * s, 37.0f * s, 11 * s, 11 * s);
                g.FillEllipse(botDot, 58.5f * s, 80.0f * s, 11 * s, 11 * s);
            }

            if (connected)
            {
                using var ringBrush = new SolidBrush(Color.FromArgb(0x14, 0x0D, 0x22));
                using var greenBrush = new SolidBrush(Color.FromArgb(0x4A, 0xDE, 0x80));
                g.FillEllipse(ringBrush, 42, 42, 20, 20);
                g.FillEllipse(greenBrush, 45, 45, 14, 14);
            }

            return Icon.FromHandle(bmp.GetHicon());
        }

        private static GraphicsPath CreateRoundedRect(float x, float y, float w, float h, float r)
        {
            var path = new GraphicsPath();
            float d = r * 2;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void CreateTrayIcon()
        {
            _trayIcon = new TaskbarIcon
            {
                Icon = _disconnectedIcon,
                ToolTipText = "Hivox: Отключено",
                Visibility = Visibility.Visible
            };

            var contextMenu = new System.Windows.Controls.ContextMenu();
            var showItem = new System.Windows.Controls.MenuItem { Header = "Показать" };
            showItem.Click += (s, e) => ShowWindow();
            contextMenu.Items.Add(showItem);

            var toggleItem = new System.Windows.Controls.MenuItem { Header = "Подключить/Отключить" };
            toggleItem.Click += (s, e) => _mainWindow.Dispatcher.Invoke(() =>
            {
                if (_mainWindow.DataContext is ViewModels.MainViewModel vm)
                    vm.ToggleConnectionCommand.Execute(null);
            });
            contextMenu.Items.Add(toggleItem);

            var exitItem = new System.Windows.Controls.MenuItem { Header = "Выход" };
            exitItem.Click += (s, e) => Application.Current.Shutdown();
            contextMenu.Items.Add(exitItem);

            _trayIcon.ContextMenu = contextMenu;

            _trayIcon.TrayMouseDoubleClick += (s, e) => ShowWindow();
        }

        private void ShowWindow()
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }

        public void UpdateState(bool isConnected)
        {
            if (_trayIcon == null) return;
            var icon = isConnected ? _connectedIcon : _disconnectedIcon;
            _trayIcon.Icon = icon;
            ApplyWindowIcon(icon);
            _trayIcon.ToolTipText = isConnected ? "Hivox: Подключено" : "Hivox: Отключено";
        }

        public void ShowNotification(string title, string message)
        {
            _trayIcon.ShowBalloonTip(title, message, BalloonIcon.Info);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _trayIcon?.Dispose();
                _disposed = true;
            }
        }
    }
}
