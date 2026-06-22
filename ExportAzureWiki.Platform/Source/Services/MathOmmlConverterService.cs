using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;

namespace ExportAzureWiki.Services;

public static class MathOmmlConverterService
{
    public static bool TryConvertMathMlToOmml(string mathMl, out string? ommlXml, string? preferredAlignment = null)
    {
        ommlXml = null;
        try
        {
            if (string.IsNullOrWhiteSpace(mathMl))
            {
                return false;
            }

            var xsltPath = ResolveMml2OmmlXsltPath();
            if (string.IsNullOrWhiteSpace(xsltPath) || !File.Exists(xsltPath))
            {
                return false;
            }

            var transform = new XslCompiledTransform();
            transform.Load(xsltPath);

            using var reader = XmlReader.Create(new StringReader(mathMl));
            using var writer = new StringWriter();
            using (var xmlWriter = XmlWriter.Create(writer, transform.OutputSettings))
            {
                transform.Transform(reader, xmlWriter);
            }

            var transformed = writer.ToString();
            if (string.IsNullOrWhiteSpace(transformed))
            {
                return false;
            }

            transformed = StripXmlDeclarations(transformed);

            var wrapped = $"<root xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\">{transformed}</root>";
            var xdoc = XDocument.Parse(wrapped, LoadOptions.PreserveWhitespace);
            var first = xdoc.Root?.Elements().FirstOrDefault();
            if (first == null)
            {
                return false;
            }

            var rawOmml = first.ToString(SaveOptions.DisableFormatting);
            ommlXml = ApplyOmmlAlignment(rawOmml, preferredAlignment);
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning("Math OMML conversion failed.", ex);
            return false;
        }
    }

    public static bool TryConvertLatexToOmml(string latex, bool displayMode, out string? ommlXml, string? preferredAlignment = null)
    {
        ommlXml = null;
        try
        {
            var mathMl = Task.Run(async () =>
                    await ExportChromiumPipelineService
                        .RenderMathFormulaMathMlAsync(latex, displayMode)
                        .ConfigureAwait(false))
                .WaitAsync(TimeSpan.FromSeconds(20))
                .GetAwaiter()
                .GetResult();
            if (string.IsNullOrWhiteSpace(mathMl))
            {
                return false;
            }
            return TryConvertMathMlToOmml(mathMl, out ommlXml, preferredAlignment);
        }
        catch (TimeoutException tex)
        {
            LoggingService.LogWarning("Math OMML conversion timeout.", tex);
            return false;
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning("Math OMML conversion failed.", ex);
            return false;
        }
    }

    private static string? ResolveMml2OmmlXsltPath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "tools", "math", "MML2OMML.XSL"),
            Path.Combine(baseDir, "..", "tools", "math", "MML2OMML.XSL"),
            Path.Combine(baseDir, "..", "..", "tools", "math", "MML2OMML.XSL"),
            Path.Combine(baseDir, "..", "..", "..", "tools", "math", "MML2OMML.XSL"),
            @"C:\Program Files\Microsoft Office\root\Office16\MML2OMML.XSL",
            @"C:\Program Files (x86)\Microsoft Office\root\Office16\MML2OMML.XSL",
            @"C:\Program Files\Microsoft Office\Office16\MML2OMML.XSL",
            @"C:\Program Files (x86)\Microsoft Office\Office16\MML2OMML.XSL"
        };

        foreach (var c in candidates)
        {
            var p = Path.GetFullPath(c);
            if (File.Exists(p))
            {
                return p;
            }
        }

        return null;
    }

    private static string StripXmlDeclarations(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return xml;
        }

        var output = xml;
        while (true)
        {
            var start = output.IndexOf("<?xml", StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                break;
            }

            var end = output.IndexOf("?>", start, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
            {
                break;
            }

            output = output.Remove(start, (end + 2) - start);
        }

        return output.Trim();
    }

    private static string ApplyOmmlAlignment(string ommlXml, string? preferredAlignment)
    {
        if (string.IsNullOrWhiteSpace(ommlXml))
        {
            return ommlXml;
        }

        var normalized = (preferredAlignment ?? "left").Trim().ToLowerInvariant();
        var target = normalized switch
        {
            "center" => "centerGroup",
            "right" => "right",
            _ => "left"
        };

        if (target == "centerGroup")
        {
            return ommlXml;
        }

        try
        {
            XNamespace m = "http://schemas.openxmlformats.org/officeDocument/2006/math";
            var wrapped = $"<root xmlns:m=\"{m}\">{ommlXml}</root>";
            var doc = XDocument.Parse(wrapped, LoadOptions.PreserveWhitespace);
            var root = doc.Root;
            if (root == null)
            {
                return ommlXml;
            }

            var hasAnyMathPara = false;
            foreach (var mathPara in root.Descendants(m + "oMathPara"))
            {
                hasAnyMathPara = true;
                if (target != "centerGroup")
                {
                    var innerMath = mathPara.Element(m + "oMath");
                    if (innerMath != null)
                    {
                        mathPara.ReplaceWith(new XElement(innerMath));
                        continue;
                    }
                }

                var paraPr = mathPara.Element(m + "oMathParaPr");
                if (paraPr == null)
                {
                    paraPr = new XElement(m + "oMathParaPr");
                    mathPara.AddFirst(paraPr);
                }

                var jc = paraPr.Element(m + "jc");
                if (jc == null)
                {
                    jc = new XElement(m + "jc");
                    paraPr.Add(jc);
                }

                jc.SetAttributeValue(m + "val", target);
            }

            // Some transforms can emit direct jc nodes; normalize all.
            foreach (var jc in root.Descendants(m + "jc"))
            {
                jc.SetAttributeValue(m + "val", target);
            }

            if (!hasAnyMathPara)
            {
                return ommlXml;
            }

            return string.Concat(root.Elements().Select(x => x.ToString(SaveOptions.DisableFormatting)));
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning("Failed to apply OMML alignment override.", ex);
            return ommlXml;
        }
    }
}
