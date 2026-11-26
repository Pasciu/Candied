using System;
using System.Collections.Generic;
using System.Threading;

namespace Candied.Vision.Protocol;

public sealed class MainThreadDispatcher
{
    private readonly object _lock = new object();
    private readonly Queue<MainThreadWorkItem> _queue = new Queue<MainThreadWorkItem>();

    public int PendingCount
    {
        get
        {
            lock (_lock)
            {
                return _queue.Count;
            }
        }
    }

    public T Enqueue<T>(Func<T> func, int timeoutMs = 5000)
    {
        if (func == null)
        {
            throw new ArgumentNullException(nameof(func));
        }

        using (var item = new MainThreadWorkItem(() => func()))
        {
            lock (_lock)
            {
                _queue.Enqueue(item);
            }

            object? result = item.Wait(timeoutMs);
            return (T)result!;
        }
    }

    public void EnqueueAction(Action action, int timeoutMs = 5000)
    {
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        Enqueue<object?>(() =>
        {
            action();
            return null;
        }, timeoutMs);
    }

    public void ProcessQueue()
    {
        while (true)
        {
            MainThreadWorkItem? item = null;
            lock (_lock)
            {
                if (_queue.Count > 0)
                {
                    item = _queue.Dequeue();
                }
            }

            if (item == null)
            {
                break;
            }

            item.Execute();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            while (_queue.Count > 0)
            {
                var item = _queue.Dequeue();
                item.Abort(new OperationCanceledException("Dispatcher queue was cleared."));
            }
        }
    }

    private sealed class MainThreadWorkItem : IDisposable
    {
        private readonly Func<object?> _func;
        private readonly ManualResetEvent _doneEvent = new ManualResetEvent(false);
        private object? _result;
        private Exception? _exception;
        private bool _isDisposed;

        public MainThreadWorkItem(Func<object?> func)
        {
            _func = func;
        }

        public void Execute()
        {
            if (_isDisposed)
            {
                return;
            }

            try
            {
                _result = _func();
            }
            catch (Exception ex)
            {
                _exception = ex;
            }
            finally
            {
                if (!_isDisposed)
                {
                    _doneEvent.Set();
                }
            }
        }

        public void Abort(Exception ex)
        {
            if (_isDisposed)
            {
                return;
            }

            _exception = ex;
            _doneEvent.Set();
        }

        public object? Wait(int timeoutMs)
        {
            if (!_doneEvent.WaitOne(timeoutMs, false))
            {
                throw new TimeoutException("Engine main thread did not execute operation within " + timeoutMs + "ms.");
            }

            if (_exception != null)
            {
                throw _exception;
            }

            return _result;
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                _doneEvent.Close();
            }
        }
    }
}
