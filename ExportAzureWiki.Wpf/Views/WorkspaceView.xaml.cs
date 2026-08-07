using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using ExportAzureWiki.Wpf.ViewModels;
using ExportAzureWiki.Wpf.Views.Dialogs;
using ExportAzureWiki;
using ExportAzureWiki.Services;

namespace ExportAzureWiki.Wpf.Views;

public partial class WorkspaceView : UserControl
{
    private const string LocalAssetsHost = "local.assets";
    private const string LocalImagesHost = "local.images";
    private static readonly Regex CspMetaRegex = new(
        "<meta[^>]+http-equiv\\s*=\\s*[\"']?Content-Security-Policy[\"']?[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private bool _previewHostLoaded;
    private readonly SemaphoreSlim _pdfPrintLock = new(1, 1);
    private AiCenterViewModel? _aiCenter;
    // Cores already wired with the local.images decryption interceptor, so the
    // handler/filter is added at most once per WebView2 (ConfigureVirtualHostMapping
    // can run again for the same core).
    private readonly HashSet<CoreWebView2> _imageInterceptorCores = [];

    public WorkspaceView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => HookAiCenter();
        Unloaded += (_, _) =>
        {
            if (_aiCenter != null)
            {
                _aiCenter.ResultReady -= OnAiResultReady;
                _aiCenter = null;
            }
        };
    }

