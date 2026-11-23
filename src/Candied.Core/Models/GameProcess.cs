using System;
using System.Collections.Generic;
using Candied.Primitives;

namespace Candied.Core.Models;

public sealed record GameProcessInfo(
    ProcessId Id,
    string ProcessName,
    string MainWindowTitle,
    bool IsResponding,
    bool HasBridgeAttached,
    DateTime StartTime
);

public sealed record GameLaunchOptions(
    bool Windowed = true,
    int WindowWidth = 1280,
    int WindowHeight = 720,
    IReadOnlyList<string>? CustomArguments = null
);
