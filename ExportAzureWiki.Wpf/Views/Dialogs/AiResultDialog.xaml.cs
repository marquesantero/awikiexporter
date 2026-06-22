using System.ComponentModel;
using System.Windows;
using Markdig;

namespace ExportAzureWiki.Wpf.Views.Dialogs;

public enum AiResultDialogAction
{
    Close = 0,
    AddAsPreviewPage = 1
}

public partial class AiResultDialog : Window, INotifyPropertyChanged
{
    private static readonly MarkdownPipeline PreviewPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private string _markdownContent;

    public AiResultDialog(string title, string markdownContent)
    {
        InitializeComponent();
        _markdownContent = markdownContent ?? string.Empty;
        DialogTitle = string.IsNullOrWhiteSpace(title)
            ? AppText.S("wpf.ai.result.dialog.title", "AI Result")
            : title.Trim();
        DataContext = this;
        Loaded += async (_, _) => await RenderPreviewAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AiResultDialogAction Action { get; private set; } = AiResultDialogAction.Close;

    public string DialogTitle { get; }
    public string StatusText => AppText.S("wpf.ai.result.dialog.status", "Review and choose how to apply this content.");
    public string MarkdownTabText => AppText.S("wpf.ai.result.dialog.tab.markdown", "Markdown");
    public string PreviewTabText => AppText.S("wpf.ai.result.dialog.tab.preview", "Preview");
    public string AddAsPreviewPageText => AppText.S("wpf.ai.result.dialog.add_preview", "Add as preview page");
    public string CloseText => AppText.S("common.close", "Close");

    public string MarkdownContent
    {
        get => _markdownContent;
        set
        {
            if (string.Equals(_markdownContent, value, StringComparison.Ordinal))
            {
                return;
            }

            _markdownContent = value ?? string.Empty;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MarkdownContent)));
            _ = RenderPreviewAsync();
        }
    }

    private async Task RenderPreviewAsync()
    {
        try
        {
            var body = Markdown.ToHtml(MarkdownContent ?? string.Empty, PreviewPipeline);
            var html = """
                       <!doctype html>
                       <html>
                       <head>
                         <meta charset="utf-8" />
                         <style>
                           body { font-family: Segoe UI, Arial, sans-serif; margin: 14px; color: #1f1f1f; background: #ffffff; }
                           pre { background: #f3f5f7; padding: 10px; border-radius: 8px; overflow: auto; }
                           code { font-family: Consolas, monospace; }
                           table { border-collapse: collapse; width: 100%; }
                           th, td { border: 1px solid #d1d9e0; padding: 6px; text-align: left; vertical-align: top; }
                           blockquote { border-left: 4px solid #0F6CBD; margin-left: 0; padding-left: 10px; color: #3F4A59; }
                         </style>
                       </head>
                       <body>
                       """ + body + """
                       </body>
                       </html>
                       """;

            await wbPreview.EnsureCoreWebView2Async();
            wbPreview.NavigateToString(html);
        }
        catch
        {
            // Keep modal resilient.
        }
    }

    private void BtnAddAsPreviewPage_OnClick(object sender, RoutedEventArgs e)
    {
        Action = AiResultDialogAction.AddAsPreviewPage;
        DialogResult = true;
        Close();
    }

    private void BtnClose_OnClick(object sender, RoutedEventArgs e)
    {
        Action = AiResultDialogAction.Close;
        DialogResult = false;
        Close();
    }
}
