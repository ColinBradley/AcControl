namespace AcControl.Server.Pages;

using AcControl.Server.Data.Models;
using ApexCharts;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

public partial class PowerSummaryPage
{
    private ApexChartOptions<InverterDaySummary> ChartOptions { get; } = new();
    private ApexChart<InverterDaySummary>? Chart { get; set; }

    private InverterDaySummary[] Summary { get; set; } = [];

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
        this.ChartOptions.Yaxis = [new YAxis() {
            Labels = new YAxisLabels() {
                Formatter = "v => v + ' kW'",
            }
        }];
        this.ChartOptions.Xaxis = new XAxis()
        {
            Type = XAxisType.Datetime,
        };
        this.ChartOptions.Stroke = new Stroke()
        {
            Curve = Curve.Smooth,
            Width = 3,
        };
        this.ChartOptions.Grid = new Grid()
        {
            BorderColor = "#333",
        };
    }

    protected override async Task OnAfterRenderAsync(bool isFirstRender)
    {
        await base.OnAfterRenderAsync(isFirstRender);

        if (!isFirstRender)
        {
            return;
        }

        await this.UpdateData();
    }

    private async Task UpdateData()
    {
        using var context = this.Db.CreateDbContext();
        var cutOffDate = DateOnly.FromDateTime(DateTime.Now).AddYears(-1);

        var entries = await context.InverterDaySummaries
            .Where(e => e.Date > cutOffDate)
            .ToArrayAsync();

        this.Summary = entries
            .Select(dayEntry =>
                new InverterDaySummary()
                {
                    Consumption = dayEntry.Entries.Sum(e => e.Consumption),
                    GridPower = dayEntry.Entries.Sum(e => e.GridPower),
                    SolarPv = dayEntry.Entries.Sum(e => e.SolarPv),
                    Day = dayEntry.Date,
                }
            )
            .ToArray();

        // Have to wait for the chart to be ready.. but it's already supposed to be ready, and I hate everything
        await Task.Delay(500);

        this.Chart?.RenderAsync();
        this.StateHasChanged();
    }
}