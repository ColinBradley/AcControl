namespace AcControl.Server.Pages;

using AcControl.Server.Data;
using AcControl.Server.Data.Models;
using ApexCharts;
using Microsoft.AspNetCore.Components;
using System;
using System.Threading.Tasks;

public partial class AirPage
{
    private DateOnly mChartDate = DateOnly.FromDateTime(DateTime.UtcNow);
    private bool mIsDisposed = false;

    private string DateAsText => mChartDate.ToString("yyyy-MM-dd");

    private ApexChartOptions<AirGradientSensorEntry> ChartOptions { get; } = new();

    private ApexChart<AirGradientSensorEntry>? Chart { get; set; }

    private AirGradientSensorEntry[] CurrentDayEntries { get; set; } = [];

    private AirGradientSensorType ActiveSensorType { get; set; } = AirGradientSensorType.Atmp;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        this.ChartOptions.Theme = new Theme()
        {
            Mode = Mode.Dark,
        };
        this.ChartOptions.Chart.Background = "0000";
        this.ChartOptions.Chart.Animations = new Animations()
        {
            Speed = 100,
            AnimateGradually = new AnimateGradually()
            {
                Enabled = false,
            }
        };
        this.ChartOptions.Xaxis = new XAxis()
        {
            Type = XAxisType.Datetime,
            Labels = new XAxisLabels()
            {
                Format = "HH:mm",
                DatetimeUTC = false,
            },
        };
        this.ChartOptions.Stroke = new Stroke()
        {
            Curve = Curve.Smooth,
            Width = 3,
        };
        this.ChartOptions.Tooltip = new Tooltip()
        {
            X = new TooltipX()
            {
                Format = "HH:mm",
                Show = false,
            }
        };
        this.ChartOptions.Grid = new Grid()
        {
            BorderColor = "#333",
        };

        await this.UpdateCurrentDayEntries();
    }

    private async Task DatePicker_Input(ChangeEventArgs e)
    {
        var value = e.Value as string;
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        mChartDate = DateOnly.Parse(value);

        await this.UpdateCurrentDayEntries();
    }

    private async Task RefreshButton_Click()
    {
        await this.UpdateCurrentDayEntries();
    }

    private async Task SensorTypeButton_Click(AirGradientSensorType sensorType)
    {
        this.ActiveSensorType = sensorType;

        if (this.Chart is not null)
        {
            await this.Chart.RenderAsync();
        }
    }

    private async Task PreviousDate_Click()
    {
        mChartDate = mChartDate.AddDays(-1);

        await this.UpdateCurrentDayEntries();
    }

    private async Task NextDate_Click()
    {
        if (this.DateAsText == LuxPowerTekService.TodaysDateString)
        {
            return;
        }

        mChartDate = mChartDate.AddDays(1);

        await this.UpdateCurrentDayEntries();
    }

    private async Task UpdateCurrentDayEntries()
    {
        using var context = await this.HomeDbFactory.CreateDbContextAsync();

        var start = mChartDate.ToDateTime(TimeOnly.MinValue);
        var end = mChartDate.ToDateTime(TimeOnly.MaxValue);

        this.CurrentDayEntries = context.AirGradientSensorEntries.Where(e => e.DateTime > start && e.DateTime < end).OrderBy(e => e.DateTime).ToArray();

        await this.InvokeAsync(async () => 
        {
            if (this.Chart is not null)
            {
                try
                {
                    await this.Chart.RenderAsync();
                } 
                catch (NullReferenceException ex)
                {
                    // ¬_¬
                }
            }
        });
    }

    private static string HumanName(AirGradientSensorType sensorType) =>
        sensorType switch
        {
            AirGradientSensorType.WiFiStrength => "WiFi Strength",
            AirGradientSensorType.RCo2 => "Relative CO2",
            AirGradientSensorType.Pm01 => "Pm01",
            AirGradientSensorType.Pm02 => "Pm02",
            AirGradientSensorType.Pm10 => "Pm10",
            AirGradientSensorType.Pm003Count => "Pm003 Count",
            AirGradientSensorType.Atmp => "Temperature",
            AirGradientSensorType.AtmpCompensated => "Compensated Temperature",
            AirGradientSensorType.Rhum => "Humidity",
            AirGradientSensorType.RhumCompensated => "Compensated Humidity",
            AirGradientSensorType.TvocIndex => "Total Voc Index",
            AirGradientSensorType.TvocRaw => "Total Voc",
            AirGradientSensorType.NoxIndex => "Nox Index",
            AirGradientSensorType.NoxRaw => "Nox",
        };
    
}