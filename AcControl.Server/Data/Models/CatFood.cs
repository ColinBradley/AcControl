namespace AcControl.Server.Data.Models;

using Newtonsoft.Json;

public record CatFood
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Brand { get; set; } = string.Empty;

    public string ImageUrl { get; set; } = string.Empty;
}