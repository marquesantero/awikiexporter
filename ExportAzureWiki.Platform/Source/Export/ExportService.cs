using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using HtmlAgilityPack;
using HtmlToOpenXml;
using System.Text.RegularExpressions;
using System.Globalization;
using Color = DocumentFormat.OpenXml.Wordprocessing.Color;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using System.Text;
using System.Net;
using ExportAzureWiki.Interfaces;
using HtmlToOpenXmlConverter = HtmlToOpenXml.HtmlConverter;
using System.Net.Http;
using ExportAzureWiki.Localization;
using ExportAzureWiki.Services;

namespace ExportAzureWiki
{
    public partial class ExportService : IExportService
    {
        private void ProcessHtmlContent(HtmlNode node, MainDocumentPart mainPart)
        {
            var converter = new HtmlToOpenXmlConverter(mainPart);
            var accumulatedHtml = new StringBuilder();
            var mathFormulaNodes = 0;
            var mathOmmlApplied = 0;
            var mathFallbackToHtml = 0;
            var mathInlineImageApplied = 0;

            // Phase timers to locate the export bottleneck.
            var perfParseMs = 0L;
            var perfParseCalls = 0;
            var perfCodeMs = 0L;
            var perfCodeCalls = 0;
            var perfImageMs = 0L;
            var perfImageCalls = 0;
            var perfTimer = new System.Diagnostics.Stopwatch();

            void FlushAccumulatedHtml()
            {
                if (accumulatedHtml.Length <= 0) return;
                var htmlToProcess = accumulatedHtml.ToString();
                htmlToProcess = ProcessInlineCode(htmlToProcess);

                try
                {
                    perfTimer.Restart();
                    var paragraphs = converter.Parse(htmlToProcess);
                    perfParseMs += perfTimer.ElapsedMilliseconds;
                    perfParseCalls++;
                    foreach (var element in paragraphs)
                    {
                        if (element is Table table)
                        {
                            ApplyDefaultTableVisual(table);
                        }

                        mainPart.Document.Body?.AppendChild(element);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(LocalizationManager.Sf("export.word.log.html_convert_error", ex.Message));
                    var errorParagraph = new Paragraph(new Run(new Text(LocalizationManager.S("export.word.warning.partial_html_convert"))));
                    mainPart.Document.Body?.AppendChild(errorParagraph);
                }
                accumulatedHtml.Clear();
            }

            void TraverseNodes(HtmlNode currentNode)
            {
                if (ShouldInsertWordPageBreakBefore(currentNode))
                {
                    FlushAccumulatedHtml();
                    AppendWordPageBreak(mainPart);
                    currentNode.Attributes.Remove("data-word-page-break-before");
                }

                if (IsImageRowParagraph(currentNode))
                {
                    // A row of badges/images: render them inline in one paragraph
                    // instead of one block paragraph per image (which stacks them).
                    FlushAccumulatedHtml();
                    perfTimer.Restart();
                    ProcessImageRow(currentNode, mainPart);
                    perfImageMs += perfTimer.ElapsedMilliseconds;
                    perfImageCalls++;
                }
                else if (IsCodeBlock(currentNode))
                {
                    FlushAccumulatedHtml();
                    perfTimer.Restart();
                    ProcessCodeBlock(currentNode, mainPart);
                    perfCodeMs += perfTimer.ElapsedMilliseconds;
                    perfCodeCalls++;
                }
                else if (IsStyledContainerBlock(currentNode))
                {
                    FlushAccumulatedHtml();
                    ProcessStyledContainerBlock(currentNode, mainPart);
                }
                else if (currentNode.Name == "img" && ShouldProcessImageManually(currentNode))
                {
                    FlushAccumulatedHtml();
                    try
                    {
                        perfTimer.Restart();
                        ProcessImage(currentNode, mainPart);
                        perfImageMs += perfTimer.ElapsedMilliseconds;
                        perfImageCalls++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(LocalizationManager.Sf("export.word.log.image_process_error", ex.Message));
                        var errorParagraph = new Paragraph(new Run(new Text(LocalizationManager.Sf("export.word.image_load_error", currentNode.GetAttributeValue("src", "unknown")))));
                        mainPart.Document.Body?.AppendChild(errorParagraph);
                    }
                }
                else if (IsMathFormulaNode(currentNode))
                {
                    mathFormulaNodes++;
                    var classAttr = currentNode.GetAttributeValue("class", string.Empty);
                    var displayMode =
                        currentNode.Name.Equals("div", StringComparison.OrdinalIgnoreCase) ||
                        classAttr.Contains("display", StringComparison.OrdinalIgnoreCase);
                    var standaloneParagraphMath = IsStandaloneMathParagraph(currentNode);
                    var preferBlockMath = displayMode || standaloneParagraphMath;

                    if (!preferBlockMath && TryBuildInlineMathImageHtml(currentNode, out var inlineMathHtml))
                    {
                        mathInlineImageApplied++;
                        accumulatedHtml.Append(inlineMathHtml);
                    }
                    else
                    {
                        FlushAccumulatedHtml();
                        if (!TryAppendMathFormula(currentNode, mainPart, preferBlockMath))
                        {
                            mathFallbackToHtml++;
                            var fallbackText = ExtractMathRawText(currentNode);
                            if (!string.IsNullOrWhiteSpace(fallbackText))
                            {
                                mainPart.Document.Body?.AppendChild(new Paragraph(new Run(new Text(fallbackText))));
                            }
                            else
                            {
                                accumulatedHtml.Append(currentNode.OuterHtml);
                            }
                        }
                        else
                        {
                            mathOmmlApplied++;
                        }
                    }
                }
                else
                {
                    if (currentNode.HasChildNodes)
                    {
                        accumulatedHtml.Append($"<{currentNode.Name}{GetAttributesString(currentNode)}>");
                        foreach (var child in currentNode.ChildNodes)
                        {
                            TraverseNodes(child);
                        }
                        accumulatedHtml.Append($"</{currentNode.Name}>");
                    }
                    else
                    {
                        accumulatedHtml.Append(currentNode.OuterHtml);
                    }
                }
            }

            TraverseNodes(node);
            FlushAccumulatedHtml();
            LoggingService.LogInfo($"WORD_MATH_PIPELINE_SUMMARY: nodes={mathFormulaNodes}; ommlApplied={mathOmmlApplied}; inlineImgApplied={mathInlineImageApplied}; fallbackHtml={mathFallbackToHtml}");
            LoggingService.LogInfo($"WORD_PROCESS_PERF: htmlParse={perfParseMs}ms/{perfParseCalls}x; codeBlocks={perfCodeMs}ms/{perfCodeCalls}x; images={perfImageMs}ms/{perfImageCalls}x");
        }

        private static bool IsMathFormulaNode(HtmlNode node)
        {
            if (!node.Name.Equals("span", StringComparison.OrdinalIgnoreCase) &&
                !node.Name.Equals("div", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var classAttr = node.GetAttributeValue("class", string.Empty);
            return classAttr
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(c => c.Equals("math", StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryAppendMathFormula(HtmlNode node, MainDocumentPart mainPart, bool forceDisplayMode)
        {
            var classAttr = node.GetAttributeValue("class", string.Empty);
            var displayMode =
                forceDisplayMode ||
                node.Name.Equals("div", StringComparison.OrdinalIgnoreCase) ||
                classAttr.Contains("display", StringComparison.OrdinalIgnoreCase);
            var preferredAlignment = ResolveMathAlignment(node);

            var mathMlNode = node.SelectSingleNode(".//*[local-name()='math']");
            var ommlConverted =
                (mathMlNode != null &&
                 MathOmmlConverterService.TryConvertMathMlToOmml(mathMlNode.OuterHtml, out var mathMlOmml, preferredAlignment) &&
                 !string.IsNullOrWhiteSpace(mathMlOmml))
                ? mathMlOmml
                : null;

            if (string.IsNullOrWhiteSpace(ommlConverted))
            {
                var raw = ExtractMathRawText(node);

                if (string.IsNullOrWhiteSpace(raw) ||
                    !MathOmmlConverterService.TryConvertLatexToOmml(raw, displayMode, out ommlConverted, preferredAlignment) ||
                    string.IsNullOrWhiteSpace(ommlConverted))
                {
                    // Last resort: append readable plain text and consume this node to avoid HtmlToOpenXml warnings.
                    var fallbackText = string.IsNullOrWhiteSpace(raw) ? WebUtility.HtmlDecode(node.InnerText ?? string.Empty) : raw;
                    fallbackText = fallbackText?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(fallbackText))
                    {
                        mainPart.Document.Body?.AppendChild(new Paragraph(new Run(new Text(fallbackText))));
                        return true;
                    }

                    return false;
                }
            }

            try
            {
                var paragraph = new Paragraph();
                var normalizedAlignment = (preferredAlignment ?? "left").Trim().ToLowerInvariant();
                var shouldInlineDisplayMath = displayMode && normalizedAlignment is "left" or "right";
                paragraph.InnerXml = shouldInlineDisplayMath
                    ? $"<w:r>{ommlConverted}</w:r>"
                    : ommlConverted;
                if (displayMode)
                {
                    var pPr = paragraph.GetFirstChild<ParagraphProperties>();
                    if (pPr == null)
                    {
                        pPr = new ParagraphProperties();
                        paragraph.PrependChild(pPr);
                    }

                    var jc = pPr.GetFirstChild<Justification>();
                    if (jc == null)
                    {
                        jc = new Justification();
                        pPr.AppendChild(jc);
                    }

                    jc.Val = ToWordJustification(preferredAlignment);
                }

                if (displayMode && normalizedAlignment is "left" or "right")
                {
                    // Word may still center a paragraph that contains only math.
                    // Add a hidden text run so layout follows paragraph justification.
                    var hiddenRun = new Run(
                        new RunProperties(new Vanish()),
                        new Text("x"));
                    paragraph.InsertAt(hiddenRun, 0);
                }

                if (!paragraph.ChildElements.Any())
                {
                    return false;
                }
                mainPart.Document.Body?.AppendChild(paragraph);
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("Failed to append OMML element to Word body.", ex);
                return false;
            }
        }

        private static JustificationValues ToWordJustification(string? alignment)
        {
            var normalized = (alignment ?? "left").Trim().ToLowerInvariant();
            return normalized switch
            {
                "center" => JustificationValues.Center,
                "right" => JustificationValues.Right,
                _ => JustificationValues.Left
            };
        }

        private static string ExtractMathRawText(HtmlNode node)
        {
            var raw = node.GetAttributeValue("data-math-raw", string.Empty);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                return WebUtility.HtmlDecode(raw).Trim();
            }

            return WebUtility.HtmlDecode(node.InnerText ?? string.Empty).Trim();
        }

        private static bool TryBuildInlineMathImageHtml(HtmlNode node, out string inlineMathHtml)
        {
            inlineMathHtml = string.Empty;
            try
            {
                var raw = ExtractMathRawText(node);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return false;
                }

                var result = Task.Run(async () =>
                        await ExportChromiumPipelineService.RenderMathFormulaDataUrlAsync(raw, false).ConfigureAwait(false))
                    .WaitAsync(TimeSpan.FromSeconds(12))
                    .GetAwaiter()
                    .GetResult();

                if (string.IsNullOrWhiteSpace(result.dataUrl))
                {
                    return false;
                }

                var alt = WebUtility.HtmlEncode(raw);
                inlineMathHtml =
                    $"<img src=\"{result.dataUrl}\" data-awiki-math=\"1\" alt=\"{alt}\" style=\"display:inline-block;vertical-align:-0.16em;margin-bottom:-0.08em;height:0.9em;line-height:1;width:auto;max-width:none;\" />";
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("Inline math image rendering failed.", ex);
                return false;
            }
        }

        private static bool IsStandaloneMathParagraph(HtmlNode node)
        {
            var parent = node.ParentNode;
            if (parent == null || !parent.Name.Equals("p", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            foreach (var child in parent.ChildNodes)
            {
                if (ReferenceEquals(child, node))
                {
                    continue;
                }

                if (child.NodeType == HtmlNodeType.Text &&
                    !string.IsNullOrWhiteSpace(WebUtility.HtmlDecode(child.InnerText)))
                {
                    return false;
                }

                if (child.NodeType == HtmlNodeType.Element)
                {
                    var text = WebUtility.HtmlDecode(child.InnerText ?? string.Empty);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static string ResolveMathAlignment(HtmlNode node)
        {
            static string? ParseAlignment(string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return null;
                var v = value.Trim().ToLowerInvariant();
                if (v.Contains("center")) return "center";
                if (v.Contains("right")) return "right";
                if (v.Contains("left")) return "left";
                return null;
            }

            HtmlNode? current = node;
            while (current != null)
            {
                var alignAttr = ParseAlignment(current.GetAttributeValue("align", string.Empty));
                if (!string.IsNullOrWhiteSpace(alignAttr))
                {
                    return alignAttr;
                }

                var style = current.GetAttributeValue("style", string.Empty);
                if (!string.IsNullOrWhiteSpace(style))
                {
                    var match = Regex.Match(style, @"text-align\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        var parsed = ParseAlignment(match.Groups[1].Value);
                        if (!string.IsNullOrWhiteSpace(parsed))
                        {
                            return parsed;
                        }
                    }
                }

                current = current.ParentNode;
            }

            return "left";
        }

        private string ProcessInlineCode(string htmlContent)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(htmlContent);

            var inlineCodeNodes = doc.DocumentNode.SelectNodes(".//code[not(ancestor::pre)]");
            if (inlineCodeNodes != null)
            {
                foreach (var codeNode in inlineCodeNodes)
                {
                    var span = doc.CreateElement("span");
                    span.InnerHtml = codeNode.InnerHtml;
                    span.SetAttributeValue("style", "font-family: Consolas;");
                    codeNode.ParentNode.ReplaceChild(span, codeNode);
                }
            }

            return doc.DocumentNode.OuterHtml;
        }

        private void ProcessStyledContainerBlock(HtmlNode containerNode, MainDocumentPart mainPart)
        {
            var style = containerNode.GetAttributeValue("style", string.Empty);
            var isBlockquote = containerNode.Name.Equals("blockquote", StringComparison.OrdinalIgnoreCase);
            var backgroundColor = ExtractCssColor(style, "background-color") ??
                                  ExtractCssColorFromBackground(style) ??
                                  (isBlockquote ? "F8FAFC" : "F3F4F6");
            var borderColor = ExtractCssColor(style, "border-left-color") ??
                              ExtractCssColor(style, "border-color") ??
                              (isBlockquote ? "0EA5E9" : "9CA3AF");
            var hasLeftBorder = isBlockquote || style.Contains("border-left", StringComparison.OrdinalIgnoreCase);

            var blockTable = new Table(
                new TableProperties(
                    new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                    new TableLayout { Type = TableLayoutValues.Fixed },
                    new TableBorders(
                        new TopBorder { Val = BorderValues.None },
                        new BottomBorder { Val = BorderValues.None },
                        new LeftBorder
                        {
                            Val = hasLeftBorder ? BorderValues.Single : BorderValues.None,
                            Color = borderColor,
                            Size = hasLeftBorder ? (UInt32Value)18U : 0U
                        },
                        new RightBorder { Val = BorderValues.None },
                        new InsideHorizontalBorder { Val = BorderValues.None },
                        new InsideVerticalBorder { Val = BorderValues.None }
                    ),
                    new TableCellMarginDefault(
                        new TopMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                        new BottomMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                        new LeftMargin { Width = "120", Type = TableWidthUnitValues.Dxa },
                        new RightMargin { Width = "120", Type = TableWidthUnitValues.Dxa }
                    ),
                    new TableLook { Val = "04A0" }
                )
            );

            var cellProperties = new TableCellProperties(
                new Shading
                {
                    Val = ShadingPatternValues.Clear,
                    Color = "auto",
                    Fill = backgroundColor
                },
                new TableCellBorders(
                    new TopBorder { Val = BorderValues.None },
                    new BottomBorder { Val = BorderValues.None },
                    new LeftBorder { Val = BorderValues.None },
                    new RightBorder { Val = BorderValues.None }
                )
            );

            var blockCell = new TableCell(cellProperties);
            var converter = new HtmlToOpenXmlConverter(mainPart);
            var innerHtml = ProcessInlineCode(containerNode.InnerHtml);
            var elements = converter.Parse($"<div>{innerHtml}</div>");
            foreach (var element in elements)
            {
                blockCell.Append(element.CloneNode(true));
            }

            if (!blockCell.ChildElements.Any())
            {
                blockCell.Append(new Paragraph(new Run(new Text(WebUtility.HtmlDecode(containerNode.InnerText ?? string.Empty)))));
            }

            blockTable.Append(new TableRow(blockCell));
            mainPart.Document.Body?.AppendChild(blockTable);
        }

        private static bool IsStyledContainerBlock(HtmlNode node)
        {
            // Keep blockquote on the default HTML->OpenXml path to avoid
            // contaminating adjacent table parsing in Word exports.
            if (node.Name.Equals("blockquote", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (node.Name is not ("div" or "section" or "article" or "aside"))
            {
                return false;
            }

            var style = node.GetAttributeValue("style", string.Empty);
            if (string.IsNullOrWhiteSpace(style))
            {
                return false;
            }

            // Keep code blocks on the dedicated syntax-highlight pipeline.
            if (node.SelectSingleNode(".//pre") != null)
            {
                return false;
            }

            return style.Contains("background", StringComparison.OrdinalIgnoreCase) ||
                   style.Contains("border-left", StringComparison.OrdinalIgnoreCase) ||
                   style.Contains("border:", StringComparison.OrdinalIgnoreCase);
        }

        private static string? ExtractCssColor(string style, string propertyName)
        {
            var regex = new Regex($@"{Regex.Escape(propertyName)}\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            var match = regex.Match(style);
            return match.Success ? NormalizeCssColor(match.Groups[1].Value.Trim()) : null;
        }

        private static string? ExtractCssColorFromBackground(string style)
        {
            var regex = new Regex(@"background\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            var match = regex.Match(style);
            if (!match.Success)
            {
                return null;
            }

            var parts = match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var color = NormalizeCssColor(part.Trim());
                if (!string.IsNullOrWhiteSpace(color))
                {
                    return color;
                }
            }

            return null;
        }

        private static string? NormalizeCssColor(string cssColor)
        {
            if (string.IsNullOrWhiteSpace(cssColor))
            {
                return null;
            }

            try
            {
                var input = cssColor.Trim();
                if (input.StartsWith("#", StringComparison.Ordinal))
                {
                    var hex = input[1..];
                    if (hex.Length == 3)
                    {
                        return string.Concat(hex.Select(c => $"{c}{c}")).ToUpperInvariant();
                    }

                    if (hex.Length >= 6)
                    {
                        return hex[..6].ToUpperInvariant();
                    }
                }

                var rgbMatch = Regex.Match(input, @"rgb\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)", RegexOptions.IgnoreCase);
                if (rgbMatch.Success)
                {
                    var r = byte.Parse(rgbMatch.Groups[1].Value);
                    var g = byte.Parse(rgbMatch.Groups[2].Value);
                    var b = byte.Parse(rgbMatch.Groups[3].Value);
                    return $"{r:X2}{g:X2}{b:X2}";
                }

                var named = System.Drawing.Color.FromName(input);
                if (named.A > 0)
                {
                    return $"{named.R:X2}{named.G:X2}{named.B:X2}";
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static string? ResolveImageExtension(string? contentType, string imageUrl, byte[] bytes)
        {
            var ct = contentType?.ToLowerInvariant() ?? string.Empty;
            if (ct.Contains("svg") || ct.Contains("webp") || ct.StartsWith("text/"))
            {
                return null;
            }

            if (ct.Contains("png")) return "png";
            if (ct.Contains("jpeg") || ct.Contains("jpg")) return "jpg";
            if (ct.Contains("gif")) return "gif";
            if (ct.Contains("bmp")) return "bmp";
            if (ct.Contains("tiff")) return "tiff";
            if (ct.Contains("x-icon") || ct.Contains("icon")) return "ico";

            if (bytes.Length >= 8 &&
                bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
                bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            {
                return "png";
            }

            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            {
                return "jpg";
            }

            if (bytes.Length >= 6)
            {
                var gifHeader = Encoding.ASCII.GetString(bytes, 0, 6);
                if (gifHeader == "GIF87a" || gifHeader == "GIF89a")
                {
                    return "gif";
                }
            }

            try
            {
                var uri = new Uri(imageUrl);
                var ext = Path.GetExtension(uri.AbsolutePath)?.TrimStart('.').ToLowerInvariant();
                if (ext is "png" or "jpg" or "jpeg" or "gif" or "bmp" or "tif" or "tiff" or "ico")
                {
                    return ext == "jpeg" ? "jpg" : ext;
                }
            }
            catch { }

            return null;
        }

    }
}


