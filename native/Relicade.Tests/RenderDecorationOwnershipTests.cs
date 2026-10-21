using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Relicade.Services;
using Relicade.Views;

namespace Relicade.Tests;

// Successive supported sessions exercise real resource factories, original input tests and rendering.
// This models the established ownership mechanism; it does not reconstruct a historical test seed.
public class RenderDecorationOwnershipTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Original_banner_inputs_render_with_owning_resources_after_completed_application_transition()
    {
        TextDecoration? previous = null;
        Dispatcher? previousOwner = null;
        var errors = new List<Exception?>();
        await using var first = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApp));
        await using var second = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApp));
        for (var application = 0; application < 2; application++)
        {
            var session = application == 0 ? first : second;
            {
                foreach (var gamepad in new[] { false, true })
                {
                    var error = await Record.ExceptionAsync(() => session.Dispatch<int>(async () =>
                    {
                        output.WriteLine($"application={application} gamepad={gamepad} thread={Environment.CurrentManagedThreadId}");
                        // Every original focus, editing, text input and navigation assertion executes unchanged.
                        await new SettingsTextInputTests().Banner_token_shortcut_moves_navigation_into_the_editing_field(gamepad);
                        output.WriteLine("original-banner-input-PASS; original window close and ShutdownAsync completed");
                        var decoration = await InspectBanner();
                        if (previousOwner != null)
                        {
                            if (application > 0 && !gamepad)
                                Assert.NotSame(previousOwner, decoration.Dispatcher);
                            if (!ReferenceEquals(previousOwner, decoration.Dispatcher))
                                Assert.NotSame(previous, decoration);
                        }
                        previous = decoration;
                        previousOwner = decoration.Dispatcher;
                        return 0;
                    }, CancellationToken.None));
                    errors.Add(error);
                    output.WriteLine($"application={application} gamepad={gamepad} error={error}");
                }
            }
            output.WriteLine($"application={application} Dispatch completed, including per-test application disposal; window ShutdownAsync awaited");
        }
        Assert.Equal(4, errors.Count);
        Assert.All(errors, Assert.Null);
    }

    [Fact]
    public async Task Actual_markdown_link_render_and_action_have_distinct_owners_across_completed_applications()
    {
        TextDecoration? previous = null;
        Dispatcher? previousOwner = null;
        await using var first = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApp));
        await using var second = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApp));
        for (var application = 0; application < 2; application++)
        {
            var session = application == 0 ? first : second;
            {
                await session.Dispatch(() =>
                {
                    string? opened = null;
                    var paragraph = (SelectableTextBlock)new MarkdownRenderer(url => opened = url)
                        .Render("[Read more](https://example.com)").Single();
                    var run = paragraph.Inlines!.OfType<Run>().Single();
                    Assert.Equal("Read more", run.Text);
                    var decoration = Assert.Single(run.TextDecorations!);
                    output.WriteLine($"application={application} decoration={RuntimeHelpers.GetHashCode(decoration)} owner={decoration.Dispatcher.Thread.ManagedThreadId} render-thread={Environment.CurrentManagedThreadId} same-prior={ReferenceEquals(previous, decoration)} access={decoration.CheckAccess()}");
                    var window = new Window { Content = paragraph, Width = 700, Height = 400 };
                    try
                    {
                        window.Show();
                        Dispatcher.UIThread.RunJobs();
                        window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                        // Read all properties only on the actual rendering owner, never a foreign singleton.
                        AssertDecoration(decoration, paragraph.Dispatcher);
                        if (previousOwner != null)
                        {
                            Assert.NotSame(previousOwner, decoration.Dispatcher);
                            Assert.NotSame(previous, decoration);
                        }
                        Assert.True(paragraph.TextLayout.Width > 0);
                        paragraph.RaiseEvent(new PointerReleasedEventArgs(paragraph,
                            new Pointer(1, PointerType.Mouse, true), paragraph, new Point(3, 5), 0,
                            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
                            KeyModifiers.None, MouseButton.Left));
                        Assert.Equal("https://example.com", opened);
                        previous = decoration;
                        previousOwner = decoration.Dispatcher;
                        output.WriteLine("actual-Markdown-render-properties-and-injected-link-action-PASS");
                    }
                    finally
                    {
                        window.Close();
                        Dispatcher.UIThread.RunJobs();
                        Assert.False(window.IsVisible);
                    }
                }, CancellationToken.None);
            }
            output.WriteLine($"markdown application={application} window closed and per-test application disposal completed");
        }
    }

    private async Task<TextDecoration> InspectBanner()
    {
        var store = new Store();
        var view = new MainView(new()
        {
            SettingsStore = store, InitializeOnOpen = false, EnableInput = false, EnableMusic = false
        });
        var window = new Window { Content = view, Width = 1200, Height = 900 };
        try
        {
            window.Show();
            var banner = view.FindControl<LauncherBannerView>("Banners")!;
            banner.FindControl<Border>("GitHubTokenBanner")!.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var button = banner.FindControl<Button>("GitHubTokenBannerSettingsButton")!;
            var text = button.GetVisualDescendants().OfType<TextBlock>()
                .Single(t => t.TextDecorations?.Count == 1);
            var decoration = Assert.Single(text.TextDecorations!);
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            AssertDecoration(decoration, text.Dispatcher);
            output.WriteLine($"actual-banner-render decoration={RuntimeHelpers.GetHashCode(decoration)} owner={decoration.Dispatcher.Thread.ManagedThreadId} thread={Environment.CurrentManagedThreadId}");
            return decoration;
        }
        finally
        {
            window.Close();
            await view.ShutdownAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible);
            output.WriteLine("inspected-banner window close and ShutdownAsync fully completed");
        }
    }

    private static void AssertDecoration(TextDecoration decoration, Dispatcher owner)
    {
        Assert.Same(owner, decoration.Dispatcher);
        Assert.Same(Dispatcher.UIThread, decoration.Dispatcher);
        Assert.True(decoration.CheckAccess());
        Assert.Equal(TextDecorationLocation.Underline, decoration.Location);
        Assert.Null(decoration.Stroke);
        Assert.Equal(1, decoration.StrokeThickness);
        Assert.Equal(TextDecorationUnit.FontRecommended, decoration.StrokeThicknessUnit);
        Assert.Null(decoration.StrokeDashArray);
        Assert.Equal(0, decoration.StrokeDashOffset);
        Assert.Equal(PenLineCap.Flat, decoration.StrokeLineCap);
        Assert.Equal(0, decoration.StrokeOffset);
        Assert.Equal(TextDecorationUnit.FontRecommended, decoration.StrokeOffsetUnit);
    }

    private sealed class Store : ISettingsStore, Relicade.Services.Credentials.ICredentialSettingsStore
    {
        public Relicade.Services.Credentials.CredentialSession Credentials { get; } = new(new ControlledCredentialVault());
        public bool HasLegacyCredentials => false;
        public AppSettings Current { get; } = new()
        {
            FirstStartup = false, EnableGamepadInput = true,
            AppsPath = Path.Combine(Path.GetTempPath(), "relicade-decoration-tests", Guid.NewGuid().ToString("N"))
        };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
}
