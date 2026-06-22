using System.Windows;
using System.Windows.Controls;
using ExportAzureWiki.Core.Localization;

namespace ExportAzureWiki.Wpf.Controls;

public partial class SensitiveTextBox : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(SensitiveTextBox),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnTextChanged));

    public static readonly DependencyProperty IsRevealedProperty = DependencyProperty.Register(
        nameof(IsRevealed),
        typeof(bool),
        typeof(SensitiveTextBox),
        new PropertyMetadata(false, OnVisualStateChanged));

    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly),
        typeof(bool),
        typeof(SensitiveTextBox),
        new PropertyMetadata(false, OnVisualStateChanged));

    public static readonly DependencyProperty AcceptsReturnProperty = DependencyProperty.Register(
        nameof(AcceptsReturn),
        typeof(bool),
        typeof(SensitiveTextBox),
        new PropertyMetadata(false, OnTextBoxBehaviorChanged));

    public static readonly DependencyProperty TextWrappingProperty = DependencyProperty.Register(
        nameof(TextWrapping),
        typeof(TextWrapping),
        typeof(SensitiveTextBox),
        new PropertyMetadata(TextWrapping.NoWrap, OnTextBoxBehaviorChanged));

    public static readonly DependencyProperty VerticalScrollBarVisibilityProperty = DependencyProperty.Register(
        nameof(VerticalScrollBarVisibility),
        typeof(ScrollBarVisibility),
        typeof(SensitiveTextBox),
        new PropertyMetadata(ScrollBarVisibility.Disabled, OnTextBoxBehaviorChanged));

    private bool _syncing;

    public SensitiveTextBox()
    {
        InitializeComponent();
        SyncControlsFromText();
        ApplyTextBoxBehavior();
        UpdateVisualState();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    public bool IsRevealed
    {
        get => (bool)GetValue(IsRevealedProperty);
        set => SetValue(IsRevealedProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public bool AcceptsReturn
    {
        get => (bool)GetValue(AcceptsReturnProperty);
        set => SetValue(AcceptsReturnProperty, value);
    }

    public TextWrapping TextWrapping
    {
        get => (TextWrapping)GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(VerticalScrollBarVisibilityProperty);
        set => SetValue(VerticalScrollBarVisibilityProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SensitiveTextBox)d).SyncControlsFromText();
    }

    private static void OnVisualStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SensitiveTextBox)d).UpdateVisualState();
    }

    private static void OnTextBoxBehaviorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SensitiveTextBox)d).ApplyTextBoxBehavior();
    }

    private void ToggleButton_OnClick(object sender, RoutedEventArgs e)
    {
        IsRevealed = !IsRevealed;

        // Keep the caret in the field the user is editing after the swap.
        if (IsReadOnly)
        {
            return;
        }

        if (IsRevealed)
        {
            VisibleTextBox.Focus();
            VisibleTextBox.CaretIndex = VisibleTextBox.Text?.Length ?? 0;
        }
        else
        {
            HiddenPasswordBox.Focus();
        }
    }

    private void HiddenPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing || IsReadOnly)
        {
            return;
        }

        Text = HiddenPasswordBox.Password;
    }

    private void VisibleTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || IsReadOnly)
        {
            return;
        }

        Text = VisibleTextBox.Text;
    }

    private void SyncControlsFromText()
    {
        var value = Text ?? string.Empty;
        _syncing = true;
        try
        {
            if (HiddenPasswordBox.Password != value)
            {
                HiddenPasswordBox.Password = value;
            }

            if (VisibleTextBox.Text != value)
            {
                VisibleTextBox.Text = value;
            }

            MaskedReadOnlyTextBox.Text = string.IsNullOrEmpty(value)
                ? string.Empty
                : new string('*', Math.Min(value.Length, 32));
        }
        finally
        {
            _syncing = false;
        }
    }

    private void ApplyTextBoxBehavior()
    {
        VisibleTextBox.AcceptsReturn = AcceptsReturn;
        VisibleTextBox.TextWrapping = TextWrapping;
        VisibleTextBox.VerticalScrollBarVisibility = VerticalScrollBarVisibility;
        MaskedReadOnlyTextBox.AcceptsReturn = AcceptsReturn;
        MaskedReadOnlyTextBox.TextWrapping = TextWrapping;
        MaskedReadOnlyTextBox.VerticalScrollBarVisibility = VerticalScrollBarVisibility;
    }

    private void UpdateVisualState()
    {
        ToggleButton.Content = IsRevealed ? "\uE8F4" : "\uE890";
        var toggleLabel = IsRevealed
            ? AppText.S("wpf.common.hide_value", "Hide value")
            : AppText.S("wpf.common.show_value", "Show value");
        ToggleButton.ToolTip = toggleLabel;
        System.Windows.Automation.AutomationProperties.SetName(ToggleButton, toggleLabel);

        HiddenPasswordBox.Visibility = !IsReadOnly && !IsRevealed ? Visibility.Visible : Visibility.Collapsed;
        MaskedReadOnlyTextBox.Visibility = IsReadOnly && !IsRevealed ? Visibility.Visible : Visibility.Collapsed;
        VisibleTextBox.Visibility = IsRevealed ? Visibility.Visible : Visibility.Collapsed;
        VisibleTextBox.IsReadOnly = IsReadOnly;
    }
}
