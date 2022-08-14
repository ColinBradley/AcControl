namespace AcControl.Server.Data
{
    using System.Collections;
    using System.Collections.Concurrent;
    
    public class DevicesService : IDisposable, IEnumerable<AcUnitModel>
    {
        private readonly ToshibaAcHttpService mAcHttpService;
        private readonly ToshibaAcMqttService mMqttService;
        private readonly ConcurrentDictionary<string, AcUnitModel> mUnitsById = new();

        private int mSubscriptionCount = 0;

        private bool mIsDisposed = false;

        public delegate void ChangedEventHandler();
        public event ChangedEventHandler? Changed;

        public DevicesService(ToshibaAcHttpService httpService, ToshibaAcMqttService mqttService)
        {
            mAcHttpService = httpService;
            mMqttService = mqttService;

            mqttService.DeviceUpdated += this.MqttService_DeviceUpdated;
        }

        private void MqttService_DeviceUpdated(string deviceId, string unitState)
        {
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
        }

        public async Task UpdateUnits()
        {
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
