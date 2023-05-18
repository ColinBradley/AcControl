namespace AcControl.Server.Data.Models;

public record CatFeed
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public DateTime Time { get; set; } = DateTime.UtcNow;

    public string Food { get; set; } = string.Empty;

    public bool StartedImmediately { get; set; } = true;

    public bool OneSitting { get; set; } = true;

    public bool Finished { get; set; } = true;

    public string Notes { get; set; } = string.Empty;
}
