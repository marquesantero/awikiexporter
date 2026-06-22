using System.Text;
using ExportAzureWiki.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using Svg;

namespace ExportAzureWiki;

/// <summary>
/// Converts image formats that the Word/OpenXml image pipeline (backed by
/// System.Drawing) cannot embed directly — SVG and WebP — into PNG bytes.
///
/// Both backends are managed and Windows-friendly: <c>Svg</c> rasterizes via
/// System.Drawing.Common; <c>SixLabors.ImageSharp</c> (2.1.x, Apache-2.0)
/// decodes WebP and re-encodes PNG. All methods are best-effort and return
/// null on failure so callers can fall back to skipping the image.
/// </summary>
internal static class RasterImageConverter
{
    /// <summary>Rasterizes SVG markup to PNG bytes, or null on failure.</summary>
    public static byte[]? SvgToPng(string svgXml)
    {
        if (string.IsNullOrWhiteSpace(svgXml))
        {
            return null;
        }

        try
        {
            var svgDocument = SvgDocument.FromSvg<SvgDocument>(svgXml);
            if (svgDocument == null)
            {
                return null;
            }

            // Draw() honors the SVG's intrinsic size/viewBox. Guard against a
            // zero-sized document (some badges omit width/height) by falling
            // back to a sensible raster size.
            using var bitmap = svgDocument.Draw();
            if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
            {
                using var sized = svgDocument.Draw(800, 0);
                if (sized == null)
                {
                    return null;
                }

                using var sizedStream = new MemoryStream();
                sized.Save(sizedStream, System.Drawing.Imaging.ImageFormat.Png);
                return sizedStream.ToArray();
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return stream.ToArray();
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning("RasterImageConverter: SVG rasterization failed.", ex);
            return null;
        }
    }

    /// <summary>
    /// Transcodes any ImageSharp-decodable image (notably WebP) to PNG bytes,
    /// or null on failure.
    /// </summary>
    public static byte[]? TranscodeToPng(byte[] imageBytes)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var image = Image.Load(imageBytes);
            using var stream = new MemoryStream();
            image.Save(stream, new PngEncoder());
            return stream.ToArray();
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning("RasterImageConverter: WebP/raster transcode failed.", ex);
            return null;
        }
    }

    /// <summary>Heuristic: do these bytes look like SVG markup?</summary>
    public static bool LooksLikeSvg(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return false;
        }

        var sample = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 512)).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return sample.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
               && sample.Contains("<svg", StringComparison.OrdinalIgnoreCase)
            || sample.StartsWith("<svg", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Heuristic: do these bytes start with the WebP (RIFF/WEBP) magic?</summary>
    public static bool LooksLikeWebp(byte[] bytes)
        => bytes is { Length: >= 12 }
           && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
           && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P';
}
