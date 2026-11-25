using ALNoor.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace ALNoor.Api.Tests.LiveKit;

public sealed class LiveKitOptionsValidationTests
{
    private static void AssertStartupFails(string key, string? value)
    {
        using var factory = new ApiFactory(ApiFactory.UnreachableDatabase).WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })));

        Assert.ThrowsAny<OptionsValidationException>(() => factory.CreateClient());
    }

    [Fact]
    public void Short_api_secret_prevents_startup() => AssertStartupFails("LiveKit:ApiSecret", "too-short");

    [Fact]
    public void Missing_api_key_prevents_startup() => AssertStartupFails("LiveKit:ApiKey", "");

    [Fact]
    public void Http_url_prevents_startup() => AssertStartupFails("LiveKit:Url", "http://localhost:7880");

    [Fact]
    public void Missing_connection_string_prevents_startup() => AssertStartupFails("ConnectionStrings:Default", "");
}
