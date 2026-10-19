using System.Text.Json;
using Relicade.Services.Credentials;

namespace Relicade.Tests;

public class CredentialAdapterTests
{
    private const string Canary = "synthetic-helper-stdin-canary";
    [Theory]
    [InlineData(CredentialProvider.GitHub, "github")]
    [InlineData(CredentialProvider.GitLab, "gitlab")]
    [InlineData(CredentialProvider.Codeberg, "codeberg")]
    public async Task Native_adapter_uses_only_product_service_and_lowercase_provider(CredentialProvider provider, string account)
    {
        var api = new NativeBoundary(); var vault = new NativeCredentialVault(api);
        Assert.Null(await vault.ReadAsync(provider, default));
        await vault.StoreAsync(provider, Canary, default); Assert.Equal(Canary, await vault.ReadAsync(provider, default));
        await vault.DeleteAsync(provider, default); Assert.Null(await vault.ReadAsync(provider, default));
        Assert.All(api.Identities, identity => Assert.Equal(("Relicade.ProviderToken", account), identity));
    }
    private sealed class NativeBoundary : INativeCredentialApi
    {
        public List<(string, string)> Identities = [];
        private string? _value;
        public string? Read(string service, string account) { Identities.Add((service, account)); return _value; }
        public void Store(string service, string account, string value) { Identities.Add((service, account)); _value = value; }
        public void Delete(string service, string account) { Identities.Add((service, account)); _value = null; }
    }

    [Theory]
    [InlineData(1, "", CredentialVaultFailure.Unavailable)]
    [InlineData(1, "vault locked", CredentialVaultFailure.Locked)]
    [InlineData(1, "access denied", CredentialVaultFailure.Denied)]
    [InlineData(2, "synthetic-helper-stdin-canary", CredentialVaultFailure.Unavailable)]
    [InlineData(0, "synthetic-helper-stdin-canary", CredentialVaultFailure.Unavailable)]
    public async Task Helper_store_failures_are_typed_and_redacted(int exit, string error, CredentialVaultFailure expected)
    {
        var vault = new SecretServiceCredentialVault(new Helper(new(exit, "", error)));
        var ex = await Assert.ThrowsAsync<CredentialVaultUnavailableException>(() => vault.StoreAsync(CredentialProvider.GitHub, Canary, default));
        Assert.Equal(expected, ex.Failure); Assert.DoesNotContain(Canary, ex.ToString()); Assert.Null(ex.InnerException);
    }
    [Fact]
    public async Task Helper_missing_is_distinct_from_unknown_failure_and_successful_empty_lookup()
    {
        Assert.Null(await new SecretServiceCredentialVault(new Helper(new(1, "", ""))).ReadAsync(CredentialProvider.GitHub, default));
        await Assert.ThrowsAsync<CredentialVaultUnavailableException>(() => new SecretServiceCredentialVault(new Helper(new(2, "", ""))).ReadAsync(CredentialProvider.GitHub, default));
        await Assert.ThrowsAsync<CredentialVaultUnavailableException>(() => new SecretServiceCredentialVault(new Helper(new(0, "", ""))).ReadAsync(CredentialProvider.GitHub, default));
    }
    private sealed class Helper(SecretToolResult result) : ISecretToolRunner
    {
        public Task<SecretToolResult> RunAsync(IReadOnlyList<string> arguments, string? standardInput, CancellationToken cancellationToken)
        {
            Assert.DoesNotContain(Canary, string.Join(" ", arguments));
            Assert.Equal(new[] { "service", "Relicade.ProviderToken", "account", "github" }, arguments.TakeLast(4));
            return Task.FromResult(result);
        }
    }

    [Theory]
    [InlineData("bad\ncredential")]
    [InlineData("bad\rcredential")]
    [InlineData("bad\0credential")]
    public async Task Control_characters_never_cross_the_helper_boundary(string token)
    {
        var vault = new SecretServiceCredentialVault(new ForbiddenHelper());
        await Assert.ThrowsAsync<CredentialVaultUnavailableException>(() => vault.StoreAsync(CredentialProvider.GitHub, token, default));
    }
    private sealed class ForbiddenHelper : ISecretToolRunner
    {
        public Task<SecretToolResult> RunAsync(IReadOnlyList<string> arguments, string? standardInput, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Helper must not be called.");
    }

    [Fact]
    public async Task Missing_helper_is_explicitly_unavailable_and_creates_no_fallback_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "absent-credential-helper", Guid.NewGuid().ToString("N"));
        var vault = new SecretServiceCredentialVault(new SecretToolProcessRunner(Path.Combine(root, "absent")));
        await Assert.ThrowsAsync<CredentialVaultUnavailableException>(() => vault.StoreAsync(CredentialProvider.GitHub, Canary, default));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task Controlled_helper_process_receives_secret_only_on_stdin_and_confirms_roundtrip()
    {
        if (!OperatingSystem.IsLinux()) return; // Controlled process transport only, NOT native Secret Service acceptance.
        var root = Path.Combine(Path.GetTempPath(), "credential-helper-process", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var executable = Path.Combine(root, "fake-secret-tool");
        var previousEnv = Environment.GetEnvironmentVariable("RELICADE_TEST_UNRELATED_SECRET");
        try
        {
            // A deliberately fake helper in an isolated synthetic directory. No bus/session/host vault is used.
            File.WriteAllText(executable, """
                #!/usr/bin/python3
                import sys,os,json,pathlib
                root=pathlib.Path(__file__).parent
                with (root/'calls.jsonl').open('a') as f: f.write(json.dumps({'argv':sys.argv,'env':dict(os.environ)})+'\n')
                p=root/'synthetic-fake-vault'
                if sys.argv[1]=='store': p.write_text(sys.stdin.read())
                elif sys.argv[1]=='lookup':
                    if not p.exists(): sys.exit(1)
                    sys.stdout.write(p.read_text()+'\n')
                elif sys.argv[1]=='clear': p.unlink(missing_ok=True)
                """ + "\n");
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Environment.SetEnvironmentVariable("RELICADE_TEST_UNRELATED_SECRET", Canary);
            var vault = new SecretServiceCredentialVault(new SecretToolProcessRunner(executable));
            Assert.Null(await vault.ReadAsync(CredentialProvider.GitHub, default));
            await vault.StoreAsync(CredentialProvider.GitHub, Canary, default);
            Assert.Equal(Canary, await vault.ReadAsync(CredentialProvider.GitHub, default));
            await vault.DeleteAsync(CredentialProvider.GitHub, default);
            Assert.Null(await vault.ReadAsync(CredentialProvider.GitHub, default));
            var calls = File.ReadAllText(Path.Combine(root, "calls.jsonl"));
            Assert.DoesNotContain(Canary, calls); Assert.DoesNotContain("RELICADE_TEST_UNRELATED_SECRET", calls);
            foreach (var line in File.ReadAllLines(Path.Combine(root, "calls.jsonl")))
            {
                using var call = JsonDocument.Parse(line);
                Assert.Equal(new[] { "service", "Relicade.ProviderToken", "account", "github" },
                    call.RootElement.GetProperty("argv").EnumerateArray().Select(a => a.GetString()).TakeLast(4));
            }
        }
        finally { Environment.SetEnvironmentVariable("RELICADE_TEST_UNRELATED_SECRET", previousEnv); Directory.Delete(root, true); }
    }
}
