using System.Text.Json;

namespace ExportAzureWiki;

public partial class CodeStyle
{
    public string? BackgroundColor { get; set; }
    public string? TextColor { get; set; }
    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }
}

public class ColorSchema
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, CodeStyle> Styles { get; set; } = new();
}

public class ColorSchemaRoot
{
    public List<ColorSchema> Schemas { get; set; } = new();
}

public class ColorSchemaManager
{
    private Dictionary<string, ColorSchema> schemas = new();

    public ColorSchemaManager(string jsonFilePath)
    {
        LoadSchemas(jsonFilePath);
    }

    private void LoadSchemas(string jsonFilePath)
    {
        var json = File.ReadAllText(jsonFilePath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        var root = JsonSerializer.Deserialize<ColorSchemaRoot>(json, options) ?? new ColorSchemaRoot();
        schemas = root.Schemas.ToDictionary(s => s.Name, s => s);
    }

    public ColorSchema GetSchema(string name)
    {
        return (schemas.TryGetValue(name, out var schema) ? schema : null)!;
    }
}
