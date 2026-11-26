using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Candied.Vision.Controls;

namespace Candied.Vision.Ports;

public interface IGameControlProfileStore
{
    Task<GameControlProfile?> LoadProfileAsync(string profileFilePath, CancellationToken ct = default);

    Task SaveProfileAsync(string profileFilePath, GameControlProfile profile, CancellationToken ct = default);

    Task<GameControlProfile> MergeAndSaveActionsAsync(
        string profileFilePath,
        string targetId,
        string targetName,
        IReadOnlyList<ControlActionEntry> incomingActions,
        bool overwriteHigherPrioritySources = false,
        CancellationToken ct = default);
}
