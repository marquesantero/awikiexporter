using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using HtmlAgilityPack;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Color = DocumentFormat.OpenXml.Wordprocessing.Color;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using ExportAzureWiki.Localization;
using ExportAzureWiki.Services;

namespace ExportAzureWiki
{
    public partial class ExportService
    {
        private static bool ShouldProcessImageManually(HtmlNode imgNode)
        {
            return !string.IsNullOrWhiteSpace(ResolveImageSource(imgNode));
        }

        private string GetAttributesString(HtmlNode node)
        {
            if (!node.HasAttributes)
                return string.Empty;

            var attributes = node.Attributes.Select(attr => $"{attr.Name}=\"{attr.Value}\"");
            return " " + string.Join(" ", attributes);
        }

        private static bool IsCodeBlock(HtmlNode node)
        {
            if (node.Name != "pre")
            {
                return false;
            }

            return HasSyntaxHint(node) || IsAsciiFlowPre(node);
        }

        private static bool HasSyntaxHint(HtmlNode preNode)
        {
            var preClass = preNode.GetAttributeValue("class", string.Empty);
            if (preClass.Contains("language-", StringComparison.OrdinalIgnoreCase) ||
                preClass.Contains("hljs", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var codeNode = preNode.SelectSingleNode(".//code");
            if (codeNode == null)
            {
                return false;
            }

            var codeClass = codeNode.GetAttributeValue("class", string.Empty);
            return codeClass.Contains("language-", StringComparison.OrdinalIgnoreCase) ||
                   codeClass.Contains("hljs", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAsciiFlowPre(HtmlNode preNode)
        {
            var text = WebUtility.HtmlDecode(preNode.InnerText ?? string.Empty);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (!(text.Contains('\n') || text.Contains('\r')))
            {
                return false;
            }

            var flowChars = new[] { "│", "─", "├", "└", "┌", "┐", "┘", "→", "▼", "▲", "◄", "►" };
            return flowChars.Any(ch => text.Contains(ch, StringComparison.Ordinal));
        }

        private string GetDefaultThemePath()
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var pathTheme = Path.Combine(baseDirectory, "style", "styles");

            if (!Directory.Exists(pathTheme))
                return string.Empty;

            // Procura por arquivo 'default' ou pega o primeiro tema disponível
            var themeFiles = Directory.GetFiles(pathTheme, "*.min.css");
            var defaultTheme = themeFiles.FirstOrDefault(f => f.Contains("default", StringComparison.OrdinalIgnoreCase));

            return defaultTheme ?? themeFiles.FirstOrDefault() ?? string.Empty;
        }
        private void ProcessCodeBlock(HtmlNode codeBlock, MainDocumentPart mainPart)
        {
            var cssFilePath = ThemeManager.GetCurrentThemeStatic();

            // Se não há tema disponível, usa estilos padrão vazios
            if (string.IsNullOrEmpty(cssFilePath) || !File.Exists(cssFilePath))
            {
                cssFilePath = GetDefaultThemePath();
            }

            var styles = CssToStylesConverter.ConvertCssToStyles(cssFilePath!);
            var codeElement = codeBlock.SelectSingleNode(".//code") ?? codeBlock;
            var languageClass = codeElement.GetAttributeValue("class", "");
            var hasLanguageClass = languageClass.Contains("language-", StringComparison.OrdinalIgnoreCase) ||
                                   languageClass.Contains("hljs", StringComparison.OrdinalIgnoreCase);
            var language = ResolveCodeLanguage(languageClass, codeElement.InnerText);

            var tokenList = ExtractTokensFromHtml(codeElement);
            if (tokenList.Count == 0)
            {
                tokenList.Add((1, string.Empty, WebUtility.HtmlDecode(codeElement.InnerText ?? string.Empty), string.Empty));
            }

            var decodedTokenList = DecodeHtmlTokens(tokenList);

            var table = new Table(
                new TableProperties(
                    new TableWidth { Width = "99%", Type = TableWidthUnitValues.Pct },
                    new TableLayout { Type = TableLayoutValues.Fixed },
                    new TableCellMarginDefault(
                        new TopMargin { Width = "10", Type = TableWidthUnitValues.Dxa },
                        new BottomMargin { Width = "10", Type = TableWidthUnitValues.Dxa },
                        new LeftMargin { Width = "100", Type = TableWidthUnitValues.Dxa },
                        new RightMargin { Width = "100", Type = TableWidthUnitValues.Dxa }
                    ),
                    new TableLook { Val = "04A0" }
                )
            );

            if (hasLanguageClass && language is not "plaintext" and not "json")
            {
                var rPr = new RunProperties(
                    new Color { Val = styles["CodeBlockStyle"].TextColor },
                    new Bold(),
                    new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" },
                    new FontSize { Val = "16" }
                );
                var run = new Run(new Text($"Linguagem: {language.ToUpper()}")) { RunProperties = rPr };
                var paragraph = new Paragraph(run);
                var headerCellProperties = new TableCellProperties(
                    new TableCellWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                    new Shading { Fill = styles["CodeBlockStyle"].BackgroundColor, Val = ShadingPatternValues.Clear }
                );
                var headerCell = new TableCell(headerCellProperties, paragraph);
                var headerRow = new TableRow(headerCell);
                table.Append(headerRow);
            }

            var codeRow = new TableRow();
            var codeCellProperties = new TableCellProperties(
                new TableCellWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                new TableCellBorders(
                    new TopBorder { Val = BorderValues.None },
                    new BottomBorder { Val = BorderValues.None },
                    new LeftBorder { Val = BorderValues.None },
                    new RightBorder { Val = BorderValues.None }
                ),
                new Shading { Fill = styles["CodeBlockStyle"].BackgroundColor, Val = ShadingPatternValues.Clear }
            );
            var codeCell = new TableCell(codeCellProperties);

            var tokensByLine = decodedTokenList.GroupBy(t => t.Item1);
            foreach (var lineGroup in tokensByLine)
            {
                var paragraph = new Paragraph
                {
                    ParagraphProperties = new ParagraphProperties(
                        new ParagraphStyleId { Val = "CodeBlockStyle" },
                        new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto },
                        new WordWrap { Val = true }
                    )
                };
                ApplySyntaxHighlighting(paragraph, lineGroup.ToList(), styles);
                codeCell.Append(paragraph);
            }
            codeRow.Append(codeCell);
            table.Append(codeRow);

            mainPart.Document.Body?.AppendChild(table);
        }

        private static string ResolveCodeLanguage(string languageClass, string? codeText)
        {
            if (!string.IsNullOrWhiteSpace(languageClass))
            {
                var tokens = languageClass
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(t => t.ToLowerInvariant())
                    .ToList();

                var explicitLanguage = tokens
                    .FirstOrDefault(t => t.StartsWith("language-", StringComparison.Ordinal))
                    ?.Replace("language-", string.Empty, StringComparison.Ordinal);
                if (!string.IsNullOrWhiteSpace(explicitLanguage))
                {
                    return explicitLanguage;
                }

                var commonLanguage = tokens.FirstOrDefault(t =>
                    t is "sql" or "plsql" or "oracle" or "tsql" or "mysql" or "postgresql" or "pgsql" or "json" or "xml" or "yaml" or "powershell");
                if (!string.IsNullOrWhiteSpace(commonLanguage))
                {
                    return commonLanguage;
                }
            }

            if (!string.IsNullOrWhiteSpace(codeText))
            {
                var sample = codeText.TrimStart();
                if (Regex.IsMatch(sample, @"\b(select|from|where|join|insert|update|delete|declare|begin|end)\b", RegexOptions.IgnoreCase))
                {
                    return "sql";
                }
            }

            return "plaintext";
        }

        private static bool ShouldInsertWordPageBreakBefore(HtmlNode node)
        {
            if (node.GetAttributeValue("data-word-page-break-before", string.Empty) == "1")
            {
                return true;
            }

            if (!node.Name.Equals("hr", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var classes = node.GetAttributeValue("class", string.Empty);
            return classes.Contains("page-break", StringComparison.OrdinalIgnoreCase) ||
                   classes.Contains("separator", StringComparison.OrdinalIgnoreCase);
        }

        private static void AppendWordPageBreak(MainDocumentPart mainPart)
        {
            var paragraph = new Paragraph(new Run(new Break { Type = BreakValues.Page }));
            mainPart.Document.Body?.AppendChild(paragraph);
        }

        private static void AppendWordTableOfContents(Body body)
        {
            var title = new Paragraph(
                new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
                new Run(new Text("Sumario"))
            );

            var tocField = new Paragraph(
                new SimpleField { Instruction = "TOC \\o \"1-3\" \\h \\z \\u" }
            );

            body.Append(title);
            body.Append(tocField);
            body.Append(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
        }

        private static void ApplyDefaultTableVisual(Table table)
        {
            if (table == null)
            {
                return;
            }

            var props = table.GetFirstChild<TableProperties>();
            if (props == null)
            {
                props = new TableProperties();
                table.PrependChild(props);
            }

            props.TableBorders = new TableBorders(
                new TopBorder { Val = BorderValues.None },
                new BottomBorder { Val = BorderValues.None },
                new LeftBorder { Val = BorderValues.None },
                new RightBorder { Val = BorderValues.None },
                new InsideHorizontalBorder { Val = BorderValues.None },
                new InsideVerticalBorder { Val = BorderValues.None }
            );

            props.TableStyle = null;
            // Keep Word tables inside printable area (A4 portrait with normal margins).
            // 9000 dxa ~= 6.25 in of usable width and prevents overflow caused by Autofit.
            const int tableUsableWidthDxa = 9000;
            props.TableWidth = new TableWidth { Width = tableUsableWidthDxa.ToString(), Type = TableWidthUnitValues.Dxa };
            props.TableLayout = new TableLayout { Type = TableLayoutValues.Fixed };
            props.TableLook = new TableLook { FirstRow = false, LastRow = false, FirstColumn = false, LastColumn = false, NoHorizontalBand = true, NoVerticalBand = true };

            var maxColumns = GetTableColumnCountForLayout(table);
            var columnWidths = maxColumns > 0
                ? CalculateColumnWidthsDxa(table, maxColumns, tableUsableWidthDxa)
                : Array.Empty<int>();
            if (maxColumns > 0)
            {
                var grid = new TableGrid();
                for (var i = 0; i < maxColumns; i++)
                {
                    grid.Append(new GridColumn { Width = columnWidths[i].ToString() });
                }

                table.GetFirstChild<TableGrid>()?.Remove();
                table.InsertAfter(grid, props);
            }

            // Force all table cells to white to avoid dark/light striping from source theme.
            foreach (var row in table.Elements<TableRow>())
            {
                var columnCursor = 0;
                foreach (var cell in row.Elements<TableCell>())
                {
                    var cellProps = cell.GetFirstChild<TableCellProperties>() ?? cell.PrependChild(new TableCellProperties());
                    if (maxColumns > 0)
                    {
                        var span = cellProps.GetFirstChild<GridSpan>()?.Val?.Value ?? 1;
                        if (span < 1)
                        {
                            span = 1;
                        }

                        var remaining = Math.Max(1, maxColumns - columnCursor);
                        if (span > remaining)
                        {
                            span = remaining;
                        }

                        var cellWidth = 0;
                        for (var idx = columnCursor; idx < columnCursor + span && idx < columnWidths.Length; idx++)
                        {
                            cellWidth += columnWidths[idx];
                        }
                        if (cellWidth <= 0)
                        {
                            cellWidth = tableUsableWidthDxa;
                        }
                        cellProps.TableCellWidth = new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = cellWidth.ToString() };
                        columnCursor += span;
                    }
                    else
                    {
                        cellProps.TableCellWidth = new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = tableUsableWidthDxa.ToString() };
                    }
                    cellProps.NoWrap = null;
                    cellProps.Shading = new Shading
                    {
                        Val = ShadingPatternValues.Clear,
                        Color = "auto",
                        Fill = "auto"
                    };
                    cellProps.TableCellBorders = new TableCellBorders(
                        new TopBorder { Val = BorderValues.None },
                        new BottomBorder { Val = BorderValues.None },
                        new LeftBorder { Val = BorderValues.None },
                        new RightBorder { Val = BorderValues.None }
                    );
                }
            }

            // Keep only a black separator line below header row.
            var firstRow = table.Elements<TableRow>().FirstOrDefault();
            if (firstRow != null)
            {
                firstRow.TableRowProperties ??= new TableRowProperties();
                if (firstRow.TableRowProperties.GetFirstChild<TableHeader>() == null)
                {
                    firstRow.TableRowProperties.Append(new TableHeader());
                }

                foreach (var cell in firstRow.Elements<TableCell>())
                {
                    var cellProps = cell.GetFirstChild<TableCellProperties>() ?? cell.PrependChild(new TableCellProperties());
                    cellProps.TableCellBorders = new TableCellBorders(
                        new BottomBorder { Val = BorderValues.Single, Size = 12, Color = "000000" }
                    );
                }

                foreach (var run in firstRow.Descendants<Run>())
                {
                    run.RunProperties ??= new RunProperties();
                    if (run.RunProperties.Bold == null)
                    {
                        run.RunProperties.Bold = new Bold();
                    }
                }
            }
        }

        private static int GetTableColumnCountForLayout(Table table)
        {
            // Prefer header/first row width to avoid distortion from malformed rows.
            var firstRow = table.Elements<TableRow>().FirstOrDefault();
            if (firstRow != null)
            {
                var firstRowCount = CountRowColumns(firstRow);
                if (firstRowCount > 0)
                {
                    return Math.Clamp(firstRowCount, 1, 8);
                }
            }

            var counts = new List<int>();
            foreach (var row in table.Elements<TableRow>())
            {
                var count = CountRowColumns(row);
                if (count > 0)
                {
                    counts.Add(count);
                }
            }

            if (counts.Count == 0)
            {
                return 0;
            }

            // Use the most common row width to avoid extreme rows collapsing all columns.
            var mode = counts
                .GroupBy(v => v)
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.Key)
                .First()
                .Key;

            return Math.Clamp(mode, 1, 8);
        }

        private static int CountRowColumns(TableRow row)
        {
            var total = 0;
            foreach (var cell in row.Elements<TableCell>())
            {
                var span = cell.GetFirstChild<TableCellProperties>()?.GetFirstChild<GridSpan>()?.Val?.Value ?? 1;
                total += Math.Max(1, span);
            }

            return total;
        }

        private static int[] CalculateColumnWidthsDxa(Table table, int maxColumns, int tableUsableWidthDxa)
        {
            if (maxColumns <= 0)
            {
                return Array.Empty<int>();
            }

            var weights = Enumerable.Repeat(1.0, maxColumns).ToArray();
            var sampledRows = table.Elements<TableRow>().Take(30);

            foreach (var row in sampledRows)
            {
                var columnCursor = 0;
                foreach (var cell in row.Elements<TableCell>())
                {
                    if (columnCursor >= maxColumns)
                    {
                        break;
                    }

                    var span = cell.GetFirstChild<TableCellProperties>()?.GetFirstChild<GridSpan>()?.Val?.Value ?? 1;
                    span = Math.Max(1, Math.Min(span, maxColumns - columnCursor));

                    var text = Regex.Replace(cell.InnerText ?? string.Empty, @"\s+", " ").Trim();
                    var length = Math.Min(text.Length, 120);
                    var weight = 1.0 + (length / 18.0);

                    for (var i = 0; i < span; i++)
                    {
                        var col = columnCursor + i;
                        if (col < maxColumns)
                        {
                            weights[col] = Math.Max(weights[col], weight / span);
                        }
                    }

                    columnCursor += span;
                }
            }

            var minWidthDxa = 1100;
            var remainingWidth = tableUsableWidthDxa - (minWidthDxa * maxColumns);
            if (remainingWidth <= 0)
            {
                return Enumerable.Repeat(Math.Max(600, tableUsableWidthDxa / maxColumns), maxColumns).ToArray();
            }

            var totalWeight = Math.Max(1.0, weights.Sum());
            var widths = new int[maxColumns];
            var used = 0;

            for (var i = 0; i < maxColumns; i++)
            {
                var extra = (int)Math.Round((weights[i] / totalWeight) * remainingWidth);
                widths[i] = minWidthDxa + extra;
                used += widths[i];
            }

            widths[maxColumns - 1] += tableUsableWidthDxa - used;
            return widths;
        }

        private static void ApplyWordHeadingPaginationRules(Body body)
        {
            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
                if (styleId is not ("Heading1" or "Heading2" or "Heading3"))
                {
                    continue;
                }

                paragraph.ParagraphProperties ??= new ParagraphProperties();
                if (paragraph.ParagraphProperties.GetFirstChild<KeepNext>() == null)
                {
                    paragraph.ParagraphProperties.Append(new KeepNext());
                }

                if (paragraph.ParagraphProperties.GetFirstChild<KeepLines>() == null)
                {
                    paragraph.ParagraphProperties.Append(new KeepLines());
                }
            }
        }

        private List<(int, string, string, string)> DecodeHtmlTokens(List<(int, string, string, string)> tokens)
        {
            return tokens.Select(token => (
                token.Item1,
                token.Item2,
                WebUtility.HtmlDecode(token.Item3),
                token.Item4
            )).ToList();
        }

        private List<(int, string, string, string)> ExtractTokensFromHtml(HtmlNode codeElement)
        {
            var tokenList = new List<(int, string, string, string)>(); // LineNumber, Indentation, Text, Class
            var lineNumber = 1;

            ExtractTokensRecursive(codeElement, ref lineNumber, ref tokenList, "");

            return tokenList;
        }

        private void ExtractTokensRecursive(HtmlNode node, ref int lineNumber, ref List<(int, string, string, string)> tokenList, string parentClass)
        {
            if (node.NodeType == HtmlNodeType.Text)
            {
                AddToken(node.InnerText, parentClass, ref lineNumber, ref tokenList);
                return;
            }

            var nodeClass = node.GetAttributeValue("class", "");
            var nodeStyle = node.GetAttributeValue("style", "");
            var effectiveClass = BuildTokenDescriptor(parentClass, nodeClass, nodeStyle);

            if (node.HasChildNodes)
            {
                foreach (var childNode in node.ChildNodes)
                {
                    ExtractTokensRecursive(childNode, ref lineNumber, ref tokenList, effectiveClass);
                }
            }
            else if (!string.IsNullOrEmpty(node.InnerText))
            {
                AddToken(node.InnerText, effectiveClass, ref lineNumber, ref tokenList);
            }
        }

        private static string BuildTokenDescriptor(string parentDescriptor, string nodeClass, string nodeStyle)
        {
            var parentClass = ExtractDescriptorClass(parentDescriptor);
            var parentColor = ExtractDescriptorColor(parentDescriptor);

            var effectiveClass = string.IsNullOrWhiteSpace(nodeClass) ? parentClass : nodeClass.Trim();
            var effectiveColor = TryExtractInlineTextColor(nodeStyle) ?? parentColor;

            if (string.IsNullOrWhiteSpace(effectiveColor))
            {
                return effectiveClass;
            }

            return $"cls::{effectiveClass}||col::{effectiveColor}";
        }

        private static string ExtractDescriptorClass(string descriptor)
        {
            if (string.IsNullOrWhiteSpace(descriptor))
            {
                return string.Empty;
            }

            if (!descriptor.StartsWith("cls::", StringComparison.Ordinal))
            {
                return descriptor;
            }

            var idx = descriptor.IndexOf("||", StringComparison.Ordinal);
            if (idx < 0)
            {
                return descriptor["cls::".Length..];
            }

            return descriptor.Substring("cls::".Length, idx - "cls::".Length);
        }

        private static string? ExtractDescriptorColor(string descriptor)
        {
            if (string.IsNullOrWhiteSpace(descriptor))
            {
                return null;
            }

            var marker = "||col::";
            var idx = descriptor.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
            {
                return null;
            }

            var value = descriptor[(idx + marker.Length)..].Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static string? TryExtractInlineTextColor(string style)
        {
            if (string.IsNullOrWhiteSpace(style))
            {
                return null;
            }

            var match = Regex.Match(style, @"(?:^|;)\s*color\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            var value = match.Groups[1].Value.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private void AddToken(string? text, string className, ref int lineNumber, ref List<(int, string, string, string)> tokenList)
        {
            if (text == null) return;
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var indentation = new string(' ', GetLeadingSpaces(line));
                var content = line.TrimStart();
                tokenList.Add((lineNumber, indentation, content, className));
                if (i < lines.Length - 1) lineNumber++;
            }
        }

        private int GetLeadingSpaces(string line)
        {
            var count = 0;
            foreach (var c in line)
            {
                if (c == ' ')
                    count++;
                else
                    break;
            }
            return count;
        }

        public void ApplySyntaxHighlighting(Paragraph paragraph, List<(int LineNumber, string Indentation, string Text, string Class)> lineTokens, Dictionary<string, CodeStyle> styles)
        {
            var highlighter = new LanguageHighlighter();
            highlighter.ApplyHighlighting(paragraph, lineTokens, styles);
        }

        private void CreateCodeStyles(MainDocumentPart mainPart)
        {
            var styleDefinitionsPart = mainPart.StyleDefinitionsPart;
            if (styleDefinitionsPart == null)
            {
                styleDefinitionsPart = mainPart.AddNewPart<StyleDefinitionsPart>();
                styleDefinitionsPart.Styles = new Styles();
            }

            var cssFilePath = ThemeManager.GetCurrentThemeStatic();

            // Se não há tema disponível, usa estilos padrão
            if (string.IsNullOrEmpty(cssFilePath) || !File.Exists(cssFilePath))
            {
                cssFilePath = GetDefaultThemePath();
            }

            var styles = CssToStylesConverter.ConvertCssToStyles(cssFilePath!);

            foreach (var (styleName, style) in styles)
            {
                AddCodeStyle(
                    styleDefinitionsPart,
                    styleName,
                    styleName,
                    style.BackgroundColor,
                    style.TextColor,
                    style.IsBold,
                    style.IsItalic
                );
            }
        }

        private static void AddCodeStyle(StyleDefinitionsPart styleDefinitionsPart, string styleId, string styleName, string? backgroundColor = null, string? textColor = null, bool isBold = false, bool isItalic = false)
        {
            if (styleDefinitionsPart.Styles!.Elements<Style>().Any(s => s.StyleId == styleId))
                return;

            var style = new Style
            {
                Type = StyleValues.Character,
                StyleId = styleId,
                CustomStyle = true
            };

            style.Append(new Name { Val = styleName });
            style.Append(new BasedOn { Val = "DefaultParagraphFont" });
            style.Append(new UIPriority { Val = 99 });
            style.Append(new UnhideWhenUsed());

            var runProperties = new StyleRunProperties();
            runProperties.Append(new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" });
            runProperties.Append(new FontSize { Val = "20" });

            if (!string.IsNullOrEmpty(textColor))
            {
                runProperties.Append(new Color { Val = textColor });
            }

            if (!string.IsNullOrEmpty(backgroundColor))
            {
                runProperties.Append(new Shading { Fill = backgroundColor, Val = ShadingPatternValues.Clear, Color = "auto" });
            }

            if (isBold)
            {
                runProperties.Append(new Bold());
            }

            if (isItalic)
            {
                runProperties.Append(new Italic());
            }

            style.Append(runProperties);
            styleDefinitionsPart.Styles.Append(style);
            styleDefinitionsPart.Styles.Save();
        }


        private static void ProcessImage(HtmlNode imgNode, MainDocumentPart mainPart)
        {
            try
            {
                var resolvedPath = ResolveImageToLocalPath(imgNode);
                if (File.Exists(resolvedPath))
                {
                    try
                    {
                        InsertImageWithSizeAdjustment(mainPart, resolvedPath!, imgNode);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(LocalizationManager.Sf("export.word.log.image_insert_error", ex.Message));
                    }
                }
                else
                {
                    Console.WriteLine($@"Imagem não encontrada ou não acessível: {ResolveImageSource(imgNode)}");
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning(LocalizationManager.Sf(
                    "export.word.log.image_process_exception_src",
                    imgNode?.GetAttributeValue("src", "unknown") ?? "unknown"), ex);
            }
        }

        /// <summary>Downloads/persists an image to a local file and returns its path.</summary>
        private static string? ResolveImageToLocalPath(HtmlNode imgNode)
        {
            var imageSrc = ResolveImageSource(imgNode);
            if (string.IsNullOrWhiteSpace(imageSrc))
            {
                return null;
            }

            if (imageSrc.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                return TryPersistDataUriImage(imageSrc, ResolveWordImagesCacheFolderPath());
            }
            if (imageSrc.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                imageSrc.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return TryPersistRemoteImage(imageSrc, ResolveWordImagesCacheFolderPath());
            }
            return imageSrc;
        }

        /// <summary>
        /// True for a paragraph whose only visible content is images (e.g. a row
        /// of shields.io badges in a README header). Such a paragraph must render
        /// as one Word paragraph with the images inline side by side -- not one
        /// block paragraph per image, which stacks them vertically.
        /// </summary>
        private static bool IsImageRowParagraph(HtmlNode node)
        {
            if (!node.Name.Equals("p", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (node.Descendants("img").Count() < 2)
            {
                return false;
            }

            // An explicit line break means the author stacked the images on
            // purpose -- keep them on the normal (block-per-image) path.
            if (node.Descendants("br").Any())
            {
                return false;
            }

            // No meaningful text (only whitespace around the images/links).
            var text = string.Concat(node.DescendantsAndSelf()
                .Where(n => n.NodeType == HtmlNodeType.Text)
                .Select(t => t.InnerText));
            return string.IsNullOrWhiteSpace(WebUtility.HtmlDecode(text));
        }

        /// <summary>
        /// Renders every image in <paramref name="containerNode"/> into a single
        /// paragraph, inline and side by side, at natural size.
        /// </summary>
        private static void ProcessImageRow(HtmlNode containerNode, MainDocumentPart mainPart)
        {
            var paragraph = new Paragraph();
            var added = 0;
            foreach (var img in containerNode.Descendants("img"))
            {
                try
                {
                    var path = ResolveImageToLocalPath(img);
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    var drawing = BuildImageDrawing(mainPart, path!, img);
                    if (drawing == null)
                    {
                        continue;
                    }

                    if (added > 0)
                    {
                        paragraph.AppendChild(new Run(new Text(" ") { Space = SpaceProcessingModeValues.Preserve }));
                    }
                    paragraph.AppendChild(new Run(drawing));
                    added++;
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning(LocalizationManager.Sf(
                        "export.word.log.image_process_exception_src",
                        img.GetAttributeValue("src", "unknown")), ex);
                }
            }

            if (added > 0)
            {
                mainPart.Document.Body?.AppendChild(paragraph);
            }
        }

        private static string ResolveImageSource(HtmlNode imgNode)
        {
            if (imgNode == null)
            {
                return string.Empty;
            }

            var src = imgNode.GetAttributeValue("src", "").Trim();
            if (!string.IsNullOrWhiteSpace(src))
            {
                return src;
            }

            var fallbackAttrs = new[] { "data-src", "data-original", "data-lazy-src", "data-url" };
            foreach (var attr in fallbackAttrs)
            {
                var value = imgNode.GetAttributeValue(attr, "").Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            var srcset = imgNode.GetAttributeValue("srcset", "").Trim();
            if (!string.IsNullOrWhiteSpace(srcset))
            {
                var first = srcset.Split(',').FirstOrDefault()?.Trim();
                if (!string.IsNullOrWhiteSpace(first))
                {
                    var url = first.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        return url;
                    }
                }
            }

            return string.Empty;
        }

        private static void InsertImageWithSizeAdjustment(MainDocumentPart mainPart, string imagePath, HtmlNode? sourceImgNode = null)
        {
            var drawing = BuildImageDrawing(mainPart, imagePath, sourceImgNode);
            if (drawing != null)
            {
                mainPart.Document.Body?.AppendChild(new Paragraph(new Run(drawing)));
            }
        }

        /// <summary>
        /// Builds the inline picture element for an image, sized from the image's
        /// natural pixel dimensions (or explicit width/height), without appending
        /// it. Callers decide placement -- a standalone image gets its own
        /// paragraph; a row of badges shares one paragraph (see ProcessImageRow).
        /// </summary>
        private static Drawing? BuildImageDrawing(MainDocumentPart mainPart, string imagePath, HtmlNode? sourceImgNode = null)
        {
            const int maxWidthEmus = 6000000;
            const int defaultWidthEmus = 1800000;
            const int defaultHeightEmus = 420000;
            const double emusPerPixel = 9525d;

            // Last line of defense for local .svg/.webp files that reached here
            // without going through the persist/transcode path: convert to PNG so
            // System.Drawing can read them.
            imagePath = EnsureWordEmbeddableImagePath(imagePath);

            using var imageStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
            var extension = Path.GetExtension(imagePath).ToLowerInvariant();
            var imagePart = extension switch
            {
                ".png" => mainPart.AddImagePart(ImagePartType.Png),
                ".gif" => mainPart.AddImagePart(ImagePartType.Gif),
                ".bmp" => mainPart.AddImagePart(ImagePartType.Bmp),
                ".tif" or ".tiff" => mainPart.AddImagePart(ImagePartType.Tiff),
                ".ico" => mainPart.AddImagePart(ImagePartType.Icon),
                ".webp" => mainPart.AddImagePart(ImagePartType.Png),
                _ => mainPart.AddImagePart(ImagePartType.Jpeg)
            };
            
            imageStream.Position = 0;
            imagePart.FeedData(imageStream);

            long? naturalWidthEmus = null;
            long? naturalHeightEmus = null;
            var widthEmus = (long)defaultWidthEmus;
            var heightEmus = (long)defaultHeightEmus;
            try
            {
                imageStream.Position = 0;
                // Fully qualified: Platform no longer pulls System.Drawing in
                // implicitly via WinForms (Fase 3.1b). System.Drawing.Common
                // still provides Image.FromStream.
                using var image = System.Drawing.Image.FromStream(imageStream);
                if (image.Width > 0 && image.Height > 0)
                {
                    naturalWidthEmus = (long)(image.Width * emusPerPixel);
                    naturalHeightEmus = (long)(image.Height * emusPerPixel);
                }
            }
            catch
            {
                // Keep default dimensions when metadata cannot be read (e.g. unsupported encoder).
            }

            var pageMaxWidthPx = maxWidthEmus / emusPerPixel;
            var desiredWidthPx = TryGetRequestedImageSizePx(sourceImgNode, "width", pageMaxWidthPx);
            var desiredHeightPx = TryGetRequestedImageSizePx(sourceImgNode, "height", pageMaxWidthPx);

            if (desiredWidthPx.HasValue || desiredHeightPx.HasValue)
            {
                if (desiredWidthPx.HasValue && desiredHeightPx.HasValue)
                {
                    widthEmus = (long)(desiredWidthPx.Value * emusPerPixel);
                    heightEmus = (long)(desiredHeightPx.Value * emusPerPixel);
                }
                else if (desiredWidthPx.HasValue)
                {
                    widthEmus = (long)(desiredWidthPx.Value * emusPerPixel);
                    if (naturalWidthEmus.HasValue && naturalHeightEmus.HasValue && naturalWidthEmus.Value > 0)
                    {
                        var ratio = (double)widthEmus / naturalWidthEmus.Value;
                        heightEmus = (long)(naturalHeightEmus.Value * ratio);
                    }
                }
                else if (desiredHeightPx.HasValue)
                {
                    heightEmus = (long)(desiredHeightPx.Value * emusPerPixel);
                    if (naturalWidthEmus.HasValue && naturalHeightEmus.HasValue && naturalHeightEmus.Value > 0)
                    {
                        var ratio = (double)heightEmus / naturalHeightEmus.Value;
                        widthEmus = (long)(naturalWidthEmus.Value * ratio);
                    }
                }
            }
            else if (naturalWidthEmus.HasValue && naturalHeightEmus.HasValue)
            {
                widthEmus = naturalWidthEmus.Value;
                heightEmus = naturalHeightEmus.Value;
            }

            if (widthEmus > maxWidthEmus)
            {
                var ratio = (double)maxWidthEmus / widthEmus;
                widthEmus = maxWidthEmus;
                heightEmus = (long)(heightEmus * ratio);
            }

            if (widthEmus <= 0 || heightEmus <= 0)
            {
                widthEmus = defaultWidthEmus;
                heightEmus = defaultHeightEmus;
            }

            var element =
                new Drawing(
                    new DW.Inline(
                        new DW.Extent() { Cx = widthEmus, Cy = heightEmus },
                        new DW.EffectExtent()
                        {
                            LeftEdge = 0L,
                            TopEdge = 0L,
                            RightEdge = 0L,
                            BottomEdge = 0L
                        },
                        new DW.DocProperties()
                        {
                            Id = (UInt32Value)1U,
                            Name = "Picture 1"
                        },
                        new DW.NonVisualGraphicFrameDrawingProperties(
                            new A.GraphicFrameLocks() { NoChangeAspect = true }),
                        new A.Graphic(
                            new A.GraphicData(
                                    new PIC.Picture(
                                        new PIC.NonVisualPictureProperties(
                                            new PIC.NonVisualDrawingProperties()
                                            {
                                                Id = (UInt32Value)0U,
                                                Name = "New Bitmap Image.jpg"
                                            },
                                            new PIC.NonVisualPictureDrawingProperties()),
                                        new PIC.BlipFill(
                                            new A.Blip(
                                                new A.BlipExtensionList(
                                                    new A.BlipExtension()
                                                    {
                                                        Uri =
                                                            "{28A0092B-C50C-407E-A947-70E740481C1C}"
                                                    })
                                            )
                                            {
                                                Embed = mainPart.GetIdOfPart(imagePart),
                                                CompressionState =
                                                    A.BlipCompressionValues.Print
                                            },
                                            new A.Stretch(
                                                new A.FillRectangle())),
                                        new PIC.ShapeProperties(
                                            new A.Transform2D(
                                                new A.Offset() { X = 0L, Y = 0L },
                                                new A.Extents() { Cx = widthEmus, Cy = heightEmus }),
                                            new A.PresetGeometry(
                                                    new A.AdjustValueList()
                                                )
                                            { Preset = A.ShapeTypeValues.Rectangle }))
                                )
                            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })
                    )
                    {
                        DistanceFromTop = (UInt32Value)0U,
                        DistanceFromBottom = (UInt32Value)0U,
                        DistanceFromLeft = (UInt32Value)0U,
                        DistanceFromRight = (UInt32Value)0U,
                        EditId = "50D07946"
                    });

            return element;
        }

        /// <summary>
        /// Converts a local .svg/.webp file to a sibling .png (cached next to it)
        /// so the System.Drawing-based embedder can read it. Returns the PNG path,
        /// or the original path when no conversion is needed or it failed.
        /// </summary>
        private static string EnsureWordEmbeddableImagePath(string imagePath)
        {
            try
            {
                var ext = Path.GetExtension(imagePath).ToLowerInvariant();
                if (ext is not (".svg" or ".webp"))
                {
                    return imagePath;
                }

                var pngPath = Path.ChangeExtension(imagePath, ".png");
                if (File.Exists(pngPath))
                {
                    return pngPath;
                }

                var png = ext == ".svg"
                    ? RasterImageConverter.SvgToPng(File.ReadAllText(imagePath))
                    : RasterImageConverter.TranscodeToPng(File.ReadAllBytes(imagePath));
                if (png == null)
                {
                    return imagePath;
                }

                File.WriteAllBytes(pngPath, png);
                return pngPath;
            }
            catch
            {
                return imagePath;
            }
        }

        private static double? TryGetRequestedImageSizePx(HtmlNode? imgNode, string dimension, double pageMaxWidthPx)
        {
            if (imgNode == null)
            {
                return null;
            }

            var attrValue = imgNode.GetAttributeValue(dimension, string.Empty)?.Trim();
            var parsedAttr = ParseCssSizePx(attrValue, pageMaxWidthPx);
            if (parsedAttr.HasValue && parsedAttr.Value > 0)
            {
                return parsedAttr.Value;
            }

            var style = imgNode.GetAttributeValue("style", string.Empty);
            if (!string.IsNullOrWhiteSpace(style))
            {
                var regex = new Regex($@"{dimension}\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
                var match = regex.Match(style);
                if (match.Success)
                {
                    var parsedStyle = ParseCssSizePx(match.Groups[1].Value.Trim(), pageMaxWidthPx);
                    if (parsedStyle.HasValue && parsedStyle.Value > 0)
                    {
                        return parsedStyle.Value;
                    }
                }
            }

            return null;
        }

        private static double? ParseCssSizePx(string? value, double pageMaxWidthPx)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var raw = value.Trim().ToLowerInvariant();
            if (raw.EndsWith("%"))
            {
                if (double.TryParse(raw.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
                {
                    return Math.Max(1d, pageMaxWidthPx * (percent / 100d));
                }

                return null;
            }

            raw = raw.Replace("px", string.Empty).Trim();
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var px))
            {
                return Math.Max(1d, px);
            }

            return null;
        }

        
    }
}
