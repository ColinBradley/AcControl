namespace AcControl.Server.Pages;

using AcControl.Server.Data;
using AcControl.Server.Data.Models;
using ApexCharts;
using Microsoft.AspNetCore.Components;
using System;
using System.Linq;
using System.Threading.Tasks;

public partial class PowerPage
{
    private InverterDaySummaryPoint[] mCurrentSummary = [];
    private DateOnly mChartDate = DateOnly.FromDateTime(DateTime.UtcNow);
    private bool mIsDisposed = false;

    private string DateAsText => mChartDate.ToString("yyyy-MM-dd");
    private ApexChartOptions<InverterDaySummaryPoint> ChartOptions { get; } = new();
    private ApexChart<InverterDaySummaryPoint>? Chart { get; set; }

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
                Formatter = "v => v + ' W'",
            }
        }];
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

        this.Power.Changed += this.Power_Changed;

        await this.Power.Subscribe();
    }

    private async Task PreviousDate_Click()
    {
        mChartDate = mChartDate.AddDays(-1);

        await this.UpdateData();
    }

    private async Task NextDate_Click()
    {
        if (this.DateAsText == LuxPowerTekService.TodaysDateString)
        {
            return;
        }

        mChartDate = mChartDate.AddDays(1);

        await this.UpdateData();
    }

    private async Task DatePicker_Input(ChangeEventArgs e)
    {
        var value = e.Value as string;
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        mChartDate = DateOnly.Parse(value);

        await this.UpdateData();
    }

    private async Task LoadAll_Click()
    {
        var inverter = this.Power.Inverters.FirstOrDefault();
        if (inverter is null)
        {
            return;
        }

        var blankCount = 0;
        for (var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1); !mIsDisposed; date = date.AddDays(-1))
        {
            var summary = await this.Power.GetDaySummary(inverter, date.ToString("yyyy-MM-dd"), default);

            _ = this.InvokeAsync(this.StateHasChanged);

            if (summary.Length == 0 && ++blankCount > 15)
            {
                break;
            }
        }
    }

    private async void Power_Changed()
    {
        await this.UpdateData();
    }

    public void Dispose()
    {
        mIsDisposed = true;
        this.Power.Unsubscribe();
        this.Power.Changed -= this.Power_Changed;
    }

    protected override async Task OnAfterRenderAsync(bool isFirstRender)
    {
        if (!isFirstRender)
        {
            return;
        }

        await this.UpdateData();
    }

    private async Task UpdateData()
    {
        var inverter = this.Power.Inverters.FirstOrDefault();
        if (inverter is null)
        {
            return;
        }

        var summary = await this.Power.GetDaySummary(inverter, this.DateAsText, default);

        this.SetChartData(summary);
    }

    private void SetChartData(InverterDaySummaryPoint[] summary)
    {
        if (mCurrentSummary == summary)
        {
            return;
        }
        mCurrentSummary = summary;

        this.InvokeAsync(() =>
        {
            this.Chart?.RenderAsync();
            this.StateHasChanged();
        });
    }

    public static string ToHumanReadableString(TimeSpan time)
    {
        if (time.TotalDays >= 365)
        {
            var remainingDays = time.TotalDays % 365;
            return $"{Math.Floor(time.TotalDays / 365)} years, {Math.Floor(remainingDays / 30)} months, {Math.Floor(remainingDays % 30)} days";
        }
        if (time.TotalDays >= 30)
        {
            return $"{Math.Floor(time.TotalDays / 30)} months, {Math.Floor(time.TotalDays % 30)} days";
        }

        return $@"{time:%d} days";
    }
}