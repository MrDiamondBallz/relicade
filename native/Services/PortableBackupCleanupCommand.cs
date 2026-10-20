namespace Relicade.Services;

/// <summary>Cleanup-only local command formatting and strict dispatch.</summary>
internal static class PortableBackupCleanupCommand
{
    internal const string Preview = "--preview-portable-backups", Cleanup = "--cleanup-portable-backups";
    internal static bool IsRequest(IEnumerable<string> args) => args.Any(a =>
        a.StartsWith(Preview, StringComparison.OrdinalIgnoreCase) || a.StartsWith(Cleanup, StringComparison.OrdinalIgnoreCase));
    internal static bool ValidArguments(string[] args) =>
        (args.Length == 2 && args[0] == Preview || args.Length == 4 && args[0] == Cleanup && args[2] == "--confirm" && PortableBackupCleanupService.ValidHash(args[3])) &&
        !string.IsNullOrWhiteSpace(args[1]) && !args[1].StartsWith('-') && !args[1].Contains('=') && !args[1].Any(char.IsControl) && args[1] == args[1].Trim();
    internal static bool ValidOriginalArguments(string[] original, string[] dispatched) =>
        !IsRequest(original.Skip(1)) || ValidArguments(original.Skip(1).ToArray()) && original.Skip(1).SequenceEqual(dispatched);
    internal static int Execute(GameManager manager, string[] args)
    {
        if (args[0] == Preview)
        {
            var plan = PortableBackupCleanupService.Preview(manager, args[1]);
            Console.WriteLine("Plan SHA256: " + plan.PlanSha256);
            foreach (var f in plan.Files) Console.WriteLine($"{f.OperationId}/{f.BackupRelativePath} -> retained {f.OperationId}/{f.RetainedRelativePath} | {f.Length} bytes | SHA256 {f.Sha256}");
            Console.WriteLine($"Reclaimable bytes: {plan.ReclaimableBytes}; duplicate payload files: {plan.Files.Count}.");
            Console.WriteLine("ALL previous generations, data snapshots, journals, current saves and the entire current-operation backup remain. This is not uninstall, history pruning or secure erasure.");
            return 0;
        }
        var result = PortableBackupCleanupService.Apply(manager, args[1], args[3]);
        Console.WriteLine($"Plan SHA256: {result.PlanSha256}; removed THIS invocation: {result.DeletedFiles} files / {result.ReclaimedBytes} bytes; completed: {result.Completed}.");
        Console.WriteLine("All retained previous generations, declared data and current-operation backups remain; redundant copies are not rolled back or securely erased.");
        return result.Completed ? 0 : 1;
    }
}
