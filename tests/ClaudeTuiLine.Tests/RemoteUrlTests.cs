using ClaudeTuiLine;

namespace ClaudeTuiLine.Tests;

public class RemoteUrlTests
{
    [Theory]
    [InlineData("git@github.com:user/repo.git", "https://github.com/user/repo")]
    [InlineData("git@github.com:user/repo", "https://github.com/user/repo")]
    [InlineData("https://github.com/user/repo.git", "https://github.com/user/repo")]
    [InlineData("https://github.com/user/repo", "https://github.com/user/repo")]
    [InlineData("http://github.example.com/user/repo", "http://github.example.com/user/repo")]
    [InlineData("git@gitlab.com:group/subgroup/repo.git", "https://gitlab.com/group/subgroup/repo")]
    public void RemoteUrlNormalize_HandlesSshHttpsAndGitSuffix(string raw, string expected)
    {
        Assert.Equal(expected, RemoteUrl.Normalize(raw));
    }

    [Fact]
    public void RemoteUrlNormalize_LocalPathRemote_ReturnsNullAsUnrecognized()
    {
        // A local filesystem path has no git@ prefix, no ssh://git@ prefix, and no https://
        // scheme, so it is not a recognized web remote — Normalize returns null, and §3.2.1's
        // link-suppression path (a missing resolved value) drops the link while keeping the item.
        var actual = RemoteUrl.Normalize("/Users/x/repos/foo");

        Assert.Null(actual);
    }

    [Fact]
    public void RemoteUrlNormalize_SshUrlWithExplicitPort_RewritesToHttpsWithoutThePort()
    {
        // 2222 is the SSH port; a web UI on that host is almost certainly on 443, so carrying
        // the SSH port into the rewritten https:// URL would produce a link that looks plausible
        // and reliably fails. Dropping it is wrong only in the rare case the web UI itself runs
        // on the SSH port; keeping it is wrong nearly always.
        var actual = RemoteUrl.Normalize("ssh://git@host:2222/org/repo.git");

        Assert.Equal("https://host/org/repo", actual);
    }
}
