namespace AcControl.Server.Telemetry;

using System.Diagnostics;

public static class AcControlServerTelemetry
{
    public static ActivitySource Source { get; } = new("AcControl.Server");
}
