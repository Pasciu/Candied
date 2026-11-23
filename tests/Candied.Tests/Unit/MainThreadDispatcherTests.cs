namespace Candied.Tests.Unit;

using System;
using System.Threading;
using System.Threading.Tasks;
using Candied.VisionBridge.Protocol;
using Xunit;

public sealed class MainThreadDispatcherTests
{
    [Fact]
    public async Task Enqueue_ExecutesOnProcessQueue_AndReturnsResult()
    {
        var dispatcher = new MainThreadDispatcher();

        // Enqueue from a background task (simulating HTTP request thread)
        var task = Task.Run(() => dispatcher.Enqueue(() => "Computed Value", timeoutMs: 3000));

        // Wait until dispatcher sees pending item
        for (int i = 0; i < 50 && dispatcher.PendingCount == 0; i++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(1, dispatcher.PendingCount);

        // Process queue on this thread (simulating Unity main thread)
        dispatcher.ProcessQueue();

        string result = await task;
        Assert.Equal("Computed Value", result);
        Assert.Equal(0, dispatcher.PendingCount);
    }

    [Fact]
    public async Task Enqueue_WhenExceptionThrown_PropagatesToCaller()
    {
        var dispatcher = new MainThreadDispatcher();

        var task = Task.Run(() => dispatcher.Enqueue<string>(() => throw new InvalidOperationException("Unity test error"), timeoutMs: 3000));

        for (int i = 0; i < 50 && dispatcher.PendingCount == 0; i++)
        {
            await Task.Delay(10);
        }

        dispatcher.ProcessQueue();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Equal("Unity test error", ex.Message);
    }

    [Fact]
    public void Enqueue_WhenNotProcessedWithinTimeout_ThrowsTimeoutException()
    {
        var dispatcher = new MainThreadDispatcher();

        // Very short timeout (50ms), and do not call ProcessQueue
        Assert.Throws<TimeoutException>(() => dispatcher.Enqueue(() => 123, timeoutMs: 50));
    }

    [Fact]
    public async Task Clear_CancelsPendingItems()
    {
        var dispatcher = new MainThreadDispatcher();

        var task = Task.Run(() => dispatcher.Enqueue(() => "Never called", timeoutMs: 3000));

        for (int i = 0; i < 50 && dispatcher.PendingCount == 0; i++)
        {
            await Task.Delay(10);
        }

        dispatcher.Clear();

        await Assert.ThrowsAsync<OperationCanceledException>(() => task);
    }
}
