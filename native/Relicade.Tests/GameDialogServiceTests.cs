using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Relicade.Services;

namespace Relicade.Tests;

public class GameDialogServiceTests
{
    [AvaloniaFact]
    public async Task ShowWindowAsync_completes_when_window_closes()
    {
        var window = new Window
        {
            Width = 240,
            Height = 120,
            Title = "Test Dialog",
        };

        window.Opened += (_, _) => window.Close();

        await GameDialogService.ShowWindowAsync(window).WaitAsync(TimeSpan.FromSeconds(5));
    }
}
