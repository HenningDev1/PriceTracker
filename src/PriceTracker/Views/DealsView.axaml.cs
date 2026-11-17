using Avalonia;
using Avalonia.Controls;

namespace PriceTracker.Views;

public partial class DealsView : UserControl
{
    public static readonly StyledProperty<double> CardWidthProperty =
        AvaloniaProperty.Register<DealsView, double>(nameof(CardWidth), 300);

    /// Width of one grid cell (card + gap), so the cards always fill the row: 3 columns at normal sizes.
    public double CardWidth { get => GetValue(CardWidthProperty); set => SetValue(CardWidthProperty, value); }

    private const double MinCard = 250, Gap = 14;

    public DealsView()
    {
        InitializeComponent();
        DealsGrid.SizeChanged += (_, e) =>
        {
            var w = e.NewSize.Width - 2;   // avoid rounding into a wrap
            int cols = Math.Max(1, (int)(w / (MinCard + Gap)));
            CardWidth = Math.Floor(w / cols);   // each cell = card + its 14 px right margin
        };
    }
}
