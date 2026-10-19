using AsyncImageLoader;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Relicade.Services;

namespace Relicade.Tests;

public class MarkdownFenceNetworkTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Literal_fences_do_not_even_invoke_the_image_loader(bool closed)
    {
        var previous = ImageLoader.AsyncImageLoader;
        var loader = new ObservingLoader(); ImageLoader.AsyncImageLoader = loader;
        var panel = new StackPanel(); var window = new Window { Content = panel };
        try
        {
            var renderer = new MarkdownRenderer(_ => throw new InvalidOperationException("Unexpected URL navigation"));
            var url = "https://fixture.invalid/" + Guid.NewGuid().ToString("N") + ".png";
            foreach (var control in renderer.Render("```html\n<img src=\"" + url + "\">\n![cover](" + url + ")" + (closed ? "\n```" : ""))) panel.Children.Add(control);
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Equal(0, loader.Calls);
            panel.Children.Clear();
            // Positive control proves the observer is attached to the real image-rendering path.
            foreach (var control in renderer.Render("![outside code](" + url + ")")) panel.Children.Add(control);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.True(loader.Calls > 0);
        }
        finally { window.Close(); ImageLoader.AsyncImageLoader = previous; }
    }
    private sealed class ObservingLoader : IAsyncImageLoader
    {
        public int Calls;
        public Task<Bitmap?> ProvideImageAsync(string url) { Calls++; return Task.FromResult<Bitmap?>(null); }
        public void Dispose() { }
    }
}
