using System.Diagnostics;
using System.Text;

namespace Relicade.Services.Credentials;

public sealed class SecretToolResult(int exitCode, string output, string error)
{
    public int ExitCode { get; } = exitCode;
    public string Output { get; } = output;
    public string Error { get; } = error;
    public override string ToString() => "Secret Service helper result (redacted)";
}
public interface ISecretToolRunner
{
    Task<SecretToolResult> RunAsync(IReadOnlyList<string> arguments, string? standardInput, CancellationToken cancellationToken);
}

/// <summary>Optional Secret Service/libsecret helper. No shell, no secret argv/env, no local fallback.</summary>
public sealed class SecretServiceCredentialVault(ISecretToolRunner? runner = null) : ICredentialVault
{
    private readonly ISecretToolRunner _runner = runner ?? new SecretToolProcessRunner();
    private async Task<string?> Execute(CredentialProvider provider, string operation, string? value, CancellationToken ct)
    {
        var account = CredentialIdentity.Account(provider);
        List<string> args = [operation];
        if (operation == "store") args.Add("--label=Relicade provider token");
        args.AddRange(["service", CredentialIdentity.Service, "account", account]);
        try
        {
            var result = await _runner.RunAsync(args, value, ct).ConfigureAwait(false);
            if (result.ExitCode == 1 && result.Error.Length == 0 && result.Output.Length == 0 && operation is "lookup" or "clear") return null;
            if (result.ExitCode != 0 || result.Error.Length != 0)
                throw new CredentialVaultUnavailableException(Classify(result.Error));
            if (operation != "lookup")
            {
                if (result.Output.Length != 0) throw new CredentialVaultUnavailableException();
                return null;
            }
            // secret-tool writes exactly one terminal newline, which is not part of the token.
            var token = result.Output.EndsWith('\n') ? result.Output[..^1] : result.Output;
            CredentialIdentity.ValidateToken(token);
            return token;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw CredentialSession.Sanitize(ex); }
    }
    private static CredentialVaultFailure Classify(string error) =>
        error.Contains("locked", StringComparison.OrdinalIgnoreCase) ? CredentialVaultFailure.Locked :
        error.Contains("denied", StringComparison.OrdinalIgnoreCase) || error.Contains("cancelled", StringComparison.OrdinalIgnoreCase)
            ? CredentialVaultFailure.Denied : CredentialVaultFailure.Unavailable;
    public Task<string?> ReadAsync(CredentialProvider provider, CancellationToken cancellationToken) => Execute(provider, "lookup", null, cancellationToken);
    public Task StoreAsync(CredentialProvider provider, string value, CancellationToken cancellationToken)
    {
        CredentialIdentity.ValidateToken(value);
        return Execute(provider, "store", value, cancellationToken);
    }
    public Task DeleteAsync(CredentialProvider provider, CancellationToken cancellationToken) => Execute(provider, "clear", null, cancellationToken);
}

internal sealed class SecretToolProcessRunner(string executable = "/usr/bin/secret-tool") : ISecretToolRunner
{
    public async Task<SecretToolResult> RunAsync(IReadOnlyList<string> arguments, string? standardInput, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(executable)) throw new CredentialVaultUnavailableException();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false) };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment.Clear();
        start.Environment["PATH"] = "/usr/bin:/bin"; start.Environment["LC_ALL"] = "C";
        foreach (var name in new[] { "HOME", "DBUS_SESSION_BUS_ADDRESS", "XDG_RUNTIME_DIR", "DISPLAY", "WAYLAND_DISPLAY" })
            if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new CredentialVaultUnavailableException();
            var output = ReadBounded(process.StandardOutput, deadline.Token);
            var error = ReadBounded(process.StandardError, deadline.Token);
            if (standardInput != null) await process.StandardInput.WriteAsync(standardInput.AsMemory(), deadline.Token).ConfigureAwait(false);
            process.StandardInput.Close();
            await Task.WhenAll(output, error, process.WaitForExitAsync(deadline.Token)).ConfigureAwait(false);
            return new(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new CredentialVaultUnavailableException(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw CredentialSession.Sanitize(ex); }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }
    }
    private static async Task<string> ReadBounded(StreamReader reader, CancellationToken ct)
    {
        var buffer = new char[512]; var text = new StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
        {
            if (text.Length + count > 4096) throw new CredentialVaultUnavailableException();
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }
}
