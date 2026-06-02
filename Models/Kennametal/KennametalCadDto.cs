using System.Text.Json.Serialization;

namespace AutoToolCatalog.Models.Kennametal;

public class KennametalCadDto
{
    [JsonPropertyName("productID")]
    public string? ProductId { get; set; }

    [JsonPropertyName("attributes")]
    public List<KennametalCadAttributeDto> Attributes { get; set; } = [];

    [JsonPropertyName("attributeValues")]
    public List<string> AttributeValues { get; set; } = [];
}

public class KennametalCadAttributeDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("cadParameterName")]
    public string? CadParameterName { get; set; }
}
