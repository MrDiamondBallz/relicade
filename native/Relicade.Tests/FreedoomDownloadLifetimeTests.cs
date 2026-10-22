using System.Net;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Relicade.Services;
using Relicade.ViewModels;
using Relicade.Views;
namespace Relicade.Tests;
public sealed class FreedoomDownloadLifetimeTests
{
    [AvaloniaFact]
    public async Task Explicit_download_only_prepares_preview_then_requires_copy_and_play_separately()
    {
        using var own = new OwnedSetupFixture(); await own.Initialize();
        own.Definition = own.Definition with { FileName = "freedoom2.wad", ExpectedSha256 = Convert.ToHexStringLower(SHA256.HashData(own.Data)) };
        using var download = new FreedoomDataDownloadTests.Fixture(data: own.Data);
        await using var session = new LauncherSession(); var played = 0;
        var model = new OwnedGameSetupViewModel(session, own.Game.InstanceKey, own.Setup());
        model.ConfigureFreedoom(() => { played++; return Task.FromResult(true); }, false);
        OwnedGameSetupFlow.ConfigureFreedoomDownload(model, own.Setup(), (progress, token) => download.Download(token, progress));
        var dialog = new OwnedGameSetupDialog(model);
        try
        {
            dialog.Show(); Dispatcher.UIThread.RunJobs(); Assert.Equal(0, download.Calls);
            Assert.Equal("Get free game data", dialog.DownloadButton.Content);
            dialog.DownloadButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work;
            Assert.Equal(1, download.Calls); Assert.Equal(OwnedGameSetupUiState.Confirm, model.State);
            Assert.NotNull(model.Plan); Assert.False(File.Exists(model.Plan.Destination)); Assert.Equal(0, played);
            Assert.True(File.Exists(model.Plan.Source)); Assert.Contains("COPYING", model.Message);
            dialog.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work;
            Assert.Equal(OwnedGameSetupUiState.Completed, model.State); Assert.Equal(own.Data, File.ReadAllBytes(model.Plan.Destination));
            Assert.Equal(own.Data, File.ReadAllBytes(model.Plan.Source)); Assert.Equal(0, played);
            dialog.PlayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work; Assert.Equal(1, played);
        }
        finally { dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); }
        Assert.Equal(2, Directory.GetFiles(download.Cache).Length); Assert.Equal(0, own.NetworkCalls);
    }
    [AvaloniaFact]
    public async Task Closing_during_download_cancels_and_drains_without_late_preview_copy_or_play()
    {
        using var own = new OwnedSetupFixture(); await own.Initialize(); using var download = new FreedoomDataDownloadTests.Fixture();
        using var blocked = new ControlledReadStream();
        download.Response = _ => new(HttpStatusCode.OK) { Content = new StreamContent(blocked) };
        await using var session = new LauncherSession(); var played = 0;
        var model = new OwnedGameSetupViewModel(session, own.Game.InstanceKey, own.Setup());
        model.ConfigureFreedoom(() => { played++; return Task.FromResult(true); }, false);
        OwnedGameSetupFlow.ConfigureFreedoomDownload(model, own.Setup(), (progress, token) => download.Download(token, progress));
        var dialog = new OwnedGameSetupDialog(model);
        try
        {
            dialog.Show(); Dispatcher.UIThread.RunJobs(); dialog.DownloadButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await blocked.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(dialog.Work.IsCompleted);
            dialog.Close(); await dialog.Work.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(model.IsClosed); Assert.Null(model.Plan); Assert.Equal(0, played);
            Assert.Equal(1, download.Calls); Assert.Empty(Directory.GetFiles(download.Cache)); Assert.Equal(0, own.NetworkCalls);
        }
        finally { dialog.Close(); blocked.Release.TrySetResult(); await dialog.Work; model.DisposeAfterDrain(); }
    }
    [Fact]
    public async Task Cancellation_mid_body_cleans_only_owned_partial_and_releases_cache_writer()
    {
        using var download = new FreedoomDataDownloadTests.Fixture(); using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download.Download(cancel.Token, new InlineProgress(_ => cancel.Cancel())));
        Assert.Empty(Directory.GetFiles(download.Cache));
        var result = await download.Download(); Assert.True(File.Exists(result.Source)); Assert.Equal(2, download.Calls);
    }
    [Fact]
    public async Task Unknown_length_body_cannot_exceed_pinned_download_budget()
    {
        using var download = new FreedoomDataDownloadTests.Fixture();
        download.Response = _ => new(HttpStatusCode.OK) { Content = new StreamContent(new NonSeekingStream(new MemoryStream(download.Zip.Concat(new byte[] { 9 }).ToArray()))) };
        await Assert.ThrowsAsync<InvalidDataException>(() => download.Download());
        Assert.Empty(Directory.GetFiles(download.Cache)); Assert.Equal(1, download.Calls);
    }
    [AvaloniaTheory]
    [InlineData("kiosk")][InlineData("library")]
    public async Task Changed_setup_authority_refuses_before_network_or_cache_creation(string change)
    {
        using var own = new OwnedSetupFixture(); await own.Initialize(); using var download = new FreedoomDataDownloadTests.Fixture();
        await using var session = new LauncherSession(); var setup = own.Setup();
        var model = new OwnedGameSetupViewModel(session, own.Game.InstanceKey, setup);
        model.ConfigureFreedoom(() => throw new Exception("Must not launch"), false);
        OwnedGameSetupFlow.ConfigureFreedoomDownload(model, setup, (progress, token) => download.Download(token, progress));
        if (change == "kiosk") own.Store.Current.KioskMode = true;
        else File.AppendAllText(own.Manager.CatalogService.AppsConfigPath, " ");
        try
        {
            await model.DownloadAndPreviewAsync(); Assert.Equal(OwnedGameSetupUiState.Error, model.State);
            Assert.Equal(0, download.Calls); Assert.False(Directory.Exists(download.Cache)); Assert.Null(model.Plan);
        }
        finally { model.Close(); model.DisposeAfterDrain(); }
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task Official_website_alternative_never_starts_data_download(bool throws)
    {
        await using var session = new LauncherSession(); var opened = 0;
        var model = new OwnedGameSetupViewModel(session, "synthetic", "Synthetic data only",
            (_, _) => throw new Exception(), (_, _) => throw new Exception(), () => Task.CompletedTask);
        model.ConfigureFreedoom(() => throw new Exception("Must not launch"), false);
        model.ConfigureFreeDataDownload((_, _) => throw new Exception("Must not download"));
        var dialog = new OwnedGameSetupDialog(model);
        model.OpenDownload = () => { opened++; return throws ? Task.FromException<bool>(new IOException("Synthetic browser failure")) : Task.FromResult(false); };
        try
        {
            dialog.Show(); Dispatcher.UIThread.RunJobs();
            dialog.WebsiteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.Work;
            Assert.Equal(1, opened); Assert.Contains(OwnedGameSetupViewModel.OfficialFreedoomRelease, model.Message);
            Assert.Null(model.Plan); Assert.True(model.CanChoose); Assert.False(model.CanPlay);
        }
        finally { dialog.Close(); await dialog.Work; model.DisposeAfterDrain(); }
    }
    sealed class InlineProgress(Action<double> action) : IProgress<double> { public void Report(double value) => action(value); }
    internal class NonSeekingStream(Stream inner) : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
    internal sealed class ControlledReadStream() : NonSeekingStream(Stream.Null)
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); return 0; }
    }
}
