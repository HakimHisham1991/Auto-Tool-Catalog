using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoToolCatalog.Models.Seco;

public class SecoProductDto
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("ItemNumber")]
    public string? ItemNumber { get; set; }

    [JsonPropertyName("Designation")]
    public string? Designation { get; set; }

    [JsonPropertyName("Attributes")]
    public List<SecoAttributeDto> Attributes { get; set; } = new();
}

public class SecoAttributeDto
{
    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonPropertyName("Value")]
    public JsonElement? Value { get; set; }

    public string? ValueText => Value switch
    {
        null => null,
        { ValueKind: JsonValueKind.String } el => el.GetString(),
        { ValueKind: JsonValueKind.Number } el => el.GetRawText(),
        { ValueKind: JsonValueKind.True } => "true",
        { ValueKind: JsonValueKind.False } => "false",
        { ValueKind: JsonValueKind.Null } => null,
        var el => el.ToString()
    };

    [JsonPropertyName("Unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("ValueDescription")]
    public string? ValueDescription { get; set; }
}
