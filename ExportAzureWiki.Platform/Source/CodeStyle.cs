using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Color = DocumentFormat.OpenXml.Wordprocessing.Color;
using ExCSS;
using FontSize = DocumentFormat.OpenXml.Wordprocessing.FontSize;
using System.Text.RegularExpressions;

namespace ExportAzureWiki
{
    public class LanguageHighlighter
    {
        public void ApplyHighlighting(Paragraph paragraph, List<(int LineNumber, string Indentation, string Text, string Class)> lineTokens, Dictionary<string, CodeStyle> styles)
        {
            foreach (var (lineNumber, indentation, text, cssClass) in lineTokens)
            {
                if (!string.IsNullOrEmpty(indentation))
                {
                    AppendStyledRun(paragraph, indentation, "CodeBlockStyle", styles);
                }

                if (!string.IsNullOrEmpty(text))
                {
                    AppendStyledRun(paragraph, text, cssClass, styles);
                }

                if (lineNumber != lineTokens.Last().LineNumber)
                {
                    paragraph.Append(new Run(new Break()));
                }
            }
        }

        private void AppendStyledRun(Paragraph paragraph, string text, string cssClass, Dictionary<string, CodeStyle> styles)
        {
            var run = new Run();
            var runProperties = new RunProperties(
                new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" },
                new FontSize { Val = "18" }
            );

            var colorOverride = ExtractTokenColorOverride(cssClass);
            var cssClassForStyle = ExtractTokenCssClass(cssClass);
            var styleId = "CodeBlockStyle";

            if (!string.IsNullOrEmpty(cssClassForStyle))
            {
                styleId = ResolveStyleIdFromCssClasses(cssClassForStyle);
            }

            if (styles.TryGetValue(styleId, out var codeStyle))
            {
                if (!string.IsNullOrEmpty(codeStyle.TextColor))
                {
                    runProperties.Append(new Color { Val = codeStyle.TextColor });
                }
                if (codeStyle.IsBold)
                {
                    runProperties.Append(new Bold());
                }
                if (codeStyle.IsItalic)
                {
                    runProperties.Append(new Italic());
                }
            }

            if (!string.IsNullOrWhiteSpace(colorOverride))
            {
                runProperties.Append(new Color { Val = colorOverride });
            }

            run.PrependChild(runProperties);
            run.Append(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
            paragraph.Append(run);
        }

        private static string ResolveStyleIdFromCssClasses(string cssClass)
        {
            if (string.IsNullOrWhiteSpace(cssClass))
            {
                return "CodeBlockStyle";
            }

            // hljs markup may contain multiple classes (e.g. "hljs-variable language_")
            var classTokens = cssClass
                .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 1) exact match first
            foreach (var token in classTokens)
            {
                if (CssToStylesConverter.StyleMapping.TryGetValue(token, out var mapped))
                {
                    return mapped;
                }
            }

            // 2) prefer first hljs-* token
            var hljsToken = classTokens.FirstOrDefault(t => t.StartsWith("hljs-", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(hljsToken) &&
                CssToStylesConverter.StyleMapping.TryGetValue(hljsToken, out var hljsMapped))
            {
                return hljsMapped;
            }

            // 3) some highlighters output bare tokens ("keyword", "string", ...)
            foreach (var token in classTokens)
            {
                var prefixed = $"hljs-{token}";
                if (CssToStylesConverter.StyleMapping.TryGetValue(prefixed, out var mappedPrefixed))
                {
                    return mappedPrefixed;
                }
            }

            return "CodeBlockStyle";
        }

        private static string ExtractTokenCssClass(string tokenDescriptor)
        {
            if (string.IsNullOrWhiteSpace(tokenDescriptor))
            {
                return string.Empty;
            }

            if (!tokenDescriptor.StartsWith("cls::", StringComparison.Ordinal))
            {
                return tokenDescriptor;
            }

            var sep = tokenDescriptor.IndexOf("||", StringComparison.Ordinal);
            if (sep < 0)
            {
                return tokenDescriptor["cls::".Length..];
            }

            return tokenDescriptor.Substring("cls::".Length, sep - "cls::".Length);
        }

        private static string? ExtractTokenColorOverride(string tokenDescriptor)
        {
            if (string.IsNullOrWhiteSpace(tokenDescriptor))
            {
                return null;
            }

            var marker = "||col::";
            var idx = tokenDescriptor.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
            {
                return null;
            }

            var value = tokenDescriptor[(idx + marker.Length)..].Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return NormalizeColorForWord(value);
        }

        private static string? NormalizeColorForWord(string value)
        {
            var hexMatch = Regex.Match(value, @"#([0-9a-fA-F]{3,8})");
            if (hexMatch.Success)
            {
                var hex = hexMatch.Groups[1].Value;
                if (hex.Length == 3)
                {
                    return string.Concat(hex.Select(c => $"{c}{c}")).ToUpperInvariant();
                }
                if (hex.Length >= 6)
                {
                    return hex[..6].ToUpperInvariant();
                }
            }

            var rgbMatch = Regex.Match(value, @"rgba?\s*\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})", RegexOptions.IgnoreCase);
            if (rgbMatch.Success)
            {
                var r = Math.Clamp(int.Parse(rgbMatch.Groups[1].Value), 0, 255);
                var g = Math.Clamp(int.Parse(rgbMatch.Groups[2].Value), 0, 255);
                var b = Math.Clamp(int.Parse(rgbMatch.Groups[3].Value), 0, 255);
                return $"{r:X2}{g:X2}{b:X2}";
            }

            return null;
        }
    }


