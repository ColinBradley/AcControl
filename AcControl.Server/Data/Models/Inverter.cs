﻿namespace AcControl.Server.Data.Models;

using System.Collections.Concurrent;

public class Inverter
{
    public Inverter(InverterData inverterData)
    {
        this.InverterData = inverterData;
    }

    public bool IsFullyLoaded => this.EnergyData != null && this.RuntimeData != null && !this.DaySummariesByDate.IsEmpty;

    public InverterData InverterData { get; set; }

    public InverterEnergyData? EnergyData { get; set; }

    public InverterRuntimeData? RuntimeData { get; set; }

    public ConcurrentDictionary<string, InverterDaySummaryPoint[]> DaySummariesByDate { get; } = new();

    public int ConsumptionPower
    {
        get
        {
            if (this.RuntimeData is null)
            {
                return 0;
            }

            return (this.InverterData.DeviceType == 2 ? this.RuntimeData.Ppv1 : 0)
                + (this.RuntimeData.Pinv - this.RuntimeData.Prec)
                + (this.RuntimeData.PToUser - this.RuntimeData.PToGrid);
        }
    }
}

public record InverterData
{
    public bool WithbatteryData { get; set; }
    public string? WarrantyExpireDate { get; set; }
    public required string DeviceTypeText { get; set; }
    public int ServerId { get; set; }
    public int PowerRating { get; set; }
    public int Dtc { get; set; }
    public bool ParallelEnabled { get; set; }
    public bool Lost { get; set; }
    public required string DatalogSn { get; set; }
    public int Model { get; set; }
    public string? CommissionDate { get; set; }
    public int BatCapacity { get; set; }
    public int Phase { get; set; }
    public int DeviceType { get; set; }
    public required string SerialNum { get; set; }
    public required string FwCode { get; set; }
    public int PlantId { get; set; }
    public int SubDeviceType { get; set; }
    public required string SohText { get; set; }
    public required string EndUser { get; set; }
    public int BatParallelNum { get; set; }
    public required string PowerRatingText { get; set; }
    public required string PlantName { get; set; }
    public int Status { get; set; }
    public required string LastUpdateTime { get; set; }
}

public record InverterEnergyData
{
    public required string TotalCoalReductionText { get; set; }
    public required string TotalUsageText { get; set; }
    public bool HasRuntimeData { get; set; }
    public required string TodaySavingText { get; set; }
    public required string TodayUsageText { get; set; }
    public int TodayCharging { get; set; }
    public int PvPieUsageTodayRate { get; set; }
    public int TodayYielding { get; set; }
    public int TodayDischarging { get; set; }
    public int TotalDischarging { get; set; }
    public required string TotalSavingText { get; set; }
    public required string TodayExportText { get; set; }
    public required string TotalChargingText { get; set; }
    public int TodayImport { get; set; }
    public int PvPieChargeTotalRate { get; set; }
    public required string TodayYieldingText { get; set; }
    public int TotalUsage { get; set; }
    public required string TodayDischargingText { get; set; }
    public int PvPieChargeTodayRate { get; set; }
    public required string TotalIncomeText { get; set; }
    public required string SerialNum { get; set; }
    public required string TotalDischargingText { get; set; }
    public int TodayUsage { get; set; }
    public int TotalExport { get; set; }
    public required string TotalImportText { get; set; }
    public int PvPieExportTotalRate { get; set; }
    public required string TotalYieldingText { get; set; }
    public required string TotalExportText { get; set; }
    public int PvPieUsageTotalRate { get; set; }
    public required string TodayIncomeText { get; set; }
    public int TotalYielding { get; set; }
    public int TodayExport { get; set; }
    public int PvPieExportTodayRate { get; set; }
    public bool Success { get; set; }
    public required string TodayChargingText { get; set; }
    public int TotalCharging { get; set; }
    public required string TodayImportText { get; set; }
    public required string TotalCo2ReductionText { get; set; }
    public int TotalImport { get; set; }
}

public record InverterRuntimeData
{
    public int Vpv3 { get; set; }
    public int Vpv2 { get; set; }
    public int PDisCharge { get; set; }
    public int Vpv1 { get; set; }
    public int Soc { get; set; }
    public int Tradiator2 { get; set; }
    public int VBat { get; set; }
    public int Fac { get; set; }
    public int Peps { get; set; }
    public int MaxDischgCurrValue { get; set; }
    public int Tinner { get; set; }
    public int Tradiator1 { get; set; }
    public bool HasUnclosedQuickChargeTask { get; set; }
    public int Prec { get; set; }
    public bool Lost { get; set; }
    public required string ServerTime { get; set; }
    public required string SerialNum { get; set; }
    public int PToUser { get; set; }
    public int Feps { get; set; }
    public int PCharge { get; set; }
    public int TBat { get; set; }
    public int Pinv { get; set; }
    public int MaxChgCurr { get; set; }
    public bool Success { get; set; }
    public int PToGrid { get; set; }
    public int Status { get; set; }
    public bool HasRuntimeData { get; set; }
    public bool HaspEpsLNValue { get; set; }
    public int Ppv2 { get; set; }
    public int Ppv3 { get; set; }
    public required string DeviceTime { get; set; }
    public int Ppv1 { get; set; }
    public int VBus2 { get; set; }
    public int VBus1 { get; set; }
    public int BatCapacity { get; set; }
    public int Seps { get; set; }
    public required string FwCode { get; set; }
    public int RemainTime { get; set; }
    public int Vact { get; set; }
    public int Vacs { get; set; }
    public int Vepst { get; set; }
    public int Vacr { get; set; }
    public int Vepsr { get; set; }
    public int Vepss { get; set; }
    public required string Pf { get; set; }
    public required string StatusText { get; set; }
    public int Ppv { get; set; }
    public required string BatteryColor { get; set; }
    public required string BatParallelNum { get; set; }
    public required string BatteryType { get; set; }
    public int MaxDischgCurr { get; set; }
    public int MaxChgCurrValue { get; set; }
}

public record InverterDaySummaryPoint
{
    public int SolarPv { get; set; }
    public int GridPower { get; set; }
    public int BatteryDischarging { get; set; }
    public int Month { get; set; }
    public int Hour { get; set; }
    public int Year { get; set; }
    public int Consumption { get; set; }
    public required string Time { get; set; }
    public int Day { get; set; }
    public int Minute { get; set; }
    public int Second { get; set; }
}

public record InverterDaySummary
{
    public required int SolarPv { get; init; }
    
    public required int GridPower { get; init; }
    
    public required int Consumption { get; init; }

    public required DateOnly Day { get; init; }
}