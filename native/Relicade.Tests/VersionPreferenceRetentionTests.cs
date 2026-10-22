using Relicade.Models;
namespace Relicade.Tests;

public sealed class VersionPreferenceRetentionTests
{
    [Fact]
    public void Ordinary_numeric_retirement_resumes_after_selection_scope()
    {
        var game = new GameInfo { LatestVersion = "2.0", PreferredVersion = "1.0" };
        using (game.PreserveVersionPreferenceDuringSelection())
        {
            game.LatestVersion = "1.0"; Assert.Equal("1.0", game.PreferredVersion);
            game.LatestVersion = "2.0";
        }
        Assert.Equal("1.0", game.PreferredVersion);
        game.LatestVersion = "1.0"; Assert.Null(game.PreferredVersion);
    }
    [Theory]
    [InlineData("1.5")][InlineData(null)]
    public void Explicit_edits_are_not_replaced_by_a_retained_snapshot(string? edited)
    {
        var game = new GameInfo { LatestVersion = "2.0", PreferredVersion = "1.0" };
        using (game.PreserveVersionPreferenceDuringSelection())
        {
            game.LatestVersion = "1.0"; game.PreferredVersion = edited; game.LatestVersion = "2.0";
        }
        Assert.Equal(edited, game.PreferredVersion);
    }
    [Fact]
    public void Nested_retention_scopes_release_once_and_keep_remaining_owner()
    {
        var game = new GameInfo { LatestVersion = "2.0", PreferredVersion = "1.0" };
        var first = game.PreserveVersionPreferenceDuringSelection(); var second = game.PreserveVersionPreferenceDuringSelection();
        first.Dispose(); first.Dispose(); game.LatestVersion = "1.0"; Assert.Equal("1.0", game.PreferredVersion);
        second.Dispose(); game.LatestVersion = "2.0"; game.LatestVersion = "1.0"; Assert.Null(game.PreferredVersion);
    }
}
