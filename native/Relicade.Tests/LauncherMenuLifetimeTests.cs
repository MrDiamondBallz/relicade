using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Relicade.Services;

namespace Relicade.Tests;

public sealed class LauncherMenuLifetimeTests
{
    [AvaloniaFact]
    public void Failed_open_without_a_window_leaves_no_active_input_menu()
    {
        var menu = new ContextMenu { Items = { new MenuItem { Header = "Synthetic action" } } };
        using var owner = new LauncherMenuController(new UserControl(), () => { }, () => { });
        try
        {
            owner.Open(new Button(), menu);

            Assert.False(menu.IsOpen);
            Assert.False(GamepadContextMenuNavigation.Instance.HasActiveContextMenu);
        }
        finally { GamepadContextMenuNavigation.Instance.UnregisterContextMenu(menu); }
    }
}
