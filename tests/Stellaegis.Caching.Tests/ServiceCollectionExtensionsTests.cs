using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Stellaegis.Caching.Tests;

/// <summary>
/// Unit tests for the configuration-bound registration: each cache reads its own entry of the section,
/// and a missing entry leaves the defaults.
/// </summary>
public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void Cache_reads_its_entry_of_the_default_section()
    {
        var configuration = Configuration(new()
        {
            ["Stellaegis:Caching:permissions:SizeLimit"] = "1234",
            ["Stellaegis:Caching:permissions:Expiration"] = "00:30:00",
        });

        using var provider = new ServiceCollection()
            .AddStellaegisBoundedCache("permissions", configuration)
            .BuildServiceProvider();
        var cache = provider.GetRequiredKeyedService<BoundedCache>("permissions");
        var options = provider.GetRequiredService<IOptionsMonitor<BoundedCacheOptions>>().Get("permissions");

        Assert.Equal("permissions", cache.Name);
        Assert.Equal(1234, cache.SizeLimit);
        Assert.Equal(TimeSpan.FromMinutes(30), options.Expiration);
    }

    [Fact]
    public void Missing_entry_leaves_the_defaults()
    {
        var configuration = Configuration(new());
        var defaults = new BoundedCacheOptions();

        using var provider = new ServiceCollection()
            .AddStellaegisBoundedCache("unconfigured", configuration)
            .BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<BoundedCacheOptions>>().Get("unconfigured");

        Assert.NotNull(provider.GetRequiredKeyedService<BoundedCache>("unconfigured"));
        Assert.Null(options.SizeLimit);
        Assert.Equal(defaults.Expiration, options.Expiration);
        Assert.Equal(defaults.MemoryFraction, options.MemoryFraction);
    }

    [Fact]
    public void Two_caches_are_configured_independently_from_a_custom_section()
    {
        var configuration = Configuration(new()
        {
            ["MyApp:Caches:first:SizeLimit"] = "100",
            ["MyApp:Caches:second:SizeLimit"] = "200",
        });

        using var provider = new ServiceCollection()
            .AddStellaegisBoundedCache("first", configuration, sectionName: "MyApp:Caches")
            .AddStellaegisBoundedCache("second", configuration, sectionName: "MyApp:Caches")
            .BuildServiceProvider();

        Assert.Equal(100, provider.GetRequiredKeyedService<BoundedCache>("first").SizeLimit);
        Assert.Equal(200, provider.GetRequiredKeyedService<BoundedCache>("second").SizeLimit);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
