using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;
namespace Relicade.Tests;
[Collection(SharedCatalogRenderer.CollectionName)]
public sealed class BrowseReleaseRetryRenderingTests(SharedCatalogRenderer renderer)
{
    [Theory]
    [InlineData(1000,760)][InlineData(750,490)]
    public async Task Native_release_notes_retry_is_visible_after_offline_failure(int width, int height)
    {
        await renderer.Session.Dispatch<int>(async () =>
        {
            using var http = new HttpClient(new Offline());
            var client = new QuiverCatalogClient(http, "https://catalog.test/api/v1");
            using var model = new BrowseDetailsViewModel(client, (_,_) => throw new Exception("No catalog"),
                (_,_) => Task.FromResult(new DocumentContent("Synthetic README, no game installed.")),
                (_,_) => throw new HttpRequestException("Synthetic offline failure. No external request was made."));
            await using var session = new LauncherSession(); var host = new Host(); var view = new BrowseDetailsView();
            var browse = new BrowseViewModel(client, () => [], () => "", _ => throw new Exception("No list load"));
            view.Configure(session, host, new MarkdownRenderer(_ => throw new Exception("No link requested")), model, _ => null, browse);
            var window = new Window { Content = view, Width = width, Height = height };
            try
            {
                window.Show(); view.Open(BrowseItem.FromList(new() { Name = "Synthetic offline project", Repository = "fixture/project" }), releases: true);
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var retry = view.FindControl<Button>("BrowseDetailsReleaseRetryButton")!;
                Assert.True(retry.IsVisible); Assert.True(retry.IsEnabled);
                for (var step = 0; step < 8 && !retry.Classes.Contains("gamepad-focused"); step++)
                    view.Navigate(Relicade.Services.NavigationDirection.Down);
                Assert.True(retry.Classes.Contains("gamepad-focused"));
                Assert.Same(retry, window.FocusManager!.GetFocusedElement()); retry.BringIntoView();
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                // Wait for the actual composition batch, not an elapsed-time guess.
                var visual = Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(view);
                Assert.NotNull(visual);
                var committed = visual.Compositor.RequestCommitAsync();
                // This headless host uses a manually driven render timer.
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                await committed.WaitAsync(TimeSpan.FromSeconds(10));
                var start = retry.TranslatePoint(default, window)!.Value;
                Assert.InRange(start.X,0,width-retry.Bounds.Width); Assert.InRange(start.Y,0,height-retry.Bounds.Height);
                using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); Assert.Equal(new PixelSize(width,height),frame.PixelSize);
                using var locked = frame.Lock(); var bytes = new byte[locked.RowBytes*height]; Marshal.Copy(locked.Address,bytes,0,bytes.Length);
                var colors=new HashSet<int>();for(var i=0;i<bytes.Length;i+=4)colors.Add(BitConverter.ToInt32(bytes,i));Assert.True(colors.Count>32);
                var directory=Environment.GetEnvironmentVariable("RELICADE_RENDER_OUTPUT");
                if(!string.IsNullOrWhiteSpace(directory))
                {Directory.CreateDirectory(directory);using var file=new FileStream(Path.Combine(directory,$"11-release-retry-{width}x{height}.png"),FileMode.CreateNew,FileAccess.Write);frame.Save(file);}
            }
            finally { window.Close(); }
            return 0;
        },CancellationToken.None);
    }
    sealed class Offline : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) => throw new Exception("Unexpected external request in native retry render");
    }
    sealed class Host : IFeatureNavigationHost
    {
        public GamepadNavigationService Navigation { get; }=new(){ActiveZone=GamepadNavigationZone.BrowseDetailsOverlay};
        public GamepadNavigationZone MainContentZone=>GamepadNavigationZone.BrowseDetailsOverlay;
        public bool IsFocusActive=>true;
        public bool ApplyTransition(GamepadZoneTransition transition){Navigation.ActiveZone=transition.Zone;return true;}
        public void ClearFocus(){}
        public void ClearSidebarFocus(){}
        public void FocusCard(bool stealFocus){}
    }
}
