using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace ExportAzureWiki.Services;

public static class ExportChromiumPipelineService
{
    private const string MathJaxCdnUrl = "https://cdn.jsdelivr.net/npm/mathjax@3/es5/tex-svg.js";
    private static readonly SemaphoreSlim EngineLock = new(1, 1);
    private static readonly SemaphoreSlim OperationLock = new(1, 1);
    private static IPlaywright? _playwright;
    private static IBrowser? _browser;
    private static IBrowserContext? _context;
    private static int _warmupRequested;

    public static void WarmupInBackground()
    {
        if (Interlocked.Exchange(ref _warmupRequested, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await EnsureEngineReadyAsync();
                LoggingService.LogInfo("Chromium pipeline warmup completed.");
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("Chromium pipeline warmup failed.", ex);
            }
        });
    }

    public static async Task<string> TryProcessCombinedHtmlAsync(string html)
    {
        if (string.IsNullOrWhiteSpace(html) ||
            (!Regex.IsMatch(html, "<pre\\b", RegexOptions.IgnoreCase) && !HasMathFormula(html)))
        {
            return html;
        }

        await OperationLock.WaitAsync();
        try
        {
            await using var lease = await CreatePageLeaseAsync(html, TimeSpan.FromSeconds(45));
            await InjectLocalHighlightJsAsync(lease.Page);
            await EnsureMathJaxAvailableAsync(lease.Page);

            const string script = """
                                  async () => {
                                      const normalizeLanguage = (className) => {
                                          if (!className) return null;
                                          const tokens = String(className).split(/\s+/).map(x => x.trim().toLowerCase()).filter(Boolean);
                                          const langToken = tokens.find(t => t.startsWith('language-'));
                                          const raw = langToken ? langToken.replace('language-', '') : null;
                                          if (!raw) return null;
                                          const aliases = { py: 'python', csharp: 'cs', dotnet: 'cs', yml: 'yaml', ps1: 'powershell', shell: 'bash' };
                                          return aliases[raw] || raw;
                                      };

                                      if (window.hljs) {
                                          const preBlocks = Array.from(document.querySelectorAll('pre'));
                                          for (const pre of preBlocks) {
                                              let code = pre.querySelector('code');
                                              if (!code) {
                                                  code = document.createElement('code');
                                                  code.textContent = pre.textContent || '';
                                                  pre.innerHTML = '';
                                                  pre.appendChild(code);
                                              }

                                              if (code.querySelector('span[class*="hljs-"]')) {
                                                  continue;
                                              }

                                              const source = code.textContent || '';
                                              if (!source.trim()) {
                                                  continue;
                                              }

                                              const lang = normalizeLanguage(code.className || '');
                                              let result = null;

                                              if (lang &&
                                                  typeof window.hljs.highlight === 'function' &&
                                                  typeof window.hljs.getLanguage === 'function' &&
                                                  window.hljs.getLanguage(lang)) {
                                                  try {
                                                      result = window.hljs.highlight(source, { language: lang, ignoreIllegals: true });
                                                  } catch (_) { }
                                              }

                                              if ((!result || !result.value) && typeof window.hljs.highlightElement === 'function') {
                                                  try {
                                                      code.textContent = source;
                                                      window.hljs.highlightElement(code);
                                                  } catch (_) { }
                                              }

                                              if ((!result || !result.value) && typeof window.hljs.highlightAuto === 'function') {
                                                  try {
                                                      result = window.hljs.highlightAuto(source);
                                                  } catch (_) { }
                                              }

                                              if (result && result.value) {
                                                  code.innerHTML = result.value;
                                                  code.classList.add('hljs');
                                                  if (result.language && !code.classList.contains(`language-${result.language}`)) {
                                                      code.classList.add(`language-${result.language}`);
                                                  }
                                              }
                                          }

                                          const spans = Array.from(document.querySelectorAll('pre code span[class*="hljs-"]'));
                                          for (const span of spans) {
                                              try {
                                                  const computed = window.getComputedStyle(span);
                                                  if (!computed) continue;
                                                  if (computed.color) span.style.color = computed.color;
                                                  if (computed.fontWeight) span.style.fontWeight = computed.fontWeight;
                                                  if (computed.fontStyle) span.style.fontStyle = computed.fontStyle;
                                              } catch (_) { }
                                          }
                                      }

                                      const hasMathHint = () => {
                                          try {
                                              const bodyText = (document.body && document.body.innerText) || '';
                                              return /\\\(|\\\[|\$\$/.test(bodyText) ||
                                                  !!document.querySelector('span.math, div.math, mjx-container');
                                          } catch (_) {
                                              return false;
                                          }
                                      };

                                      const normalizeMathContainers = () => {
                                          try {
                                              const spans = Array.from(document.querySelectorAll('span.math'));
                                              for (const span of spans) {
                                                  if (!span || span.querySelector('mjx-container')) continue;
                                                  const raw = (span.textContent || '').trim();
                                                  if (!raw) continue;
                                                  const host = document.createElement('span');
                                                  host.textContent = raw;
                                                  span.replaceWith(host);
                                              }

                                              const divs = Array.from(document.querySelectorAll('div.math'));
                                              for (const div of divs) {
                                                  if (!div || div.querySelector('mjx-container')) continue;
                                                  const raw = (div.textContent || '').trim();
                                                  if (!raw) continue;
                                                  const host = document.createElement('div');
                                                  host.textContent = raw;
                                                  div.replaceWith(host);
                                              }
                                          } catch (_) { }
                                      };

                                      if (hasMathHint() &&
                                          window.MathJax &&
                                          typeof window.MathJax.typesetPromise === 'function') {
                                          try {
                                              normalizeMathContainers();
                                              await window.MathJax.typesetPromise();
                                          } catch (_) { }
                                      }

                                      return document.documentElement.outerHTML;
                                  }
                                  """;

            var processed = await lease.Page.EvaluateAsync<string>(script);
            return string.IsNullOrWhiteSpace(processed) ? html : processed;
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning("Chromium pipeline (highlight) failed.", ex);
            return html;
        }
        finally
        {
            OperationLock.Release();
        }
    }

    public static async Task<string> TryRenderMermaidAndInlineImagesAsync(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return html;
        }

        var hasMermaid = Regex.IsMatch(html, "language-mermaid|class\\s*=\\s*['\\\"]mermaid['\\\"]", RegexOptions.IgnoreCase);
        var hasRemoteImages = Regex.IsMatch(html, "<img[^>]+src\\s*=\\s*['\\\"]https?://", RegexOptions.IgnoreCase);
        var hasMathFormula = HasMathFormula(html);
        if (!hasMermaid && !hasRemoteImages && !hasMathFormula)
        {
            return html;
        }

        await OperationLock.WaitAsync();
        try
        {
            await using var lease = await CreatePageLeaseAsync(html, TimeSpan.FromSeconds(60));
            await InjectLocalHighlightJsAsync(lease.Page);
            await EnsureMathJaxAvailableAsync(lease.Page);

            const string script = """
                                  async () => {
                                      const wait = (ms) => new Promise(resolve => setTimeout(resolve, ms));
                                      const waitFor = async (predicate, timeoutMs = 12000, intervalMs = 120) => {
                                          const start = Date.now();
                                          while ((Date.now() - start) < timeoutMs) {
                                              try {
                                                  if (predicate()) return true;
                                              } catch (_) { }
                                              await wait(intervalMs);
                                          }
                                          return false;
                                      };

                                      const ensureMermaidLoaded = async () => {
                                          if (window.mermaid) return true;
                                          const sources = [
                                              'https://cdn.jsdelivr.net/npm/mermaid@10/dist/mermaid.min.js',
                                              'https://unpkg.com/mermaid@10/dist/mermaid.min.js'
                                          ];
                                          for (let i = 0; i < sources.length && !window.mermaid; i++) {
                                              const scriptEl = document.createElement('script');
                                              scriptEl.src = sources[i];
                                              scriptEl.async = true;
                                              document.head.appendChild(scriptEl);
                                              if (await waitFor(() => !!window.mermaid, 7000, 120)) {
                                                  return true;
                                              }
                                          }
                                          return !!window.mermaid;
                                      };

                                      const normalizeMermaidSources = () => {
                                          const codeBlocks = Array.from(document.querySelectorAll('pre > code'));
                                          for (const code of codeBlocks) {
                                              const cls = (code.getAttribute('class') || '').toLowerCase();
                                              if (!cls.includes('language-mermaid')) continue;
                                              const pre = code.parentElement;
                                              if (!pre) continue;
                                              const div = document.createElement('div');
                                              div.className = 'mermaid';
                                              div.textContent = (code.textContent || '').trim();
                                              pre.replaceWith(div);
                                          }
                                      };

                                      const toPngDataUrl = (svg) => new Promise((resolve) => {
                                          try {
                                              const xml = new XMLSerializer().serializeToString(svg);
                                              const blob = new Blob([xml], { type: 'image/svg+xml;charset=utf-8' });
                                              const url = URL.createObjectURL(blob);
                                              const img = new Image();
                                              img.onload = () => {
                                                  try {
                                                      const w = Math.max(1, Math.ceil(svg.getBoundingClientRect().width || svg.clientWidth || 1200));
                                                      const h = Math.max(1, Math.ceil(svg.getBoundingClientRect().height || svg.clientHeight || 600));
                                                      const canvas = document.createElement('canvas');
                                                      canvas.width = w;
                                                      canvas.height = h;
                                                      const ctx = canvas.getContext('2d');
                                                      if (!ctx) {
                                                          resolve(null);
                                                          return;
                                                      }
                                                      ctx.fillStyle = '#ffffff';
                                                      ctx.fillRect(0, 0, w, h);
                                                      ctx.drawImage(img, 0, 0, w, h);
                                                      resolve(canvas.toDataURL('image/png'));
                                                  } catch (_) {
                                                      resolve(null);
                                                  } finally {
                                                      URL.revokeObjectURL(url);
                                                  }
                                              };
                                              img.onerror = () => {
                                                  URL.revokeObjectURL(url);
                                                  resolve(null);
                                              };
                                              img.src = url;
                                          } catch (_) {
                                              resolve(null);
                                          }
                                      });

                                      const fetchImageAsDataUrl = async (url) => {
                                          try {
                                              const response = await fetch(url, { mode: 'cors', cache: 'no-store', credentials: 'include' });
                                              if (!response.ok) return null;
                                              const blob = await response.blob();
                                              return await new Promise((resolve) => {
                                                  const objectUrl = URL.createObjectURL(blob);
                                                  const img = new Image();
                                                  img.onload = () => {
                                                      try {
                                                          const w = Math.max(1, img.naturalWidth || img.width || 1);
                                                          const h = Math.max(1, img.naturalHeight || img.height || 1);
                                                          const canvas = document.createElement('canvas');
                                                          canvas.width = w;
                                                          canvas.height = h;
                                                          const ctx = canvas.getContext('2d');
                                                          if (!ctx) {
                                                              resolve(null);
                                                              return;
                                                          }
                                                          ctx.drawImage(img, 0, 0, w, h);
                                                          resolve(canvas.toDataURL('image/png'));
                                                      } catch (_) {
                                                          resolve(null);
                                                      } finally {
                                                          URL.revokeObjectURL(objectUrl);
                                                      }
                                                  };
                                                  img.onerror = () => {
                                                      URL.revokeObjectURL(objectUrl);
                                                      resolve(null);
                                                  };
                                                  img.src = objectUrl;
                                              });
                                          } catch (_) {
                                              return null;
                                          }
                                      };

                                      const normalizeExternalUrl = (src) => {
                                          if (!src) return '';
                                          const trimmed = src.trim();
                                          if (trimmed.startsWith('//')) return `${window.location.protocol}${trimmed}`;
                                          if (trimmed.startsWith('http://') || trimmed.startsWith('https://') || trimmed.startsWith('data:')) return trimmed;
                                          try { return new URL(trimmed, window.location.href).href; } catch (_) { return trimmed; }
                                      };

                                      const hasMathHint = () => {
                                          try {
                                              const bodyText = (document.body && document.body.innerText) || '';
                                              return /\\\(|\\\[|\$\$/.test(bodyText) ||
                                                  !!document.querySelector('span.math, div.math, mjx-container');
                                          } catch (_) {
                                              return false;
                                          }
                                      };

                                      const normalizeMathContainers = () => {
                                          try {
                                              const spans = Array.from(document.querySelectorAll('span.math'));
                                              for (const span of spans) {
                                                  if (!span || span.querySelector('mjx-container')) continue;
                                                  const raw = (span.textContent || '').trim();
                                                  if (!raw) continue;
                                                  const host = document.createElement('span');
                                                  host.textContent = raw;
                                                  span.replaceWith(host);
                                              }

                                              const divs = Array.from(document.querySelectorAll('div.math'));
                                              for (const div of divs) {
                                                  if (!div || div.querySelector('mjx-container')) continue;
                                                  const raw = (div.textContent || '').trim();
                                                  if (!raw) continue;
                                                  const host = document.createElement('div');
                                                  host.textContent = raw;
                                                  div.replaceWith(host);
                                              }
                                          } catch (_) { }
                                      };

                                      const inlineExternalImages = async () => {
                                          const images = Array.from(document.querySelectorAll('img'));
                                          for (const img of images) {
                                              const rawSrc = img.getAttribute('src') || img.currentSrc || '';
                                              const src = normalizeExternalUrl(rawSrc);
                                              if (!src || src.toLowerCase().startsWith('data:image/')) continue;
                                              if (!(src.toLowerCase().startsWith('http://') || src.toLowerCase().startsWith('https://'))) continue;
                                              const dataUrl = await fetchImageAsDataUrl(src);
                                              if (dataUrl) {
                                                  img.setAttribute('src', dataUrl);
                                              }
                                          }
                                      };

                                      normalizeMermaidSources();
                                      const loaded = await ensureMermaidLoaded();
                                      if (loaded && window.mermaid) {
                                          try {
                                              if (typeof window.mermaid.initialize === 'function') {
                                                  window.mermaid.initialize({
                                                      startOnLoad: false,
                                                      theme: 'default',
                                                      securityLevel: 'loose',
                                                      flowchart: { htmlLabels: true }
                                                  });
                                              }
                                              const nodes = Array.from(document.querySelectorAll('.mermaid'));
                                              if (typeof window.mermaid.run === 'function') {
                                                  await window.mermaid.run({ nodes });
                                              } else if (typeof window.mermaid.init === 'function') {
                                                  window.mermaid.init(undefined, nodes);
                                              }
                                              await waitFor(() => Array.from(document.querySelectorAll('.mermaid')).every(x => !!x.querySelector('svg')), 10000, 100);
                                          } catch (_) { }
                                      }

                                      const mermaids = Array.from(document.querySelectorAll('.mermaid'));
                                      for (let i = 0; i < mermaids.length; i++) {
                                          const node = mermaids[i];
                                          const svg = node.querySelector('svg');
                                          if (!svg) continue;
                                          const png = await toPngDataUrl(svg);
                                          if (!png) continue;
                                          const img = document.createElement('img');
                                          img.src = png;
                                          img.alt = 'Mermaid diagram';
                                          img.style.maxWidth = '100%';
                                          img.style.height = 'auto';
                                          node.replaceWith(img);
                                      }

                                      await inlineExternalImages();

                                      if (hasMathHint() &&
                                          window.MathJax &&
                                          typeof window.MathJax.typesetPromise === 'function') {
                                          try {
                                              normalizeMathContainers();
                                              await window.MathJax.typesetPromise();
                                          } catch (_) { }
                                      }

                                      return document.documentElement.outerHTML;
                                  }
                                  """;

            var processed = await lease.Page.EvaluateAsync<string>(script);
            return string.IsNullOrWhiteSpace(processed) ? html : processed;
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning("Chromium pipeline (mermaid/images) failed.", ex);
            return html;
        }
        finally
        {
            OperationLock.Release();
        }
    }

    private static async Task InjectLocalHighlightJsAsync(IPage page)
    {
        var localScriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "style", "highlight.min.js");
        if (!File.Exists(localScriptPath))
        {
            LoggingService.LogWarning($"Chromium pipeline: local highlight.js not found at '{localScriptPath}'.");
            return;
        }

        try
        {
            await page.AddScriptTagAsync(new PageAddScriptTagOptions { Path = localScriptPath });
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning("Chromium pipeline: failed to inject local highlight.js.", ex);
        }
    }

    private static bool HasMathFormula(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return false;
        }

        return Regex.IsMatch(
            html,
            @"\\\(|\\\[|\$\$|<span[^>]*\bclass\s*=\s*['""][^'""]*\bmath\b|<div[^>]*\bclass\s*=\s*['""][^'""]*\bmath\b",
            RegexOptions.IgnoreCase);
    }

    private static async Task EnsureMathJaxAvailableAsync(IPage page)
    {
        try
        {
            var alreadyLoaded = await page.EvaluateAsync<bool>(
                "() => !!window.MathJax && typeof window.MathJax.typesetPromise === 'function'");
            if (alreadyLoaded)
            {
                return;
            }

            await page.EvaluateAsync(
                """
                () => {
                    if (!window.MathJax) {
                        window.MathJax = {
                            tex: {
                                inlineMath: [['\\(', '\\)']],
                                displayMath: [['$$', '$$'], ['\\[', '\\]']]
                            },
                            svg: { fontCache: 'global' }
                        };
                    }
                }
                """);

            var localScriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "style", "vendor", "mathjax", "tex-svg.js");
            if (File.Exists(localScriptPath))
            {
                await page.AddScriptTagAsync(new PageAddScriptTagOptions { Path = localScriptPath });
            }
            else
            {
                await page.AddScriptTagAsync(new PageAddScriptTagOptions { Url = MathJaxCdnUrl });
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning("Chromium pipeline: failed to inject MathJax.", ex);
        }
    }

    public static async Task<string?> RenderMathFormulaMathMlAsync(string formulaLatex, bool displayMode)
    {
        if (string.IsNullOrWhiteSpace(formulaLatex))
        {
            return null;
        }

        var tex = NormalizeTexExpression(formulaLatex.Trim());
        if (string.IsNullOrWhiteSpace(tex))
        {
            return null;
        }

        var html = """
                   <!doctype html>
                   <html>
                   <head><meta charset="utf-8" /></head>
                   <body><div id="host"></div></body>
                   </html>
                   """;

        await OperationLock.WaitAsync();
        try
        {
            await using var lease = await CreatePageLeaseAsync(html, TimeSpan.FromSeconds(30));
            await EnsureMathJaxAvailableAsync(lease.Page);

            const string script = """
                                  async (args) => {
                                      const tex = String((args && args.tex) || '');
                                      const display = !!(args && args.display);
                                      if (!tex || !window.MathJax) return null;
                                      try {
                                          if (typeof window.MathJax.tex2mmlPromise === 'function') {
                                              return await window.MathJax.tex2mmlPromise(tex, { display: display });
                                          }
                                          if (typeof window.MathJax.tex2mml === 'function') {
                                              return window.MathJax.tex2mml(tex, { display: display });
                                          }
                                      } catch (_) {
                                          return null;
                                      }
                                      return null;
                                  }
                                  """;

            var mathMl = await lease.Page.EvaluateAsync<string?>(script, new { tex, display = displayMode });
            if (string.IsNullOrWhiteSpace(mathMl))
            {
                return null;
            }

            return mathMl;
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning("Chromium pipeline (render mathml) failed.", ex);
            return null;
        }
        finally
        {
            OperationLock.Release();
        }
    }

    public static async Task<(string? dataUrl, int naturalWidth, int naturalHeight)> RenderMathFormulaDataUrlAsync(string formulaLatex, bool displayMode)
    {
        if (string.IsNullOrWhiteSpace(formulaLatex))
        {
            return (null, 0, 0);
        }

        // Build a minimal isolated page to avoid mutating the source document DOM (and Mermaid nodes).
        var wrapped = formulaLatex.Trim();
        var tex = NormalizeTexExpression(wrapped);
        var html = $$"""
                     <!doctype html>
                     <html>
                     <head>
                       <meta charset="utf-8" />
                       <style>
                         html, body { margin: 0; padding: 0; background: #ffffff; }
                         body { font-family: Cambria, "Times New Roman", serif; }
                         #host { display: {{(displayMode ? "block" : "inline-block")}}; }
                       </style>
                     </head>
                     <body>
                       <span id="host"></span>
                     </body>
                     </html>
                     """;

        await OperationLock.WaitAsync();
        try
        {
            await using var lease = await CreatePageLeaseAsync(html, TimeSpan.FromSeconds(30));
            await EnsureMathJaxAvailableAsync(lease.Page);

            const string script = """
                                  async (args) => {
                                      const displayMode = !!(args && args.displayMode);
                                      const tex = String((args && args.tex) || '');
                                      if (!window.MathJax) {
                                          return { naturalWidth: 0, naturalHeight: 0 };
                                      }

                                      const host = document.getElementById('host');
                                      if (!host) {
                                          return { naturalWidth: 0, naturalHeight: 0 };
                                      }

                                      host.style.color = '#111111';
                                      host.style.background = 'transparent';
                                      host.style.display = displayMode ? 'block' : 'inline-block';
                                      host.style.whiteSpace = 'nowrap';

                                      if (!tex.trim()) {
                                          return { naturalWidth: 0, naturalHeight: 0 };
                                      }

                                      host.innerHTML = '';
                                      try {
                                          if (typeof window.MathJax.tex2svgPromise === 'function') {
                                              const node = await window.MathJax.tex2svgPromise(tex, { display: !!displayMode });
                                              host.appendChild(node);
                                          } else if (typeof window.MathJax.typesetPromise === 'function') {
                                              // Fallback path
                                              host.textContent = displayMode ? `\\[${tex}\\]` : `\\(${tex}\\)`;
                                              await window.MathJax.typesetPromise([host]);
                                          } else {
                                              return { naturalWidth: 0, naturalHeight: 0 };
                                          }
                                      } catch (_) {
                                          return { naturalWidth: 0, naturalHeight: 0 };
                                      }

                                      const container = host.querySelector('mjx-container');
                                      if (!container) {
                                          return { naturalWidth: 0, naturalHeight: 0 };
                                      }
                                      const target = container;
                                      const rect = target.getBoundingClientRect();
                                      const naturalWidth = Math.max(1, Math.ceil(rect.width || 1));
                                      const naturalHeight = Math.max(1, Math.ceil(rect.height || 1));
                                      return { naturalWidth, naturalHeight };
                                  }
                                  """;

            var result = await lease.Page.EvaluateAsync<MathRenderResult>(script, new { displayMode, tex });
            var naturalWidth = Math.Max(1, result?.NaturalWidth ?? 0);
            var naturalHeight = Math.Max(1, result?.NaturalHeight ?? 0);
            if (naturalWidth <= 1 || naturalHeight <= 1)
            {
                return (null, 0, 0);
            }

            var targetHeight = displayMode
                ? Math.Max(24, Math.Min(120, naturalHeight))
                : 16;
            var ratio = naturalHeight > 0 ? (double)naturalWidth / naturalHeight : 1d;
            var targetWidth = Math.Max(
                8,
                Math.Min(displayMode ? 900 : 700, (int)Math.Round(targetHeight * ratio)));

            // Resize host for display math; keep inline math unconstrained to avoid clipping.
            await lease.Page.EvaluateAsync(
                """
                (args) => {
                    const host = document.getElementById('host');
                    if (!host) return;
                    host.style.display = args.display ? 'block' : 'inline-block';
                    host.style.whiteSpace = 'nowrap';
                    if (args.display) {
                        host.style.width = `${args.w}px`;
                        host.style.height = `${args.h}px`;
                        host.style.overflow = 'hidden';
                    } else {
                        host.style.width = 'auto';
                        host.style.height = 'auto';
                        host.style.overflow = 'visible';
                        host.style.padding = '0 2px';
                    }
                }
                """,
                new { w = targetWidth, h = targetHeight, display = displayMode });

            var hostLocator = lease.Page.Locator("#host");
            var bytes = await hostLocator.ScreenshotAsync(new LocatorScreenshotOptions
            {
                Type = ScreenshotType.Png,
                OmitBackground = false
            });
            if (bytes == null || bytes.Length == 0)
            {
                return (null, 0, 0);
            }

            var dataUrl = $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
            return (dataUrl, targetWidth, targetHeight);
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning("Chromium pipeline (render single math) failed.", ex);
            return (null, 0, 0);
        }
        finally
        {
            OperationLock.Release();
        }
    }

    private sealed class MathRenderResult
    {
        public int NaturalWidth { get; set; }
        public int NaturalHeight { get; set; }
    }

    private static string NormalizeTexExpression(string input)
    {
        var text = (input ?? string.Empty).Trim();
        if (text.StartsWith(@"\(") && text.EndsWith(@"\)") && text.Length > 4)
        {
            return text[2..^2].Trim();
        }

        if (text.StartsWith(@"\[") && text.EndsWith(@"\]") && text.Length > 4)
        {
            return text[2..^2].Trim();
        }

        if (text.StartsWith("$$", StringComparison.Ordinal) && text.EndsWith("$$", StringComparison.Ordinal) && text.Length > 4)
        {
            return text[2..^2].Trim();
        }

        if (text.StartsWith("$", StringComparison.Ordinal) && text.EndsWith("$", StringComparison.Ordinal) && text.Length > 2)
        {
            return text[1..^1].Trim();
        }

        return text;
    }

    private static async Task EnsureEngineReadyAsync()
    {
        if (_context != null)
        {
            return;
        }

        await EngineLock.WaitAsync();
        try
        {
            if (_context != null)
            {
                return;
            }

            _playwright = await Playwright.CreateAsync();
            BrowserTypeLaunchOptions launchOptions = new()
            {
                Headless = true,
                Channel = "msedge",
                Args = ["--disable-gpu", "--disable-dev-shm-usage", "--no-sandbox"]
            };

            _browser = await _playwright.Chromium.LaunchAsync(launchOptions);
            _context = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1400, Height = 2200 },
                IgnoreHTTPSErrors = true
            });
        }
        finally
        {
            EngineLock.Release();
        }
    }

    private static async Task<PageLease> CreatePageLeaseAsync(string html, TimeSpan timeout)
    {
        await EnsureEngineReadyAsync();
        if (_context == null)
        {
            throw new InvalidOperationException("Chromium context is not initialized.");
        }

        var page = await _context.NewPageAsync();
        page.SetDefaultTimeout((float)timeout.TotalMilliseconds);
        page.SetDefaultNavigationTimeout((float)timeout.TotalMilliseconds);

        await page.SetContentAsync(html, new PageSetContentOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = (float)timeout.TotalMilliseconds
        });

        return new PageLease(page);
    }

    private sealed class PageLease(IPage page) : IAsyncDisposable
    {
        public IPage Page { get; } = page;

        public async ValueTask DisposeAsync()
        {
            try { await Page.CloseAsync(); } catch { }
        }
    }
}
