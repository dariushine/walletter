using System.Text.Json.Serialization;

namespace Walletter.Application.Categories;

public class CreateCategoryCommand
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Color { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class UpdateCategoryCommand
{
    public string? Name { get; set; }
    public string? Color { get; set; }
}
