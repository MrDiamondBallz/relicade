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
    public async Task Literal_fences_do_not_even_invoke_the_image_loader(bool closed)
    {
        var previous = ImageLoader.AsyncImageLoader;
        var loader = new ObservingLoader(); ImageLoader.AsyncImageLoader = loader;
        var panel = new StackPanel(); var window = new Window { Content = panel };
        try
        {
            var renderer = new MarkdownRenderer(_ => throw new InvalidOperationException("Unexpected URL navigation"));
            var url = "https://fixture.invalid/" + Guid.NewGuid().ToString("N") + ".png";
            // Complete a positive control first: image loading is dispatched asynchronously.
            foreach (var control in renderer.Render("![outside code](" + url + ")")) panel.Children.Add(control);
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            await loader.Called.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.True(loader.Calls > 0);
            panel.Children.Clear();
            var codeLoader = new ObservingLoader(); ImageLoader.AsyncImageLoader = codeLoader;
            var blocks = renderer.Render("```html\n<img src=\"" + url + "\">\n![cover](" + url + ")" + (closed ? "\n```" : ""));
            Assert.IsType<SelectableTextBlock>(Assert.IsType<Border>(Assert.Single(blocks)).Child);
            foreach (var control in blocks) panel.Children.Add(control);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            await Task.Delay(50, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, codeLoader.Calls);
        }
        finally { window.Close(); ImageLoader.AsyncImageLoader = previous; }
    }
    private sealed class ObservingLoader : IAsyncImageLoader
    {
        public int Calls;
        public TaskCompletionSource Called = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<Bitmap?> ProvideImageAsync(string url) { Interlocked.Increment(ref Calls); Called.TrySetResult(); return Task.FromResult<Bitmap?>(null); }
        public void Dispose() { }
    }
}
