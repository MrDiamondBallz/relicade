using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Relicade.Models;

namespace Relicade.Tests;

public sealed class OwnedSetupSerializationTests
{
    [Theory]
    [InlineData("UZDoom/UZDoom", true, false)]
    [InlineData("perfect-dark-pc-port/perfect_dark", false, true)]
    [InlineData("independent/project", false, false)]
    public void Derived_setup_capabilities_are_excluded_from_json_contract_and_ignore_stale_values(string repository, bool freedoom, bool mandatory)
    {
        var game = new GameInfo { Repository = repository, FolderName = "Saved entry", CatalogEntryId = "retained-id" };
        var options = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        var contract = options.GetTypeInfo(typeof(GameInfo));
        Assert.DoesNotContain(contract.Properties, property => property.Name == nameof(GameInfo.OffersFreedoomSetup) && property.Get != null);
        Assert.DoesNotContain(contract.Properties, property => property.Name == nameof(GameInfo.HasOwnedDataAdapter) && property.Get != null);
        // Library persistence uses an explicit projection, not UI-property serialization.
        var json = JsonSerializer.Serialize(new { game.Repository, game.FolderName, game.CatalogEntryId });
        var restored = JsonSerializer.Deserialize<GameInfo>(json)!;
        Assert.Equal(repository, restored.Repository); Assert.Equal("Saved entry", restored.FolderName);
        Assert.Equal("retained-id", restored.CatalogEntryId);
        Assert.Equal(freedoom, restored.OffersFreedoomSetup); Assert.Equal(mandatory, restored.HasOwnedDataAdapter);
        // Older files may contain stale derived values. They cannot override the source-backed rule.
        var old = JsonSerializer.Deserialize<GameInfo>(JsonSerializer.Serialize(new
        {
            Repository = repository, FolderName = "Saved entry", CatalogEntryId = "retained-id",
            OffersFreedoomSetup = !freedoom, HasOwnedDataAdapter = !mandatory
        }))!;
        Assert.Equal(repository, old.Repository); Assert.Equal("Saved entry", old.FolderName);
        Assert.Equal("retained-id", old.CatalogEntryId);
        Assert.Equal(freedoom, old.OffersFreedoomSetup); Assert.Equal(mandatory, old.HasOwnedDataAdapter);
    }
}
