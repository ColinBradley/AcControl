namespace AcControl.Server.Data.Models;

using KoenZomers.Ring.Api.Entities;

public class RingDeviceHistoryEvent
{
    public RingDeviceHistoryEvent(string id, string kind, DateTime createdAtDateTime)
    {
        this.Id = id;
        this.Kind = kind;
        this.CreatedAtDateTime = createdAtDateTime;
    }

    public string Id { get; }

    public string Kind { get; }

    public DateTime CreatedAtDateTime { get; }

    public string? VideoUrl { get; internal set; }

    public string? ThumbnailUrl { get; internal set; }

    public static RingDeviceHistoryEvent From(DoorbotHistoryEvent e)
    {
        return new RingDeviceHistoryEvent(e.Id, e.Kind, e.CreatedAtDateTime!.Value);
    }
}