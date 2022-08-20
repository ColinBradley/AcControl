namespace AcControl.Server.Data
{
    using AcControl.Server.Utils;
    using System.Collections;
    using System.Collections.Concurrent;

    public class DevicesService : IDisposable, IEnumerable<AcUnitModel>
    {
        private readonly ToshibaAcHttpService mAcHttpService;
        private readonly ToshibaAcMqttService mMqttService;
        private readonly ConcurrentDictionary<string, AcUnitModel> mUnitsById = new();
        private readonly Debouncer mUpdateDebouncer;

        private int mSubscriptionCount = 0;

        private bool mIsDisposed = false;

        public delegate void ChangedEventHandler();
        public event ChangedEventHandler? Changed;

        public DevicesService(ToshibaAcHttpService httpService, ToshibaAcMqttService mqttService)
        {
            mAcHttpService = httpService;
            mMqttService = mqttService;

            mqttService.DeviceUpdated += this.MqttService_DeviceUpdated;

            // Note: We need to keep updating the values of units in order to get latest temperature values
            // They are not given push updates from the MQTT service
            mUpdateDebouncer = new Debouncer(
                async () =>
                {
                    if (mSubscriptionCount <= 0 || mIsDisposed)
                    {
                        return;
                    }

                    await this.UpdateUnits();
                },
                30_000
            );
        }

        private void MqttService_DeviceUpdated(string deviceId, string unitState)
        {
            if (mSubscriptionCount > 0 )
            {
                mUpdateDebouncer.Ping();
            }

            if (!mUnitsById.TryGetValue(deviceId, out var unit))
            {
                return;
            }

            _ = unit.Update(unitState);
        }

        public int? OutsideTemp => mUnitsById.Values.FirstOrDefault()?.OutdoorTemperature.Current;

        public async Task Subscribe()
        {
            if (Interlocked.Increment(ref mSubscriptionCount) != 1)
            {
                return;
            }

            await this.UpdateUnits();
        }

        public void Unsubscribe()
        {
            if (Interlocked.Decrement(ref mSubscriptionCount) != 0)
            {
                return;
            }

            mUpdateDebouncer.Pause();
        }

        public async Task UpdateUnits()
        {
            mUpdateDebouncer.Ping();

            var units = await mAcHttpService.GetAirConditionerUnits(CancellationToken.None);

            foreach (var unit in units)
            {
                _ = mUnitsById.AddOrUpdate(
                    unit.DeviceUniqueId,
                    _ =>
                    {
                        var model = new AcUnitModel(unit);
                        model.Changed += this.Model_Changed;
                        return model;
                    },
                    (_, model) => model.Update(unit.ACStateData));
            }
        }

        private void Model_Changed()
        {
            this.Changed?.Invoke();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (mIsDisposed)
            {
                return;
            }

            mIsDisposed = true;

            if (disposing)
            {
                mUpdateDebouncer.Dispose();
                mMqttService.DeviceUpdated -= this.MqttService_DeviceUpdated;
            }
        }

        public void Dispose()
        {
            this.Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        IEnumerator<AcUnitModel> IEnumerable<AcUnitModel>.GetEnumerator() => mUnitsById.Values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => mUnitsById.Values.GetEnumerator();
    }
}
