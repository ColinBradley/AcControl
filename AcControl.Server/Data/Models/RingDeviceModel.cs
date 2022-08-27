﻿namespace AcControl.Server.Data.Models;

using System.ComponentModel;

public class RingDeviceModel : INotifyPropertyChanged
{
    private string mDescription;
    private DateTime? mLatestSnapshotTime;
    private byte[]? mLatestSnapshot;

    public event PropertyChangedEventHandler? PropertyChanged;

    public RingDeviceModel(int id, string description)
    {
        mDescription = description;

        this.Id = id;
    }

    public int Id { get; }

    public string Description
    {
        get => mDescription;
        set
        {
            if (mDescription == value)
            {
                return;
            }

            mDescription = value;

            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.Description)));
        }
    }

    public byte[]? LatestSnapshot
    {
        get => mLatestSnapshot;
        set
        {
            if (mLatestSnapshot == value)
            {
                return;
            }

            mLatestSnapshot = value;

            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.LatestSnapshot)));
        }
    }

    public DateTime? LatestSnapshotTime
    {
        get => mLatestSnapshotTime;
        set
        {
            if (mLatestSnapshotTime == value)
            {
                return;
            }

            mLatestSnapshotTime = value;

            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.LatestSnapshotTime)));
        }
    }
}