    public static class CssToStylesConverter
    {
        public static Dictionary<string, string> StyleMapping = new()
        {
            { "hljs", "CodeBlockStyle" },
            { "hljs-comment", "CodeCommentStyle" },
            { "hljs-quote", "CodeQuoteStyle" },
            { "hljs-keyword", "CodeKeywordStyle" },
            { "hljs-selector-tag", "CodeSelectorTagStyle" },
            { "hljs-literal", "CodeLiteralStyle" },
            { "hljs-strong", "CodeStrongStyle" },
            { "hljs-emphasis", "CodeEmphasisStyle" },
            { "hljs-type", "CodeTypeStyle" },
            { "hljs-string", "CodeStringStyle" },
            { "hljs-symbol", "CodeSymbolStyle" },
            { "hljs-bullet", "CodeBulletStyle" },
            { "hljs-addition", "CodeAdditionStyle" },
            { "hljs-attribute", "CodeAttributeStyle" },
            { "hljs-built_in", "CodeBuiltInStyle" },
            { "hljs-builtin-name", "CodeBuiltInNameStyle" },
            { "hljs-number", "CodeNumberStyle" },
            { "hljs-operator", "CodeOperatorStyle" },
            { "hljs-selector-id", "CodeSelectorIdStyle" },
            { "hljs-selector-class", "CodeSelectorClassStyle" },
            { "hljs-selector-attr", "CodeSelectorAttrStyle" },
            { "hljs-selector-pseudo", "CodeSelectorPseudoStyle" },
            { "hljs-template-tag", "CodeTemplateTagStyle" },
            { "hljs-template-variable", "CodeTemplateVariableStyle" },
            { "hljs-variable", "CodeVariableStyle" },
            { "hljs-deletion", "CodeDeletionStyle" },
            { "hljs-regexp", "CodeRegexpStyle" },
            { "hljs-link", "CodeLinkStyle" },
            { "hljs-meta", "CodeMetaStyle" },
            { "hljs-title", "CodeTitleStyle" },
            { "hljs-title.class_", "CodeClassTitleStyle" },
            { "hljs-title.function_", "CodeFunctionTitleStyle" },
            { "hljs-params", "CodeParamsStyle" },
            { "hljs-doctag", "CodeDoctagStyle" },
            { "hljs-attr", "CodeAttrStyle" },
            { "hljs-subst", "CodeSubstStyle" },
            { "hljs-section", "CodeSectionStyle" },
            { "hljs-name", "CodeNameStyle" },
            { "hljs-tag", "CodeTagStyle" }
        };

