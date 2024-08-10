namespace AcControl.Server.Pages;

using AcControl.Server.Data;
using AcControl.Server.Data.Models;
using ApexCharts;
using Microsoft.AspNetCore.Components;
using System;
using System.Threading.Tasks;

public partial class AirPage
{
    private List<AnnotationsYAxis> mCO2Annotations = [];

    private DateOnly mChartDate = DateOnly.FromDateTime(DateTime.Now);
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
            Enabled = false,
        };
        this.ChartOptions.Xaxis = new XAxis()
        {
            Type = XAxisType.Datetime,
            Labels = new XAxisLabels()
            {
                DatetimeUTC = false,
            },
        };
        this.ChartOptions.Yaxis = [
            new YAxis()
            {
                Labels = new YAxisLabels(),
            }
        ];
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

        this.ChartOptions.Annotations = new Annotations();
        var annotationLabelStyle = new Style()
        {
            Background = "none",
        };

        mCO2Annotations = [
            new AnnotationsYAxis()
            {
                Y = 0,
                Y2 = 800,
                FillColor = "#1de208",
                Opacity = .1,
            },
            new AnnotationsYAxis()
            {
                Y = 801,
                Y2 = 1000,
                FillColor = "#e2e020",
                Opacity = .1,
                Label = createLabel("Acceptable")
            },
            new AnnotationsYAxis()
            {
                Y = 1001,
                Y2 = 1500,
                FillColor = "#e26a05",
                Opacity = .1,
                Label = createLabel("Not Ideal"),
            },
            new AnnotationsYAxis()
            {
                Y = 1501,
                Y2 = 2000,
                FillColor = "#e20410",
                Opacity = .1,
                Label = createLabel("To Be Avoided"),
            },
            new AnnotationsYAxis()
            {
                Y = 2001,
                Y2 = 3000,
                FillColor = "#7f01e2",
                Opacity = .1,
                Label = createLabel("Unhealthy"),
            },
            new AnnotationsYAxis()
            {
                Y = 3001,
                Y2 = 4000,
                FillColor = "#903305",
                Opacity = .1,
                Label = createLabel("Very Unhealthy"),
            }
        ];

        this.UpdateChartYAxis();
        await this.UpdateCurrentDayEntries();

        Label createLabel(string text)
        {
            return new Label()
            {
                OffsetY = 16,
                Text = text,
                Style = annotationLabelStyle,
                BorderWidth = 0,
            };
        }
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
        this.UpdateChartYAxis();

        this.StateHasChanged();

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

        var end = mChartDate.ToDateTime(TimeOnly.MaxValue);
        var start = end.AddDays(-1.1);

        this.CurrentDayEntries = context.AirGradientSensorEntries.Where(e => e.DateTime > start && e.DateTime < end).OrderBy(e => e.DateTime).ToArray();
        
        // TODO: Should be able to remove this after initial cleanse
        var badEntries = this.CurrentDayEntries.Where(e => e.Atmp < -50).ToArray();
        if (badEntries.Length > 0)
        {
            context.AirGradientSensorEntries.RemoveRange(badEntries);
            await context.SaveChangesAsync();
        }

        if (this.CurrentDayEntries.Any())

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

    private void UpdateChartYAxis()
    {
        var yAxis = this.ChartOptions.Yaxis.First();

        yAxis.Min = this.ActiveSensorType switch
        {
            AirGradientSensorType.WiFiStrength => null,
            _ => 0,
        };
        yAxis.Max = this.ActiveSensorType switch
        {
            AirGradientSensorType.WiFiStrength => 0,
            _ => null,
        };
        yAxis.Labels.Formatter = this.ActiveSensorType switch
        {
            AirGradientSensorType.WiFiStrength => "v => v + ' dBm'",
            AirGradientSensorType.Atmp or AirGradientSensorType.AtmpCompensated => "v => v + ' °C'",
            AirGradientSensorType.Rhum or AirGradientSensorType.RhumCompensated => "v => v + ' %'",
            AirGradientSensorType.RCo2 => "v => v + ' ppm'",
            AirGradientSensorType.Pm01 or AirGradientSensorType.Pm02 or AirGradientSensorType.Pm10 => "v => v + ' μg/m³'",
            AirGradientSensorType.TvocRaw => "v => v + ' Ind30'",
            AirGradientSensorType.NoxRaw => "v => v + ' Ind40'",
            _ => null,
        };

        this.ChartOptions.Annotations.Yaxis = this.ActiveSensorType switch 
        { 
            AirGradientSensorType.RCo2 => mCO2Annotations, 
            _ => null, 
        };
    }

    internal static string HumanName(AirGradientSensorType sensorType) =>
        sensorType switch
        {
            AirGradientSensorType.WiFiStrength => "WiFi Strength",
            AirGradientSensorType.RCo2 => "CO2",
            AirGradientSensorType.Pm01 => "PM1",
            AirGradientSensorType.Pm02 => "PM2.5",
            AirGradientSensorType.Pm10 => "PM10",
            AirGradientSensorType.Pm003Count => "PM0.3",
            AirGradientSensorType.Atmp => "Temperature",
            AirGradientSensorType.AtmpCompensated => "Heat",
            AirGradientSensorType.Rhum => "Humidity",
            AirGradientSensorType.RhumCompensated => "Compensated Humidity",
            AirGradientSensorType.TvocIndex => "VOC Index",
            AirGradientSensorType.TvocRaw => "VOC",
            AirGradientSensorType.NoxIndex => "NOx Index",
            AirGradientSensorType.NoxRaw => "NOx",
        };
}