namespace Relicade.Core.Services
{
    public class LauncherProfile
    {
        public static LauncherProfile Default { get; } = new();

        public virtual string DisplayName => "Relicade";
        public virtual string ApplicationId => "Relicade";
        public virtual string Repository => "MrDiamondBallz/relicade";
        public virtual string ExecutableName => "Relicade";
        public virtual string DefaultInstallFolderName => "Apps";
        public virtual string UserAgent => "Relicade/1.0";
        public virtual string CliUserAgent => "Relicade-CLI";
        public virtual string UpdaterUserAgent => "Relicade-Updater";
        public virtual string SteamTag => DisplayName;
        public virtual void ConfigureInstalledApp(string appPath)
        {
        }
    }
}
