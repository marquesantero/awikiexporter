using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ExportAzureWiki.Wpf.Controls;

/// <summary>
/// Attached placeholder (watermark) text for a <see cref="TextBox"/>. Shown,
/// greyed, while the box is empty; drawn via an adorner so the default TextBox
/// chrome is preserved. Usage: <c>controls:Watermark.Text="Hint"</c>.
/// </summary>
public static class Watermark
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Watermark), new FrameworkPropertyMetadata(null, OnTextChanged));

    public static string GetText(DependencyObject obj) => (string)obj.GetValue(TextProperty);
    public static void SetText(DependencyObject obj, string value) => obj.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox)
        {
            return;
        }

        textBox.Loaded -= OnRefresh;
        textBox.TextChanged -= OnRefresh;
        textBox.IsVisibleChanged -= OnVisibilityChanged;
        textBox.Loaded += OnRefresh;
        textBox.TextChanged += OnRefresh;
        textBox.IsVisibleChanged += OnVisibilityChanged;

        if (textBox.IsLoaded)
        {
            UpdateAdorner(textBox);
        }
    }

    private static void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            UpdateAdorner(textBox);
        }
    }

    private static void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            UpdateAdorner(textBox);
        }
    }

    private static void UpdateAdorner(TextBox textBox)
    {
        var layer = AdornerLayer.GetAdornerLayer(textBox);
        if (layer == null)
        {
            return;
        }

        var existing = layer.GetAdorners(textBox)?.OfType<WatermarkAdorner>().FirstOrDefault();
        var shouldShow = textBox.IsVisible
            && string.IsNullOrEmpty(textBox.Text)
            && !string.IsNullOrEmpty(GetText(textBox));

        if (shouldShow && existing == null)
        {
            layer.Add(new WatermarkAdorner(textBox, GetText(textBox)));
        }
        else if (!shouldShow && existing != null)
        {
            layer.Remove(existing);
        }
    }

    private sealed class WatermarkAdorner : Adorner
    {
        private readonly string _text;

        public WatermarkAdorner(UIElement adornedElement, string text) : base(adornedElement)
        {
            _text = text;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var textBox = (TextBox)AdornedElement;
            var typeface = new Typeface(textBox.FontFamily, textBox.FontStyle, textBox.FontWeight, textBox.FontStretch);
            var formatted = new FormattedText(
                _text,
                System.Globalization.CultureInfo.CurrentUICulture,
                textBox.FlowDirection,
                typeface,
                textBox.FontSize,
                new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xB2)),
                VisualTreeHelper.GetDpi(textBox).PixelsPerDip)
            {
                MaxTextWidth = Math.Max(0, textBox.ActualWidth - textBox.Padding.Left - textBox.Padding.Right - 8),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis
            };

            var y = (textBox.ActualHeight - formatted.Height) / 2;
            drawingContext.DrawText(formatted, new Point(textBox.Padding.Left + 4, Math.Max(2, y)));
        }
    }
}
