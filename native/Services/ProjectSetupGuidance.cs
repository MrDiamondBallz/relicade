using Relicade.Core.Services;
using Relicade.Models;

namespace Relicade.Services;

/// <summary>Source-backed preparation advice, never installation or gameplay approval.</summary>
internal sealed record ProjectSetupGuidance(string Heading, string Description, bool HasSpecificGuide = true)
{
    internal const string GuideUrl = "https://github.com/MrDiamondBallz/relicade/blob/main/docs/GAME_SETUP.md";

    internal static ProjectSetupGuidance For(GameInfo? game)
    {
        if (game != null &&
            RepositorySourceHelper.Normalize(game.RepositorySource, out var unsupported) == "github" && !unsupported)
        {
            var repository = game.Repository;
            if (string.Equals(repository, "troyedwardsjr/z2rs", StringComparison.OrdinalIgnoreCase))
                return new("Original game files required",
                    "Have your lawful Zelda II USA ROM ready. For a new managed Library entry, Set up game supports z2rs v0.6.0 on Linux x64 and asks you to confirm a separate local file copy. No ROM is downloaded; other versions and platforms need their upstream setup instructions.");
            if (string.Equals(repository, "perfect-dark-pc-port/perfect_dark", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(repository, "fgsfdsfgs/perfect_dark", StringComparison.OrdinalIgnoreCase))
                return new("Original game files required",
                    "This engine needs your lawful Perfect Dark game data. Relicade's local import supports Perfect Dark US 1.1 (.z64) with the pinned Linux x64 ci-dev-build package described in the setup guide. Add to Library, install the supported engine, then choose Finish game setup. Other packages need upstream instructions; no ROM is downloaded.");
            if (string.Equals(repository, "UZDoom/UZDoom", StringComparison.OrdinalIgnoreCase))
                return new("Choose your game data",
                    "UZDoom is an engine, not the commercial Doom game data. In Library, the optional Freedoom Phase 2 setup can fetch the free game data for the supported UZDoom 5.0.3 Linux x64 package. Or keep your existing lawful WAD setup and use ordinary Launch. Commercial game files are not downloaded.");
            if (string.Equals(repository, "taisei-project/taisei", StringComparison.OrdinalIgnoreCase))
                return new("Complete-game package",
                    "No original-game ROM is needed for the official Taisei v1.4.6 Linux x64 package: it includes the game's data. Add to Library to inspect its downloads. Relicade has checked this package's contents, not gameplay or its complete third-party notice coverage. Other packages have not been reviewed.");
        }
        return new("Requirements not reviewed",
            "Check the project's upstream README before downloading. Some packages include a complete game; others need your own game files or additional setup. Adding to Library or installing an executable does not establish that it is ready to play. Open repository for the project's instructions.", HasSpecificGuide: false);
    }
}
