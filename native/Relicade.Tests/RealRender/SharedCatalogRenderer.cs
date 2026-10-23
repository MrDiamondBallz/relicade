using Avalonia.Headless;

namespace Relicade.Tests;

// PerAssembly sessions retain the application and its dispatcher context. The
// native rendering fixtures must share that owner, not recreate it per case or
// class. App resources, including thread-affine Geometry instances, are shared.
[CollectionDefinition(SharedCatalogRenderer.CollectionName)]
public sealed class CatalogRenderingCollection : ICollectionFixture<SharedCatalogRenderer>;

public sealed class SharedCatalogRenderer : IAsyncLifetime
{
    public const string CollectionName = "Catalog library and details rendering";
    internal HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(
        typeof(RealRenderTestApp), AvaloniaTestIsolationLevel.PerAssembly);
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => Session.DisposeAsync();
}
