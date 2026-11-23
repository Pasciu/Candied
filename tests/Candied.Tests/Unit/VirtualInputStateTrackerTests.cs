namespace Candied.Tests.Unit;

using Candied.VisionBridge.Protocol;
using Xunit;

public sealed class VirtualInputStateTrackerTests
{
    [Fact]
    public void InitialState_IsNotIsolatedAndButtonsAreReleased()
    {
        var tracker = new VirtualInputStateTracker();

        Assert.False(tracker.IsIsolated);
        Assert.Equal(0f, tracker.ScreenX);
        Assert.Equal(0f, tracker.ScreenY);
        Assert.Equal((ushort)0, tracker.ButtonsMask);
        Assert.False(tracker.IsButtonHeld(0));
        Assert.False(tracker.IsButtonDownOnFrame(0, 10));
        Assert.False(tracker.IsButtonUpOnFrame(0, 10));
    }

    [Fact]
    public void UpdateMouseState_ActivatesIsolation_AndTracksCoordinatesAndButtonEdges()
    {
        var tracker = new VirtualInputStateTracker();

        // Step 1: Pre-hover at (512, 270) on frame 100
        tracker.UpdateMouseState(512f, 270f, buttonsMask: 0, clickCount: 0, currentFrame: 100);
        Assert.True(tracker.IsIsolated);
        Assert.Equal(512f, tracker.ScreenX);
        Assert.Equal(270f, tracker.ScreenY);
        Assert.False(tracker.IsButtonHeld(0));
        Assert.False(tracker.IsButtonDownOnFrame(0, 101));

        // Step 2: Press left button (mask = 1) on frame 104
        tracker.UpdateMouseState(512f, 270f, buttonsMask: 1, clickCount: 1, currentFrame: 104);
        Assert.True(tracker.IsButtonHeld(0));
        Assert.True(tracker.IsButtonDownOnFrame(0, 105));
        Assert.False(tracker.IsButtonDownOnFrame(0, 106)); // Not a down edge on subsequent hold frames

        // Step 3: Hold left button on frame 105
        tracker.UpdateMouseState(512f, 270f, buttonsMask: 1, clickCount: 1, currentFrame: 105);
        Assert.True(tracker.IsButtonHeld(0));
        Assert.False(tracker.IsButtonDownOnFrame(0, 106));

        // Step 4: Release left button (mask = 0) on frame 110
        tracker.UpdateMouseState(512f, 270f, buttonsMask: 0, clickCount: 0, currentFrame: 110);
        Assert.False(tracker.IsButtonHeld(0));
        Assert.True(tracker.IsButtonUpOnFrame(0, 111));
        Assert.False(tracker.IsButtonUpOnFrame(0, 112));
    }

    [Fact]
    public void Reset_ClearsIsolationAndButtonStates()
    {
        var tracker = new VirtualInputStateTracker();
        tracker.UpdateMouseState(300f, 400f, buttonsMask: 1, clickCount: 1, currentFrame: 10);

        tracker.Reset();

        Assert.False(tracker.IsIsolated);
        Assert.Equal(0f, tracker.ScreenX);
        Assert.Equal(0f, tracker.ScreenY);
        Assert.False(tracker.IsButtonHeld(0));
        Assert.False(tracker.IsButtonDownOnFrame(0, 11));
    }

    [Fact]
    public void SetIsolated_TogglesBetweenAiVirtualMouseAndManualPhysicalMouse()
    {
        var tracker = new VirtualInputStateTracker();
        tracker.UpdateMouseState(640f, 360f, buttonsMask: 1, clickCount: 1, currentFrame: 20);
        Assert.True(tracker.IsIsolated);

        tracker.SetIsolated(false);
        Assert.False(tracker.IsIsolated);
        Assert.False(tracker.IsButtonHeld(0));

        tracker.SetIsolated(true);
        Assert.True(tracker.IsIsolated);
    }
}
