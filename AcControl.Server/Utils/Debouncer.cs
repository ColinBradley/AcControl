namespace AcControl.Server.Utils;

using System.Timers;

public class Debouncer : IDisposable
{
    private readonly Timer mTimer;
    private bool mIsDisposed;

    public Debouncer(Func<Task> action, int intervalMs)
    {
        mTimer = new Timer(intervalMs);
        mTimer.Elapsed += async (_, _) =>
        {
            mTimer.Stop();

            await action();

            if (mIsDisposed)
            {
                return;
            }

            mTimer.Start();
        };
    }

    public void Ping()
    {
        mTimer.Stop();
        mTimer.Start();
    }

    public void Pause()
    {
        mTimer.Stop();
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
}
