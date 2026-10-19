using Relicade.Core.Models;
using Relicade.Models;

namespace Relicade.Services;

public static class AppUpdateSelection
{
    public static bool IsManualPendingUpdate(GameInfo game) =>
        game.Status == GameStatus.UpdateAvailable && (!game.AutoUpdate || game.IsWindowsInstaller);

    public static bool IsAutoPendingUpdate(GameInfo game) =>
        game.Status == GameStatus.UpdateAvailable && game.AutoUpdate && !game.IsWindowsInstaller;

    public static List<GameInfo> GetManualPendingUpdates(IEnumerable<GameInfo> games) =>
        games
            .Where(IsManualPendingUpdate)
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static List<GameInfo> GetAutoPendingUpdates(IEnumerable<GameInfo> games) =>
        games
            .Where(IsAutoPendingUpdate)
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static int CountManualPendingUpdates(IEnumerable<GameInfo> games) =>
        games.Count(IsManualPendingUpdate);
}
