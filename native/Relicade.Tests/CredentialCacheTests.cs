using System.Net;
using Relicade.Core.Services;
using Relicade.Services;
using Relicade.Services.Credentials;

namespace Relicade.Tests;

public class CredentialCacheTests : IDisposable
{
    private const string Canary = "synthetic-cache-credential-canary";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "credential-cache", Guid.NewGuid().ToString("N"));
    private static readonly Uri Endpoint = new("https://api.github.com/repos/fixture/project/releases");
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private static HttpResponseMessage Response(string body = "[]")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        response.Headers.TryAddWithoutValidation("ETag", "\"synthetic-etag\"");
        response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "100"); return response;
    }
    private sealed class Handler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(cancellationToken);
    }

    [Fact]
    public async Task Successful_delete_invalidates_all_live_sessions_and_persisted_credential_cache_contexts()
    {
        var vault = new ControlledCredentialVault(); vault.Values[CredentialProvider.GitHub] = Canary;
        var session = new CredentialSession(vault); await session.InitializeAsync();
        using var first = new HttpClient(new Handler(_ => Task.FromResult(Response())));
        using var second = new HttpClient(new Handler(_ => Task.FromResult(Response())));
        foreach (var (http, name) in new[] { (first, "one"), (second, "two") })
        {
            ReleaseRequestCoordinator.Configure(http, Path.Combine(_root, name));
            await ReleaseRequestCoordinator.For(http).FetchAsync(http, Endpoint, "github", Canary, _ => []);
            Assert.True(ReleaseRequestCoordinator.For(http).HasValidatedToken("github", Canary));
        }
        await session.SaveAsync(CredentialProvider.GitHub, "");
        foreach (var (http, name) in new[] { (first, "one"), (second, "two") })
        {
            var coordinator = ReleaseRequestCoordinator.For(http);
            Assert.False(coordinator.HasValidatedToken("github", Canary)); Assert.Null(coordinator.GetRateLimitSnapshot("github", Canary));
            var cache = new ReleaseEndpointCache(Path.Combine(_root, name));
            Assert.Null(cache.Get("github:" + ReleaseRequestCoordinator.CredentialKey(Canary) + ":" + Endpoint.AbsoluteUri));
            Assert.Null(cache.GetRateLimit("github:" + ReleaseRequestCoordinator.CredentialKey(Canary)));
        }
        foreach (var file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories)) Assert.DoesNotContain(Canary, File.ReadAllText(file));
    }

    [Fact]
    public async Task Failed_delete_preserves_usable_credential_and_validated_context()
    {
        var vault = new ControlledCredentialVault(); vault.Values[CredentialProvider.GitHub] = Canary;
        var session = new CredentialSession(vault); await session.InitializeAsync();
        using var client = new HttpClient(); var coordinator = ReleaseRequestCoordinator.For(client);
        coordinator.RecordValidatedToken("github", Canary);
        vault.Failure = new CredentialVaultUnavailableException(CredentialVaultFailure.Denied);
        await Assert.ThrowsAsync<CredentialVaultUnavailableException>(() => session.SaveAsync(CredentialProvider.GitHub, ""));
        Assert.True(coordinator.HasValidatedToken("github", Canary)); Assert.Equal(Canary, session.Get(CredentialProvider.GitHub));
    }

    [Fact]
    public async Task Inflight_old_credential_cannot_repopulate_cache_after_successful_delete()
    {
        var vault = new ControlledCredentialVault(); vault.Values[CredentialProvider.GitHub] = Canary;
        var session = new CredentialSession(vault); await session.InitializeAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new Handler(async _ => { entered.SetResult(); await release.Task; return Response(); }));
        ReleaseRequestCoordinator.Configure(client, _root); var coordinator = ReleaseRequestCoordinator.For(client);
        var flight = coordinator.FetchAsync(client, Endpoint, "github", Canary, _ => []);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.SaveAsync(CredentialProvider.GitHub, ""); release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flight);
        Assert.False(coordinator.HasValidatedToken("github", Canary)); Assert.Null(coordinator.GetRateLimitSnapshot("github", Canary));
        Assert.Null(new ReleaseEndpointCache(_root).Get("github:" + ReleaseRequestCoordinator.CredentialKey(Canary) + ":" + Endpoint.AbsoluteUri));
    }

    [Fact]
    public async Task Provider_echo_of_token_cannot_enter_request_cache_or_error()
    {
        using var client = new HttpClient(new Handler(_ => Task.FromResult(Response("[{\"name\":\"" + Canary + "\"}]"))));
        ReleaseRequestCoordinator.Configure(client, _root);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => ReleaseRequestCoordinator.For(client).FetchAsync(client, Endpoint, "github", Canary, _ => []));
        Assert.DoesNotContain(Canary, ex.ToString());
        if (Directory.Exists(_root)) foreach (var file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories)) Assert.DoesNotContain(Canary, File.ReadAllText(file));
    }

    [Fact]
    public async Task Validation_connection_errors_cannot_expose_request_secret()
    {
        using var client = new HttpClient(new Handler(_ => throw new HttpRequestException(Canary)));
        var result = await ReleaseTokenValidator.ValidateAsync(client, "github", Canary, default);
        Assert.False(result.IsValid); Assert.DoesNotContain(Canary, result.ErrorMessage);
    }
}