        public static Dictionary<string, CodeStyle> ConvertCssToStyles(string cssFilePath)
        {
            var cssContent = File.ReadAllText(cssFilePath);
            var parser = new StylesheetParser();
            var stylesheet = parser.Parse(cssContent);
            var styles = new Dictionary<string, CodeStyle>();
            var cssVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var styleRule in stylesheet.StyleRules)
            {
                foreach (var declaration in styleRule.Style.Declarations)
                {
                    var property = declaration.Name?.Trim();
                    var value = declaration.Value?.Trim();
                    if (!string.IsNullOrWhiteSpace(property) &&
                        property.StartsWith("--", StringComparison.Ordinal) &&
                        !string.IsNullOrWhiteSpace(value))
                    {
                        cssVariables[property] = value;
                    }
                }
            }

            foreach (var styleRule in stylesheet.StyleRules)
            {
                foreach (var selector in styleRule.SelectorText.Split(','))
                {
                    foreach (var styleName in ResolveMappedStylesFromSelector(selector))
                    {
                        EnsureStyleExists(styles, styleName);
                        var styleObject = styles[styleName];

                        foreach (var declaration in styleRule.Style.Declarations)
                        {
                            var property = declaration.Name;
                            var value = declaration.Value?.Trim();

                            switch (property)
                            {
                                case "color":
                                    styleObject.TextColor = ConvertColor(value!, cssVariables);
                                    break;
                                case "background":
                                case "background-color":
                                    styleObject.BackgroundColor = ConvertColor(value!, cssVariables);
                                    break;
                                case "font-weight":
                                    if (int.TryParse(value, out var fontWeight))
                                    {
                                        styleObject.IsBold = fontWeight >= 700;
                                    }
                                    else
                                    {
                                        styleObject.IsBold = value!.Equals("bold", StringComparison.OrdinalIgnoreCase) ||
                                                             value.Equals("bolder", StringComparison.OrdinalIgnoreCase);
                                    }
                                    break;
                                case "font-style":
                                    styleObject.IsItalic = value!.Equals("italic", StringComparison.OrdinalIgnoreCase) ||
                                                           value.Equals("oblique", StringComparison.OrdinalIgnoreCase);
                                    break;
                            }
                        }
                    }
                }
            }

            ApplyFallbackTokenColors(styles);
            return styles;
        }

        private static string? ConvertColor(string colorValue, Dictionary<string, string> cssVariables)
        {
            colorValue = colorValue.Trim();

            if (string.IsNullOrEmpty(colorValue))
                return null;

            colorValue = ResolveCssVariables(colorValue, cssVariables);

            // Try to capture first hex token from complex values (e.g. "color:#800;" or "var(--x,#800)")
            var hexMatch = System.Text.RegularExpressions.Regex.Match(colorValue, @"#([0-9a-fA-F]{3,8})");
            if (hexMatch.Success)
            {
                return NormalizeHexColor(hexMatch.Groups[1].Value);
            }

            if (colorValue.StartsWith(@"#"))
            {
                return NormalizeHexColor(colorValue[1..]);
            }
            else if (colorValue.StartsWith("rgb"))
            {
                // Converte RGB ou RGBA para hexadecimal
                var rgbValues = colorValue
                    .Replace("rgb(", "")
                    .Replace("rgba(", "")
                    .Replace(")", "")
                    .Split(',');

                if (rgbValues.Length >= 3 &&
                    int.TryParse(rgbValues[0].Trim(), out var r) &&
                    int.TryParse(rgbValues[1].Trim(), out var g) &&
                    int.TryParse(rgbValues[2].Trim(), out var b))
                {
                    return $"{r:X2}{g:X2}{b:X2}";
                }
            }
            else
            {
                // Converte nomes de cor para hexadecimal
                var knownColor = System.Drawing.Color.FromName(colorValue);
                if (knownColor.IsKnownColor)
                {
                    return $"{knownColor.R:X2}{knownColor.G:X2}{knownColor.B:X2}";
                }
            }

            // Retorna null se a cor não for conhecida
            return null;
        }

