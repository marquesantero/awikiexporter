using ExportAzureWiki.Localization;
using iText.Html2pdf;
using iText.IO.Image;
using iText.Kernel.Pdf;
using ITextAreaBreak = iText.Layout.Element.AreaBreak;
using ITextDocument = iText.Layout.Document;
using ITextImage = iText.Layout.Element.Image;
using ITextAreaBreakType = iText.Layout.Properties.AreaBreakType;
using ITextHorizontalAlignment = iText.Layout.Properties.HorizontalAlignment;
using System.Text.RegularExpressions;

namespace ExportAzureWiki;

public partial class ExportService
{
    public async Task ExportToPdfAsync(string htmlContent, string filePath)
    {
        await Task.Run(() => ExportToPdf(htmlContent, filePath)).ConfigureAwait(false);
    }

    public void ExportToPdf(string htmlContent, string filePath)
    {
        // Decrypt referenced cache images into a throwaway folder; iText reads
        // them from disk via file:// while the cache stays encrypted at rest.
        var imageFolderPath = CreateDecryptedImageWorkspace(htmlContent);
        try
        {
            htmlContent = Regex.Replace(htmlContent,
                @"https://local\.images(/[^""')\s]+)",
                m => "file:///" + Path.Combine(imageFolderPath, m.Groups[1].Value.TrimStart('/')).Replace("\\", "/"));
            htmlContent = PreprocessHtmlForPdf(htmlContent);

            using var fileStream = new FileStream(filePath, FileMode.Create);
            var converterProperties = new ConverterProperties();
            HtmlConverter.ConvertToPdf(htmlContent, fileStream, converterProperties);
        }
        catch (Exception ex)
        {
            LocalizedMessageBox.ShowError(
                LocalizationManager.Sf("export.pdf.error", ex.Message),
                LocalizationManager.S("common.error"));
            throw;
        }
        finally
        {
            TryDeleteImageWorkspace(imageFolderPath);
        }
    }

    public void ExportImageSlicesToPdf(List<byte[]> imageSlices, string filePath)
    {
        if (imageSlices == null || imageSlices.Count == 0)
        {
            throw new InvalidOperationException(LocalizationManager.S("export.pdf.error.no_image_slices"));
        }

        using var writer = new PdfWriter(filePath);
        using var pdf = new PdfDocument(writer);
        using var document = new ITextDocument(pdf);
        document.SetMargins(0, 0, 0, 0);

        for (var i = 0; i < imageSlices.Count; i++)
        {
            var imageData = ImageDataFactory.Create(imageSlices[i]);
            var image = new ITextImage(imageData);

            var pageSize = pdf.GetDefaultPageSize();
            image.ScaleToFit(pageSize.GetWidth(), pageSize.GetHeight());
            image.SetHorizontalAlignment(ITextHorizontalAlignment.CENTER);

            document.Add(image);

            if (i < imageSlices.Count - 1)
            {
                document.Add(new ITextAreaBreak(ITextAreaBreakType.NEXT_PAGE));
            }
        }
    }
}
