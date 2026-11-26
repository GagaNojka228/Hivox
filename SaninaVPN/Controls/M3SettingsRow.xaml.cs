using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Hivox.Controls
{
    public partial class M3SettingsRow : UserControl
    {
        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register("Label", typeof(string), typeof(M3SettingsRow), new PropertyMetadata("", OnLabelChanged));
        public static readonly DependencyProperty SubtitleProperty =
            DependencyProperty.Register("Subtitle", typeof(string), typeof(M3SettingsRow), new PropertyMetadata("", OnSubtitleChanged));
        public static readonly DependencyProperty IconDataProperty =
            DependencyProperty.Register("IconData", typeof(Geometry), typeof(M3SettingsRow), new PropertyMetadata(null, OnIconDataChanged));
        public static readonly DependencyProperty IsLinkProperty =
            DependencyProperty.Register("IsLink", typeof(bool), typeof(M3SettingsRow), new PropertyMetadata(false, OnIsLinkChanged));
        public static readonly DependencyProperty IsToggleProperty =
            DependencyProperty.Register("IsToggle", typeof(bool), typeof(M3SettingsRow), new PropertyMetadata(false, OnToggleChanged));
        public static readonly DependencyProperty IsCheckedProperty =
            DependencyProperty.Register("IsChecked", typeof(bool), typeof(M3SettingsRow), new PropertyMetadata(false, OnIsCheckedChanged));
        public static readonly DependencyProperty ValueTextProperty =
            DependencyProperty.Register("ValueText", typeof(string), typeof(M3SettingsRow), new PropertyMetadata("", OnValueTextChanged));

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public string Subtitle
        {
            get => (string)GetValue(SubtitleProperty);
            set => SetValue(SubtitleProperty, value);
        }

        public Geometry IconData
        {
            get => (Geometry)GetValue(IconDataProperty);
            set => SetValue(IconDataProperty, value);
        }

        public bool IsLink
        {
            get => (bool)GetValue(IsLinkProperty);
            set => SetValue(IsLinkProperty, value);
        }

        public bool IsToggle
        {
            get => (bool)GetValue(IsToggleProperty);
            set => SetValue(IsToggleProperty, value);
        }

        public bool IsChecked
        {
            get => (bool)GetValue(IsCheckedProperty);
            set => SetValue(IsCheckedProperty, value);
        }

        public string ValueText
        {
            get => (string)GetValue(ValueTextProperty);
            set => SetValue(ValueTextProperty, value);
        }

        public event RoutedEventHandler Click;
        public event EventHandler<bool> CheckedChanged;

        public M3SettingsRow()
        {
            InitializeComponent();
        }

        private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is M3SettingsRow row)
                row.LabelElement.Text = e.NewValue as string ?? "";
        }

        private static void OnSubtitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is M3SettingsRow row)
            {
                var sub = e.NewValue as string ?? "";
                row.SubtitleElement.Text = sub;
                row.SubtitleElement.Visibility = string.IsNullOrEmpty(sub) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private static void OnIconDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is M3SettingsRow row)
            {
                var geo = e.NewValue as Geometry;
                row.IconPath.Data = geo;
                row.IconBadge.Visibility = geo == null ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private static void OnIsLinkChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is M3SettingsRow row)
                row.UpdateRightContent();
        }

        private static void OnToggleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is M3SettingsRow row)
                row.UpdateRightContent();
        }

        private static void OnValueTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is M3SettingsRow row)
                row.UpdateRightContent();
        }

        private static void OnIsCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is M3SettingsRow row)
            {
                if (row.ValueElement.Content is CheckBox toggle)
                    toggle.IsChecked = (bool)e.NewValue;
                row.CheckedChanged?.Invoke(row, (bool)e.NewValue);
            }
        }

        private void UpdateRightContent()
        {
            if (IsToggle)
            {
                var toggle = new CheckBox
                {
                    Style = (Style)FindResource("M3ToggleSwitch"),
                    IsChecked = IsChecked,
                    VerticalAlignment = VerticalAlignment.Center
                };
                toggle.Checked += (_, _) => IsChecked = true;
                toggle.Unchecked += (_, _) => IsChecked = false;
                ValueElement.Content = toggle;
            }
            else if (IsLink)
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                if (!string.IsNullOrEmpty(ValueText) && ValueText != "→")
                {
                    var pill = new Border
                    {
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(9, 4, 9, 4),
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    pill.SetResourceReference(Border.BackgroundProperty, "M3SurfaceContainerHighestBrush");
                    var tb = new TextBlock
                    {
                        Text = ValueText,
                        FontSize = 11.5,
                        FontWeight = FontWeights.SemiBold
                    };
                    tb.SetResourceReference(TextBlock.ForegroundProperty, "M3OnSurfaceVariantBrush");
                    pill.Child = tb;
                    panel.Children.Add(pill);
                }
                var chevron = new Path
                {
                    Data = (Geometry)FindResource("IconChevronRight"),
                    Width = 14,
                    Height = 14,
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center
                };
                chevron.SetResourceReference(Shape.FillProperty, "M3OutlineBrush");
                panel.Children.Add(chevron);
                ValueElement.Content = panel;
            }
            else
            {
                var tb = new TextBlock
                {
                    Text = ValueText ?? "",
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };
                tb.SetResourceReference(TextBlock.ForegroundProperty, "M3OnSurfaceVariantBrush");
                ValueElement.Content = tb;
            }
        }

        private void OnClick(object sender, MouseButtonEventArgs e)
        {
            if (IsToggle) return;
            Click?.Invoke(this, new RoutedEventArgs());
        }
    }
}
