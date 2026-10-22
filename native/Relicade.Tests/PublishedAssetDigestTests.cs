using FluentAssertions;
using Relicade.Core.Models;
using Relicade.Services;
using Xunit;

namespace Relicade.Tests;

public sealed class PublishedAssetDigestTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256:1234")]
    [InlineData("sha256:gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    [InlineData("sha1:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData(" sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa ")]
    public void Malformed_publisher_digest_does_not_invent_a_rebuilt_release(string? digest)
    {
        PublishedAssetDigest.FromPublisher(digest).Should().BeNull();
        var build = new InstalledBuild("game.zip", new string('b', 64));
        build.IsRebuiltIn(new GitHubRelease { tag_name = "v1", assets = [new() { name = "game.zip", digest = digest }] }, "v1")
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Valid_digest_is_case_insensitive_and_detects_only_changed_bytes(bool changed)
    {
        var digest = "SHA256:" + new string(changed ? 'B' : 'A', 64);
        PublishedAssetDigest.FromPublisher(digest).Should().Be(new string(changed ? 'b' : 'a', 64));
        new InstalledBuild("game.zip", new string('a', 64))
            .IsRebuiltIn(new GitHubRelease { tag_name = "v1", assets = [new() { name = "game.zip", digest = digest }] }, "v1")
            .Should().Be(changed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    [InlineData("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData(" aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa ")]
    public void Unknown_installed_digest_does_not_invent_a_rebuilt_release(string? digest)
    {
        new InstalledBuild("game.zip", digest!)
            .IsRebuiltIn(new GitHubRelease { tag_name = "nightly", assets =
                [new() { name = "game.zip", digest = "sha256:" + new string('b', 64) }] }, "nightly")
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Uppercase_installed_digest_compares_the_same_bytes(bool changed)
    {
        new InstalledBuild("game.zip", new string('A', 64))
            .IsRebuiltIn(new GitHubRelease { tag_name = "nightly", assets =
                [new() { name = "game.zip", digest = "sha256:" + new string(changed ? 'b' : 'a', 64) }] }, "nightly")
            .Should().Be(changed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Catalog_approval_warning_separates_integrity_and_respects_decline(bool accept)
    {
        var check = new ReleaseCheck(ReleaseCheckState.Verified,
            ["game.zip has no usable published SHA-256."], "v1", new Dictionary<string, string>(), MissingAssetDigest: true);
        var questions = new List<(string Message, string Title)>();
        var result = await ReleaseWarnings.ConfirmAsync("Fixture", "v1", check, (message, title) =>
        {
            questions.Add((message, title));
            return Task.FromResult(accept);
        });
        result.Should().Be(accept);
        var question = questions.Should().ContainSingle().Subject;
        question.Title.Should().Be("Download integrity not verified");
        question.Message.Should().Contain("does not verify this download's bytes")
            .And.Contain("game.zip").And.Contain("SHA-256").And.Contain("not a guarantee that software is safe");
    }
}