        private static string ResolveCssVariables(string value, Dictionary<string, string> cssVariables)
        {
            if (string.IsNullOrWhiteSpace(value) || !value.Contains("var(", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            var result = value;
            for (var i = 0; i < 5; i++)
            {
                var match = Regex.Match(result, @"var\(\s*(--[a-zA-Z0-9\-_]+)\s*(?:,\s*([^)]+))?\)");
                if (!match.Success)
                {
                    break;
                }

                var varName = match.Groups[1].Value;
                var fallback = match.Groups[2].Success ? match.Groups[2].Value.Trim() : string.Empty;
                var replacement = cssVariables.TryGetValue(varName, out var mapped) ? mapped : fallback;
                if (string.IsNullOrWhiteSpace(replacement))
                {
                    break;
                }

                result = result.Replace(match.Value, replacement, StringComparison.OrdinalIgnoreCase);
            }

            return result;
        }

        private static void ApplyFallbackTokenColors(Dictionary<string, CodeStyle> styles)
        {
            var fallback = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["CodeKeywordStyle"] = "C678DD",
                ["CodeStringStyle"] = "98C379",
                ["CodeNumberStyle"] = "D19A66",
                ["CodeCommentStyle"] = "5C6370",
                ["CodeBuiltInStyle"] = "56B6C2",
                ["CodeBuiltInNameStyle"] = "56B6C2",
                ["CodeFunctionTitleStyle"] = "61AFEF",
                ["CodeClassTitleStyle"] = "E5C07B",
                ["CodeTitleStyle"] = "61AFEF",
                ["CodeVariableStyle"] = "E06C75",
                ["CodeOperatorStyle"] = "ABB2BF"
            };

            foreach (var entry in fallback)
            {
                if (!styles.TryGetValue(entry.Key, out var style))
                {
                    styles[entry.Key] = new CodeStyle { TextColor = entry.Value };
                    continue;
                }

                if (string.IsNullOrWhiteSpace(style.TextColor))
                {
                    style.TextColor = entry.Value;
                }
            }
        }

        private static IEnumerable<string> ResolveMappedStylesFromSelector(string selector)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var candidateClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var trimmedSelector = selector.Trim();

            if (!string.IsNullOrWhiteSpace(trimmedSelector) && trimmedSelector.StartsWith(".", StringComparison.Ordinal))
            {
                candidateClasses.Add(trimmedSelector.TrimStart('.'));
            }

            foreach (Match match in Regex.Matches(trimmedSelector, @"\.([a-zA-Z0-9_-]+)"))
            {
                if (match.Groups.Count > 1 && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
                {
                    candidateClasses.Add(match.Groups[1].Value);
                }
            }

            foreach (var className in candidateClasses)
            {
                if (StyleMapping.TryGetValue(className, out var styleName))
                {
                    result.Add(styleName);
                }
            }

            var classesArray = candidateClasses.ToArray();
            for (var i = 0; i < classesArray.Length; i++)
            {
                for (var j = 0; j < classesArray.Length; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    var combined = $"{classesArray[i]}.{classesArray[j]}";
                    if (StyleMapping.TryGetValue(combined, out var styleName))
                    {
                        result.Add(styleName);
                    }
                }
            }

            return result;
        }

        private static void EnsureStyleExists(Dictionary<string, CodeStyle> styles, string styleName)
        {
            if (!styles.ContainsKey(styleName))
            {
                styles[styleName] = new CodeStyle();
            }
        }

        private static string? NormalizeHexColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                return null;
            }

            hex = hex.Trim().TrimStart('#');

            // #RGB
            if (hex.Length == 3)
            {
                return string.Concat(hex.Select(c => $"{c}{c}")).ToUpperInvariant();
            }

            // #RGBA (alpha ignored)
            if (hex.Length == 4)
            {
                return string.Concat(hex.Take(3).Select(c => $"{c}{c}")).ToUpperInvariant();
            }

            // #RRGGBB
            if (hex.Length == 6)
            {
                return hex.ToUpperInvariant();
            }

            // #RRGGBBAA (alpha ignored)
            if (hex.Length == 8)
            {
                return hex[..6].ToUpperInvariant();
            }

            return null;
        }
    }
}
