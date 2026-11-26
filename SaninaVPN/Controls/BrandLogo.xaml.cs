using System.Windows;
using System.Windows.Controls;

namespace Hivox.Controls
{
    public partial class BrandLogo : UserControl
    {
        public static readonly DependencyProperty CornerRadiusSizeProperty =
            DependencyProperty.Register(nameof(CornerRadiusSize), typeof(CornerRadius), typeof(BrandLogo),
                new PropertyMetadata(new CornerRadius(9)));

        public CornerRadius CornerRadiusSize
        {
            get => (CornerRadius)GetValue(CornerRadiusSizeProperty);
            set => SetValue(CornerRadiusSizeProperty, value);
        }

        public BrandLogo()
        {
            InitializeComponent();
        }
    }
}
