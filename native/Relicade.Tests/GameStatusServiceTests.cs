using System.Net;
using FluentAssertions;
using Relicade.Core.Models;
using Relicade.Models;
using Relicade.Services;

namespace Relicade.Tests;

public class GameStatusServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckStatusAsync_marks_missing_folder_as_not_installed(bool unavailable)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempRoot);

        try
        {
            var game = new GameInfo
            {
                Name = "Missing",
                FolderName = "MissingFolder",
                Repository = "owner/missing",
            };

            using var transport = new StatusTransport(unavailable);
            using var httpClient = new HttpClient(transport);
            await GameStatusService.CheckStatusAsync(game, httpClient, tempRoot);

            game.Status.Should().Be(GameStatus.NotInstalled);
            game.IsLoading.Should().BeFalse();
            transport.Requests.Should().Equal(unavailable
                ? ["https://api.github.com/repos/owner/missing/releases"]
                : ["https://api.github.com/repos/owner/missing/releases", "https://api.github.com/repos/owner/missing/releases/latest"]);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    // The status check also checks release metadata, even for an absent folder.
    // Keep that production path intact, but never send a fixture repository to the network.
    private sealed class StatusTransport(bool unavailable) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            if (unavailable) throw new HttpRequestException("Synthetic offline release check");
            var latest = request.RequestUri.AbsolutePath.EndsWith("/latest", StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(latest ? HttpStatusCode.NotFound : HttpStatusCode.OK)
                { Content = new StringContent(latest ? "{}" : "[]") });
        }
    }

    [Fact]
    public async Task EnsureInstalledVersionFileAsync_writes_default_version()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");

        try
        {
            var version = await GameStatusService.EnsureInstalledVersionFileAsync(tempFile);
            version.Should().Be("0.0.0");
            File.ReadAllText(tempFile).Trim().Should().Be("0.0.0");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void TryRestoreAndroidPackageName_reads_sidecar_when_missing()
    {
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(gamePath);

        try
        {
            File.WriteAllText(
                Path.Combine(gamePath, GameStatusService.AndroidPackageFileName),
                "com.example.game\n");

            var game = new GameInfo { Name = "Android Game", FolderName = "AndroidGame" };
            GameStatusService.TryRestoreAndroidPackageName(game, gamePath);
            game.AndroidPackageName.Should().Be("com.example.game");
        }
        finally
        {
            Directory.Delete(gamePath, true);
        }
    }
}
