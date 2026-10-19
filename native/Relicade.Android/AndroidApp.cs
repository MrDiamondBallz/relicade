using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace Relicade.Android;

[Application(Name = "io.github.mrdiamondballz.relicade.AndroidApp")]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    protected AndroidApp(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
