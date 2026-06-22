using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using HtmlAgilityPack;
using ExportAzureWiki.Services;
using System.Text.RegularExpressions;
using Serilog;

namespace ExportAzureWiki;

public partial class ExportService
{
    public void ExportToWord(string htmlContent, string filePath, string? wordTemplatePath = null, bool applyWordFineTune = false)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // Decrypt referenced cache images into a throwaway folder and point the
        // embedder at it; the long-lived cache stays encrypted at rest.
        var imageFolderPath = CreateDecryptedImageWorkspace(htmlContent);
        try
        {
            htmlContent = Regex.Replace(htmlContent,
                @"https://local\.images(/[^""')\s]+)",
                m => Path.Combine(imageFolderPath, m.Groups[1].Value.TrimStart('/')));
            htmlContent = PreprocessHtmlForWord(htmlContent);
            var tPreprocess = sw.ElapsedMilliseconds;

            var doc = new HtmlAgilityPack.HtmlDocument
            {
                OptionFixNestedTags = true
            };
            doc.LoadHtml(htmlContent);
            var tParse = sw.ElapsedMilliseconds;

            long tProcess, tLayout, tSave;
            using (var wordDoc = WordprocessingDocument.Create(filePath, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
            {
                var mainPart = wordDoc.AddMainDocumentPart();
                mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(new Body());
                CreateCodeStyles(mainPart);
                var bodyNode = doc.DocumentNode.SelectSingleNode("//body");
                if (bodyNode != null)
                {
                    ProcessHtmlContent(bodyNode, mainPart);
                }
                tProcess = sw.ElapsedMilliseconds;

                SeparateAdjacentTables(mainPart.Document.Body!);
                ApplyWordHeadingPaginationRules(mainPart.Document.Body!);
                if (!string.IsNullOrWhiteSpace(wordTemplatePath) && File.Exists(wordTemplatePath))
                {
                    ApplyWordTemplate(mainPart, wordTemplatePath);
                }

                if (applyWordFineTune)
                {
                    ApplyWordFineTune(mainPart.Document.Body!);
                }
                tLayout = sw.ElapsedMilliseconds;

                mainPart.Document.Save();
                tSave = sw.ElapsedMilliseconds;
            }

            Log.Information(
                "Word export timings (ms): preprocess={Pre} htmlParse={Parse} processContent={Process} layout={Layout} save={Save} total={Total} htmlChars={Len}",
                tPreprocess, tParse - tPreprocess, tProcess - tParse, tLayout - tProcess, tSave - tLayout, sw.ElapsedMilliseconds, htmlContent.Length);
        }
        finally
        {
            TryDeleteImageWorkspace(imageFolderPath);
        }
    }

    /// <summary>
    /// OOXML requires a paragraph between two tables. When two tables are direct
    /// siblings (e.g. a syntax-highlight code-block table immediately followed by
    /// a data table, as happens under a Markdown heading-less section), Word
    /// merges them on render and reconciles their differing column grids by
    /// crushing one column — which makes a row wrap to a full page each. Insert
    /// an empty separator paragraph between any two adjacent tables.
    /// </summary>
    internal static void SeparateAdjacentTables(Body body)
    {
        if (body == null)
        {
            return;
        }

        foreach (var table in body.Elements<Table>().ToList())
        {
            if (table.NextSibling() is Table)
            {
                body.InsertAfter(new Paragraph(), table);
            }
        }
    }

    private static void ApplyWordTemplate(MainDocumentPart targetMainPart, string templatePath)
    {
        try
        {
            using var templateDoc = WordprocessingDocument.Open(templatePath, false);
            var templateMainPart = templateDoc.MainDocumentPart;
            if (templateMainPart?.Document?.Body == null || targetMainPart.Document?.Body == null)
            {
                return;
            }

            var templateSection = templateMainPart.Document.Body.Descendants<SectionProperties>().LastOrDefault();
            if (templateSection == null)
            {
                return;
            }

            var targetBody = targetMainPart.Document.Body;
            var targetSection = targetBody.Descendants<SectionProperties>().LastOrDefault();
            if (targetSection == null)
            {
                targetSection = new SectionProperties();
                targetBody.AppendChild(targetSection);
            }

            targetSection.RemoveAllChildren();

            foreach (var child in templateSection.Elements())
            {
                if (child is HeaderReference || child is FooterReference)
                {
                    continue;
                }

                targetSection.Append(child.CloneNode(true));
            }

            foreach (var headerRef in templateSection.Elements<HeaderReference>())
            {
                var relationshipId = headerRef.Id?.Value;
                if (string.IsNullOrWhiteSpace(relationshipId))
                {
                    continue;
                }

                if (templateMainPart.GetPartById(relationshipId) is not HeaderPart sourceHeaderPart)
                {
                    continue;
                }

                var importedHeaderPart = targetMainPart.AddPart(sourceHeaderPart);
                var newHeaderId = targetMainPart.GetIdOfPart(importedHeaderPart);
                targetSection.Append(new HeaderReference
                {
                    Type = headerRef.Type,
                    Id = newHeaderId
                });
            }

            foreach (var footerRef in templateSection.Elements<FooterReference>())
            {
                var relationshipId = footerRef.Id?.Value;
                if (string.IsNullOrWhiteSpace(relationshipId))
                {
                    continue;
                }

                if (templateMainPart.GetPartById(relationshipId) is not FooterPart sourceFooterPart)
                {
                    continue;
                }

                var importedFooterPart = targetMainPart.AddPart(sourceFooterPart);
                var newFooterId = targetMainPart.GetIdOfPart(importedFooterPart);
                targetSection.Append(new FooterReference
                {
                    Type = footerRef.Type,
                    Id = newFooterId
                });
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning(
                LocalizationManager.Sf("export.runtime.log.word_template_apply_failed", templatePath),
                ex);
        }
    }

    private static void ApplyWordFineTune(Body body)
    {
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            if (paragraph.Ancestors<Table>().Any())
            {
                continue;
            }

            var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            if (styleId is "Heading1" or "Heading2" or "Heading3")
            {
                continue;
            }

            paragraph.ParagraphProperties ??= new ParagraphProperties();

            var spacing = paragraph.ParagraphProperties.GetFirstChild<SpacingBetweenLines>();
            if (spacing == null)
            {
                paragraph.ParagraphProperties.Append(new SpacingBetweenLines
                {
                    Before = "0",
                    After = "120",
                    Line = "276",
                    LineRule = LineSpacingRuleValues.Auto
                });
            }
            else
            {
                spacing.Before ??= "0";
                spacing.After ??= "120";
                spacing.Line ??= "276";
                spacing.LineRule ??= LineSpacingRuleValues.Auto;
            }

            if (paragraph.ParagraphProperties.GetFirstChild<WidowControl>() == null)
            {
                paragraph.ParagraphProperties.Append(new WidowControl());
            }
        }
    }
}