    private async void TreePages_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is not WorkspaceViewModel vm)
        {
            return;
        }

        await vm.SelectPageAsync(e.NewValue as WikiPageNodeViewModel);
        RenderPreview(vm);
    }

    private async void TreeLocalPages_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is not WorkspaceViewModel vm)
        {
            return;
        }

        await vm.SelectLocalPageAsync(e.NewValue as LocalPageNodeViewModel);
        RenderPreview(vm);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is WorkspaceViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnWorkspacePropertyChanged;
            oldVm.PdfPrintHandlerAsync = null;
            oldVm.MermaidRenderHandler = null;
        }

        if (e.NewValue is WorkspaceViewModel newVm)
        {
            newVm.PropertyChanged += OnWorkspacePropertyChanged;
            newVm.PdfPrintHandlerAsync = PrintHtmlToPdfAsync;
            newVm.MermaidRenderHandler = RenderMermaidToPngAsync;
            HookAiCenter();
            RenderPreview(newVm);
        }
    }

    private readonly SemaphoreSlim _mermaidLock = new(1, 1);

    /// <summary>
    /// Renders a single Mermaid diagram locally (no mermaid.ink) using an
    /// offscreen WebView2 with the bundled mermaid script, and returns a PNG of
    /// the rendered SVG via Chromium screenshot capture. Returns null on failure
    /// so the backend export pipeline can try its own Mermaid pass.
    /// </summary>
    private async Task<byte[]?> RenderMermaidToPngAsync(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        await _mermaidLock.WaitAsync();
        try
        {
            await wbMermaidHost.EnsureCoreWebView2Async();
            var core = wbMermaidHost.CoreWebView2;
            if (core == null)
            {
                return null;
            }

            ConfigureVirtualHostMapping(core);
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;

            var encoded = System.Net.WebUtility.HtmlEncode(source);
            var html = """
                <!doctype html><html><head><meta charset="utf-8" />
                <script src="https://local.assets/vendor/mermaid/mermaid.min.js"
                        onerror="this.onerror=null;this.src='https://cdn.jsdelivr.net/npm/mermaid@10/dist/mermaid.min.js';"></script>
                <style>html,body{margin:0;padding:0;background:#ffffff;}#d{display:inline-block;padding:4px;}</style>
                </head><body>
                <div id="d" class="mermaid">__MERMAID_SRC__</div>
                <script>
                  window.__done=0;
                  function go(){try{mermaid.initialize({startOnLoad:false,securityLevel:'strict'});
                    mermaid.run({nodes:[document.getElementById('d')]}).then(function(){window.__done=1;}).catch(function(e){window.__err=String(e);window.__done=2;});
                  }catch(e){window.__err=String(e);window.__done=2;}}
                  if(window.mermaid){go();}else{window.addEventListener('load',function(){window.mermaid?go():(window.__done=2);});}
                </script></body></html>
                """.Replace("__MERMAID_SRC__", encoded);

            await NavigateAndWaitAsync(wbMermaidHost, html, timeoutMs: 8000);

            for (var i = 0; i < 160; i++)
            {
                var done = await core.ExecuteScriptAsync("window.__done");
                if (done == "1")
                {
                    break;
                }

                if (done == "2")
                {
                    return null;
                }

                await Task.Delay(50);
            }

            var dim = await core.ExecuteScriptAsync(
                "(function(){var s=document.querySelector('#d svg');if(!s)return '0x0';var r=s.getBoundingClientRect();return Math.ceil(r.width)+'x'+Math.ceil(r.height);})()");
            var parts = (dim ?? "\"0x0\"").Trim('"').Split('x');
            if (parts.Length != 2
                || !double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w)
                || !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h)
                || w < 1 || h < 1)
            {
                return null;
            }

            wbMermaidHost.Width = Math.Min(w + 8, 2400);
            wbMermaidHost.Height = Math.Min(h + 8, 2400);
            wbMermaidHost.UpdateLayout();
            await Task.Delay(120);

            var devToolsPng = await TryCapturePngViaDevToolsAsync(core);
            if (devToolsPng is { Length: > 0 })
            {
                return devToolsPng;
            }

            using var ms = new MemoryStream();
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning("WPF Mermaid renderer failed.", ex);
            return null;
        }
        finally
        {
            wbMermaidHost.Width = 10;
            wbMermaidHost.Height = 10;
            _mermaidLock.Release();
        }
    }

    private static async Task<byte[]?> TryCapturePngViaDevToolsAsync(CoreWebView2 core)
    {
        try
        {
            const string options = "{\"format\":\"png\",\"fromSurface\":true}";
            var json = await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", options);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var dataProperty))
            {
                return null;
            }

            var base64 = dataProperty.GetString();
            return string.IsNullOrWhiteSpace(base64) ? null : Convert.FromBase64String(base64);
        }
        catch
        {
            return null;
        }
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is WorkspaceViewModel vm && e.PropertyName == nameof(WorkspaceViewModel.CurrentPageHtml))
        {
            RenderPreview(vm);
        }
    }

    private void HookAiCenter()
    {
        if (_aiCenter != null)
        {
            _aiCenter.ResultReady -= OnAiResultReady;
            _aiCenter = null;
        }

        if (Window.GetWindow(this)?.DataContext is MainViewModel mainVm)
        {
            _aiCenter = mainVm.AiCenter;
            _aiCenter.ResultReady += OnAiResultReady;
        }
    }

    private async void OnAiResultReady(object? sender, AiResultReadyEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm)
        {
            return;
        }

        try
        {
            var dialog = new AiResultDialog(e.SuggestedTitle, e.MarkdownContent)
            {
                Owner = Window.GetWindow(this)
            };

            _ = dialog.ShowDialog();
            if (dialog.Action == AiResultDialogAction.AddAsPreviewPage)
            {
                await vm.AddGeneratedPageToPreviewAsync(e.SuggestedTitle, dialog.MarkdownContent);
            }
        }
        catch (Exception ex)
        {
            vm.SetExternalStatus(string.Format(AppText.S("wpf.ai.status.error", "Error: {0}"), ex.Message));
        }
    }

    private void RenderPreview(WorkspaceViewModel vm)
    {
        _ = RenderPreviewAsync(vm.CurrentPageHtml);
    }

    private async Task RenderPreviewAsync(string htmlContent)
    {
        try
        {
            await wbPreview.EnsureCoreWebView2Async();
            ConfigureVirtualHostMapping(wbPreview.CoreWebView2);
            wbPreview.CoreWebView2.Settings.IsStatusBarEnabled = false;
            wbPreview.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            wbPreview.CoreWebView2.Settings.AreDevToolsEnabled = false;
            wbPreview.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;

            await EnsurePreviewHostAsync();
            await SetPreviewContentAsync(htmlContent);
        }
        catch
        {
            // Keep preview resilient while migration is in progress.
        }
    }

    private async Task EnsurePreviewHostAsync()
    {
        if (_previewHostLoaded || wbPreview.CoreWebView2 == null)
        {
            return;
        }

        var hostHtml = """
                       <!doctype html>
                       <html>
                       <head>
                         <meta charset="utf-8" />
                         <style>
                           html, body { margin:0; padding:0; width:100%; height:100%; background:#ffffff; overflow:hidden; }
                           #previewFrame { border:0; width:100%; height:100%; display:block; background:#ffffff; }
                         </style>
                       </head>
                       <body>
                         <iframe id="previewFrame"></iframe>
                         <script>
                           window.__awikiSetPreview = function (base64Utf8Html) {
                             try {
                               const binary = atob(base64Utf8Html || "");
                               const bytes = new Uint8Array(binary.length);
                               for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
                               const html = new TextDecoder("utf-8").decode(bytes);
                               const frame = document.getElementById("previewFrame");
                               if (!frame) return;
                               frame.srcdoc = html;
                             } catch (_) { }
                           };
                         </script>
                       </body>
                       </html>
                       """;

        var tcs = new TaskCompletionSource<bool>();
        void Handler(object? _, CoreWebView2NavigationCompletedEventArgs __)
        {
            wbPreview.NavigationCompleted -= Handler;
            tcs.TrySetResult(true);
        }

        wbPreview.NavigationCompleted += Handler;
        wbPreview.NavigateToString(hostHtml);
        await tcs.Task;
        _previewHostLoaded = true;
    }

    private async Task SetPreviewContentAsync(string htmlContent)
    {
        if (wbPreview.CoreWebView2 == null)
        {
            return;
        }

        var html = string.IsNullOrWhiteSpace(htmlContent) ? "<html><body></body></html>" : htmlContent;
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(html));
        var script = $"window.__awikiSetPreview && window.__awikiSetPreview('{base64}');";
        await wbPreview.ExecuteScriptAsync(script);
    }

    private void BtnAiSummary_Click(object sender, RoutedEventArgs e) => OpenScopeMenu(btnAiSummary, AiOperationType.Summary);
    private void BtnAiIndex_Click(object sender, RoutedEventArgs e) => OpenScopeMenu(btnAiIndex, AiOperationType.Index);
    private void BtnAiQuiz_Click(object sender, RoutedEventArgs e) => OpenScopeMenu(btnAiQuiz, AiOperationType.Quiz);
    private void BtnAiAsk_Click(object sender, RoutedEventArgs e) => OpenScopeMenu(btnAiAsk, AiOperationType.Answer);

    private void OpenScopeMenu(Button button, AiOperationType operationType)
    {
        if (Window.GetWindow(this)?.DataContext is not MainViewModel mainVm)
        {
            return;
        }

        var ai = mainVm.AiCenter;
        if (!ai.CanRunAiActions)
        {
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = button,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top
        };

        var current = new MenuItem { Header = ai.ScopeCurrentText };
        var all = new MenuItem { Header = ai.ScopeAllSingleText };

        current.Click += (_, _) => ExecuteScopeCommand(ai, operationType, currentScope: true);
        all.Click += (_, _) => ExecuteScopeCommand(ai, operationType, currentScope: false);

        menu.Items.Add(current);
        menu.Items.Add(all);
        menu.IsOpen = true;
    }

    private static void ExecuteScopeCommand(AiCenterViewModel ai, AiOperationType type, bool currentScope)
    {
        var command = type switch
        {
            AiOperationType.Summary => currentScope ? ai.GenerateSummaryCurrentCommand : ai.GenerateSummaryAllCommand,
            AiOperationType.Index => currentScope ? ai.GenerateIndexCurrentCommand : ai.GenerateIndexAllCommand,
            AiOperationType.Quiz => currentScope ? ai.GenerateQuizCurrentCommand : ai.GenerateQuizAllCommand,
            AiOperationType.Answer => currentScope ? ai.AskQuestionCurrentCommand : ai.AskQuestionAllCommand,
            _ => null
        };

        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
        }
    }

    private void ConfigureVirtualHostMapping(CoreWebView2? webView)
    {
        if (webView == null)
        {
            return;
        }

        var startupPath = AppDomain.CurrentDomain.BaseDirectory;
        var stylePath = Path.Combine(startupPath, "style");
        var imagePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExportAzureWiki",
            "Cache",
            "WikiImages");

        if (Directory.Exists(stylePath))
        {
            webView.SetVirtualHostNameToFolderMapping(
                LocalAssetsHost,
                stylePath,
                CoreWebView2HostResourceAccessKind.Allow);
        }

        // Images are encrypted at rest, so a plain folder mapping would serve
        // ciphertext to WebView2. Intercept requests to the images host and
        // decrypt each file in memory instead.
        WireImageDecryptionInterceptor(webView, imagePath);
    }

    private void WireImageDecryptionInterceptor(CoreWebView2 webView, string imageRoot)
    {
        if (!_imageInterceptorCores.Add(webView))
        {
            return;
        }

        var rootFullPath = Path.GetFullPath(imageRoot);
        webView.AddWebResourceRequestedFilter(
            $"https://{LocalImagesHost}/*", CoreWebView2WebResourceContext.All);
        webView.WebResourceRequested += (_, args) =>
        {
            try
            {
                var uri = args.Request.Uri ?? string.Empty;
                if (!uri.StartsWith($"https://{LocalImagesHost}/", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var relative = Uri.UnescapeDataString(new Uri(uri).AbsolutePath.TrimStart('/'))
                    .Replace('/', Path.DirectorySeparatorChar);
                var file = Path.GetFullPath(Path.Combine(rootFullPath, relative));

                // Refuse anything that escapes the cache root (path traversal).
                if (!file.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
                {
                    args.Response = webView.Environment.CreateWebResourceResponse(
                        null, 404, "Not Found", string.Empty);
                    return;
                }

                var stream = new MemoryStream(SecureCacheFile.ReadBytes(file));
                args.Response = webView.Environment.CreateWebResourceResponse(
                    stream, 200, "OK", $"Content-Type: {ImageContentType(file)}");
            }
            catch
            {
                // Leave the response unset so WebView2 falls back to its default
                // handling rather than surfacing a hard failure in the preview.
            }
        };
    }

    private static string ImageContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".bmp" => "image/bmp",
        ".ico" => "image/x-icon",
        ".tif" or ".tiff" => "image/tiff",
        _ => "application/octet-stream"
    };

    private async Task<bool> PrintHtmlToPdfAsync(string htmlContent, string outputPdfPath)
    {
        if (string.IsNullOrWhiteSpace(htmlContent) || string.IsNullOrWhiteSpace(outputPdfPath))
        {
            return false;
        }

        await _pdfPrintLock.WaitAsync();
        try
        {
            await wbPrintHost.EnsureCoreWebView2Async();
            var core = wbPrintHost.CoreWebView2;
            if (core == null)
            {
                return false;
            }

            ConfigureVirtualHostMapping(core);
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;

            // Offline export: block any external http(s) fetch during printing so
            // the PDF is produced without touching the network. Local virtual
            // hosts (assets/images) and non-http schemes are allowed.
            var offline = (DataContext as WorkspaceViewModel)?.OfflineExport ?? false;
            EventHandler<CoreWebView2WebResourceRequestedEventArgs>? offlineBlocker = null;
            if (offline)
            {
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                offlineBlocker = (_, args) =>
                {
                    var uri = args.Request.Uri ?? string.Empty;
                    var isHttp = uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                                 || uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                    var isLocalHost = uri.Contains(LocalAssetsHost, StringComparison.OrdinalIgnoreCase)
                                      || uri.Contains(LocalImagesHost, StringComparison.OrdinalIgnoreCase);
                    if (isHttp && !isLocalHost)
                    {
                        args.Response = core.Environment.CreateWebResourceResponse(null, 504, "Offline", string.Empty);
                    }
                };
                core.WebResourceRequested += offlineBlocker;
            }

            try
            {
                var htmlForPrint = SanitizeHtmlForWebViewScripts(htmlContent);
                await NavigateAndWaitAsync(wbPrintHost, htmlForPrint);
                await Task.Delay(350);

                var outputDir = Path.GetDirectoryName(outputPdfPath);
                if (!string.IsNullOrWhiteSpace(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                var printSettings = core.Environment.CreatePrintSettings();
                printSettings.ShouldPrintBackgrounds = true;
                printSettings.ShouldPrintHeaderAndFooter = false;
                printSettings.ScaleFactor = 1.0;

                return await core.PrintToPdfAsync(outputPdfPath, printSettings);
            }
            finally
            {
                if (offlineBlocker != null)
                {
                    core.WebResourceRequested -= offlineBlocker;
                }
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            _pdfPrintLock.Release();
        }
    }

    private static async Task NavigateAndWaitAsync(WebView2 webView, string htmlContent, int timeoutMs = 120000)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            webView.NavigationCompleted -= Handler;
            tcs.TrySetResult(args.IsSuccess);
        }

        webView.NavigationCompleted += Handler;
        webView.NavigateToString(htmlContent);

        var timeoutTask = Task.Delay(timeoutMs);
        var completed = await Task.WhenAny(tcs.Task, timeoutTask);
        if (completed == timeoutTask)
        {
            webView.NavigationCompleted -= Handler;
            throw new TimeoutException("WebView2 navigation timed out during PDF export.");
        }

        await tcs.Task;
    }

    private static string SanitizeHtmlForWebViewScripts(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return html;
        }

        return CspMetaRegex.Replace(html, string.Empty);
    }
}

internal enum AiOperationType
{
    Summary = 0,
    Index = 1,
    Quiz = 2,
    Answer = 3
}
