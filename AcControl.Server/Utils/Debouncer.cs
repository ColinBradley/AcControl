namespace AcControl.Server.Utils;

using System.Timers;

public class Debouncer : IDisposable
{
    private readonly Timer mTimer;
    private bool mIsDisposed;
    private bool mIsPaused = false;

    public Debouncer(Func<Task> action, int intervalMs)
    {
        mTimer = new Timer(intervalMs);
        mTimer.Elapsed += async (_, _) =>
        {
            mTimer.Stop();

            await action();

            if (mIsDisposed || mIsPaused)
            {
                return;
            }

            mTimer.Start();
        };
    }

    public void Ping()
    {
        mIsPaused = false;

        mTimer.Stop();
        mTimer.Start();
    }

    public void Pause()
    {
        mIsPaused = true;

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
