using CopilotDeepSeek.Routes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Concurrent;

namespace CopilotDeepSeekTests.Routes;

[TestClass]
public partial class RequestContextTests
{
    private static RequestContext CreateContext(string targetBase)
    {
        return new RequestContext(
            scopeFactory: new StubScopeFactory(),
            httpClient: new HttpClient(),
            targetBase: targetBase,
            apiKey: "test-key",
            socketHandler: new SocketsHttpHandler(),
            reasoningCache: new ConcurrentDictionary<string, string>());
    }

    [TestMethod]
    public void GetEndpointUrl_WithTrailingSlash_AppendsRelativePath()
    {
        // Arrange
        var ctx = CreateContext("https://api.example.com/v1/");
        const string relative = "chat/completions";

        // Act
        var result = ctx.GetEndpointUrl(relative);

        // Assert
        Assert.AreEqual("https://api.example.com/v1/chat/completions", result.AbsoluteUri);
    }

    [TestMethod]
    public void GetEndpointUrl_WithoutTrailingSlash_AppendsRelativePath()
    {
        // After the fix: baseUri (with trailing "/") is used, so /v1/ is preserved.
        var ctx = CreateContext("https://api.example.com/v1");
        const string relative = "chat/completions";

        var result = ctx.GetEndpointUrl(relative);

        Assert.AreEqual("https://api.example.com/v1/chat/completions", result.AbsoluteUri);
    }

    [TestMethod]
    public void GetEndpointUrl_WithoutTrailingSlash_RelativeWithLeadingSlash_ReplacesBasePath()
    {
        // Same bug — leading slash on relative doesn't help because TargetBase lacks trailing "/"
        var ctx = CreateContext("https://api.example.com/v1");
        const string relative = "/chat/completions";

        var result = ctx.GetEndpointUrl(relative);

        Assert.AreEqual("https://api.example.com/chat/completions", result.AbsoluteUri);
    }

    [TestMethod]
    public void GetEndpointUrl_TargetBaseWithoutPath_SlashOrNot_WorksCorrectly()
    {
        // When TargetBase has no sub-path, trailing slash doesn't matter
        var ctx = CreateContext("https://api.example.com");
        var result = ctx.GetEndpointUrl("models");

        Assert.AreEqual("https://api.example.com/models", result.AbsoluteUri);
    }

    [TestMethod]
    public void GetEndpointUrl_TargetBaseWithoutPath_WithTrailingSlash()
    {
        var ctx = CreateContext("https://api.example.com/");
        var result = ctx.GetEndpointUrl("models");

        Assert.AreEqual("https://api.example.com/models", result.AbsoluteUri);
    }

    [TestMethod]
    public void GetEndpointUrl_WithNestedRelativePath()
    {
        var ctx = CreateContext("https://api.example.com/v1/");
        var result = ctx.GetEndpointUrl("some/deep/path");

        Assert.AreEqual("https://api.example.com/v1/some/deep/path", result.AbsoluteUri);
    }

    [TestMethod]
    public void GetEndpointUrl_WithLeadingSlashOnRelative_WithTrailingSlashOnBase()
    {
        // A leading "/" means "from domain root", so the base path /v1/ is replaced.
        var ctx = CreateContext("https://api.example.com/v1/");
        var result = ctx.GetEndpointUrl("/chat/completions");

        Assert.AreEqual("https://api.example.com/chat/completions", result.AbsoluteUri);
    }
}

/// <summary>
/// Minimal stub to satisfy the IServiceScopeFactory constructor dependency.
/// Not used by GetEndpointUrl, so no implementation needed.
/// </summary>
internal sealed class StubScopeFactory : IServiceScopeFactory
{
    public IServiceScope CreateScope() => throw new NotSupportedException("Not expected in these tests");
}