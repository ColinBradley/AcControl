namespace AcControl.Server.Data
{
    using System.Collections;
    using System.Collections.Concurrent;
    using System.Timers;

    public class DevicesService : IDisposable, IEnumerable<AcUnitModel>
    {
        private readonly ToshibaAcHttpService mAcHttpService;
        private readonly Timer mTimer;
        private readonly ConcurrentDictionary<string, AcUnitModel> mUnitsById = new();

        private int mSubscriptionCount = 0;

        private bool mIsDisposed = false;

        public delegate void ChangedEventHandler();
        public event ChangedEventHandler? Changed;

        public DevicesService(ToshibaAcHttpService httpService)
        {
            mAcHttpService = httpService;

            mTimer = new Timer();
            mTimer.Elapsed += this.Timer_Tick;
            mTimer.Interval = TimeSpan.FromSeconds(5).TotalMilliseconds;
        }

        public int? OutsideTemp => mUnitsById.Values.FirstOrDefault()?.OutdoorTemperature.Current;

        public async Task Subscribe()
        {
            if (Interlocked.Increment(ref mSubscriptionCount) != 1)
            {
                return;
            }

            mTimer.Start();

            await this.UpdateUnits();
        }

        public void Unsubscribe()
        {
            if (Interlocked.Decrement(ref mSubscriptionCount) != 0)
            {
                return;
            }

            mTimer.Stop();
        }

        private async Task UpdateUnits()
        {
            var units = await mAcHttpService.GetAirConditionerUnits(CancellationToken.None);

            foreach (var unit in units)
            {
                mUnitsById.AddOrUpdate(
                    unit.Id,
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

        private async void Timer_Tick(object? sender, ElapsedEventArgs e)
        {
            await this.UpdateUnits();
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
                mTimer.Dispose();
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
