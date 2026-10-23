namespace Relicade.Services;

// A single guided setup owns these observations. They are not the legacy
// phase-weighted library percentage, a durable record, or a readiness claim.
internal enum GuidedEnginePhase { Resolving, Downloading, Verifying, Staging, Committing, Committed }
internal readonly record struct GuidedEngineProgress(GuidedEnginePhase Phase, long ReceivedBytes = 0, long? TotalBytes = null);
