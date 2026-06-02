using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoToolCatalog.Models.Sandvik;

public class SandvikProductResponseDto
{
    [JsonPropertyName("product")]
    public JsonElement Product { get; set; }

    [JsonPropertyName("properties")]
    public List<SandvikPropertyDefinitionDto> Properties { get; set; } = [];
}

public class SandvikPropertyDefinitionDto
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("isDetails")]
    public bool IsDetails { get; set; }
}

public class SandvikAutocompleteItemDto
{
    [JsonPropertyName("Title")]
    public string? Title { get; set; }

    [JsonPropertyName("Type")]
    public string? Type { get; set; }

    [JsonPropertyName("ID")]
    public string? Id { get; set; }

    [JsonPropertyName("MatchType")]
    public string? MatchType { get; set; }
}
