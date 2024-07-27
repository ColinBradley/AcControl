namespace AcControl.Server.Data.Models;

using Microsoft.EntityFrameworkCore;

[Index(nameof(SerialNumber), nameof(DateTime), IsUnique = true)]
[PrimaryKey(nameof(SerialNumber), nameof(DateTime))]
public sealed record AirGradientSensorEntry
{
    public required DateTime DateTime { get; set; }

    public required string SerialNumber { get; init; }

    /// <summary>
    /// WiFi Strength in DBm
    /// </summary>
    public required int WiFiStrength { get; init; }

    /// <summary>
    /// CO2 in ppm
    /// </summary>
    public required int RCo2 { get; init; }

    /// <summary>
    /// PM1 in μg/m³
    /// </summary>
    public required int Pm01 { get; init; }

    /// <summary>
    /// PM2.5 in μg/m³
    /// </summary>
    public required int Pm02 { get; init; }

    /// <summary>
    /// PM10 in μg/m³
    /// </summary>
    public required int Pm10 { get; init; }

    public required int Pm003Count { get; init; }

    /// <summary>
    /// Temperature in °C
    /// </summary>
    public required double Atmp { get; init; }

    public required double AtmpCompensated { get; init; }

    /// <summary>
    /// Relative Humidity in % ?
    /// </summary>
    public required int Rhum { get; init; }

    public required int RhumCompensated { get; init; }

    /// <summary>
    /// Total Volatile Components in ppb
    /// </summary>
    public required int TvocIndex { get; init; }

    /// <summary>
    /// Total Volatile Components in ppb
    /// </summary>
    public required int TvocRaw { get; init; }

    public required int NoxIndex { get; init; }

    public required int NoxRaw { get; init; }
}
