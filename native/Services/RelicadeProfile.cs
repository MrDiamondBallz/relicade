using Relicade.Core.Services;

namespace Relicade.Services
{
    public sealed class RelicadeProfile : LauncherProfile
    {
        public static RelicadeProfile Instance { get; } = new();

        public override string DisplayName => "Relicade";
        public override string ApplicationId => "Relicade";
        public override string Repository => "MrDiamondBallz/relicade";
        public override string ExecutableName => "Relicade";
        public override string DefaultInstallFolderName => "Apps";
        public override string UserAgent => "Relicade/1.0";
        public override string CliUserAgent => "Relicade-CLI";
        public override string UpdaterUserAgent => "Relicade-Updater";
        public override string SteamTag => "Relicade";
    }
}