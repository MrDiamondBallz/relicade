using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Relicade.Tests.HeadlessTestApp))]

namespace Relicade.Tests;

public static class HeadlessTestApp
{
    public static AppBuilder BuildAvaloniaApp()
    {
        App.SuppressDesktopHost = true;
        return AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
