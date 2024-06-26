﻿namespace AcControl.Server.Data;

using AcControl.Server.Data.Models;

// Note: I thought this was going to change every day or randomly, but the API I found just fetched pricing for a specific product
// And I can't find a way to list products... sooo hard code it for now hope we notice when there's a new pricing thing set (I will get sent an email?!)

public static class OctopusEnergyService
{
    private static readonly DayRate[] sDayRatesDescending;
    private static readonly TimeSpan sHour = TimeSpan.FromHours(1);

    static OctopusEnergyService()
    {
        // Check: https://octopus.energy/smart/flux/
        sDayRatesDescending =
        [
            new DayRate()
            {
                Start = new DateOnly(2027, 4, 1),
                Import = new()
                {
                    OffPeakRate = 24.66,
                    NightRate = 14.8,
                    DayRate = 34.53,
                    StandingCharge = 49.12,
                },
                Export = new()
                {
                    OffPeakRate = 14.85,
                    NightRate = 5.26,
                    DayRate = 24.99,
                }
            },
            new DayRate()
            {
                Start = new DateOnly(2023, 7, 1),
                Import = new()
                {
                    OffPeakRate = 29.94,
                    NightRate = 17.96,
                    DayRate = 41.91,
                    StandingCharge = 49.77,
                },
                Export = new()
                {
                    OffPeakRate = 18.94,
                    NightRate = 6.96,
                    DayRate = 30.91,
                }
            },
            new DayRate()
            {
                Start = new DateOnly(2023, 2, 14),
                Import = new()
                {
                    OffPeakRate = 32.72,
                    NightRate = 19.63,
                    DayRate = 45.81,
                    StandingCharge = 49.77,
                },
                Export = new()
                {
                    OffPeakRate = 21.72,
                    NightRate = 8.63,
                    DayRate = 34.81,
                }
            },
        ];
    }

    public static DayRate GetDayRate(DateOnly date) =>
        sDayRatesDescending
            .DefaultIfEmpty(sDayRatesDescending.First())
            .FirstOrDefault(r => r.Start <= date)!;

    public static DayCharges GetDayCharges(InverterDaySummaryPoint[] usage, DateOnly date)
    {
        if (usage.Length == 0)
        {
            return new DayCharges();
        }

        var dayRate = GetDayRate(date);

        var cost = dayRate.Import.StandingCharge;
        var savings = 0.0;
        var earned = 0.0;

        var lastTime = DateTime.Parse(usage.First().Time).AddMinutes(-5);
        foreach (var item in usage)
        {
            var time = DateTime.Parse(item.Time);
            var timeSpan = time - lastTime;
            var timeRate = timeSpan / sHour / 1000;
            lastTime = time;

            var costRate = time.Hour switch
            {
                >= 2 and <= 5 => dayRate.Import.NightRate,
                >= 16 and <= 19 => dayRate.Import.DayRate,
                _ => dayRate.Import.OffPeakRate,
            };

            if (item.GridPower > 0)
            {
                earned += item.GridPower * timeRate * costRate;
            }
            else if (item.GridPower < 0)
            {
                cost += -item.GridPower * timeRate * costRate;
            }

            var greenConsumption = item.Consumption + Math.Min(item.GridPower, 0);
            if (greenConsumption > 0)
            {
                savings += greenConsumption * timeRate * costRate;
            }
        }

        return new() { Date = date, Earned = earned / 100, Cost = cost / 100, Savings = savings / 100 };
    }
}

public sealed record DayCharges
{
    public DateOnly Date { get; init; }

    public double Earned { get; init; }

    public double Cost { get; init; }

    public double Savings { get; init; }

    public double Total => this.Earned + this.Savings - this.Cost;
}

public sealed record DayRate
{
    public DateOnly Start { get; init; }

    public ImportCharges Import { get; init; } = new();

    public Charges Export { get; init; } = new();
}

public sealed record ImportCharges : Charges
{
    public double StandingCharge { get; init; }
}

public record Charges
{
    public double NightRate { get; init; }

    public double DayRate { get; init; }

    public double OffPeakRate { get; init; }
}
