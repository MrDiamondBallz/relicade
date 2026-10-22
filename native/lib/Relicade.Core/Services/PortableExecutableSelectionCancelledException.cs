namespace Relicade.Core.Services;

/// <summary>The staged launch-file choice was declined or cancelled before activation; private staging may remain.</summary>
public sealed class PortableExecutableSelectionCancelledException(CancellationToken cancellationToken = default, Exception? innerException = null)
    : OperationCanceledException("No launch file was selected; this installation was not activated.", innerException, cancellationToken);
