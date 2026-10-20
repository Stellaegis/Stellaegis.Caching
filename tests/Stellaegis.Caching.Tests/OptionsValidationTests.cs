using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Stellaegis.Caching.Tests;

/// <summary>
/// The options say what is wrong with them, and a host refuses to start on a cache that would not work:
/// the bound is checked when the host starts, not when the cache is first resolved.
/// </summary>
public sealed class OptionsValidationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Defaults_hold()
        => Assert.Null(new BoundedCacheOptions().FindError());

    [Theory]
    [InlineData(19, 0.05, "removes no entry per compaction")]
    [InlineData(0, 0.05, "SizeLimit is 0")]
    [InlineData(100, 0, "CompactionPercentage is 0")]
    [InlineData(100, 1.5, "CompactionPercentage is 1.5")]
    public void A_bound_or_a_percentage_that_cannot_work_is_named(long sizeLimit, double percentage, string error)
        => Assert.Contains(error, new BoundedCacheOptions { SizeLimit = sizeLimit, CompactionPercentage = percentage }.FindError());

    [Fact]
    public void The_auto_sizing_parameters_are_checked_too()
    {
        Assert.Contains("MemoryFraction is 1", new BoundedCacheOptions { MemoryFraction = 1 }.FindError());
        Assert.Contains("EstimatedEntryBytes is 0", new BoundedCacheOptions { EstimatedEntryBytes = 0 }.FindError());
        Assert.Contains("MinEntries is 500 and MaxEntries 100", new BoundedCacheOptions { MinEntries = 500, MaxEntries = 100 }.FindError());
        Assert.Contains("Expiration is", new BoundedCacheOptions { Expiration = TimeSpan.FromSeconds(-1) }.FindError());
        Assert.Null(new BoundedCacheOptions { Expiration = TimeSpan.Zero }.FindError());
    }

    [Fact]
    public async Task A_host_fails_to_start_on_a_cache_that_would_not_work()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Stellaegis:Caching:permissions:SizeLimit"] = "19",
            ["Stellaegis:Caching:documents:SizeLimit"] = "1000",
        });
        builder.Services
            .AddStellaegisBoundedCache("permissions", builder.Configuration)
            .AddStellaegisBoundedCache("documents", builder.Configuration);
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(Ct));

        Assert.Contains("Cache 'permissions'", exception.Message);
        Assert.Contains("removes no entry per compaction", exception.Message);
        Assert.DoesNotContain("documents", exception.Message);
    }

    [Fact]
    public async Task A_host_whose_caches_hold_starts()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Stellaegis:Caching:permissions:SizeLimit"] = "1000" });
        builder.Services.AddStellaegisBoundedCache("permissions", builder.Configuration).AddStellaegisBoundedCache("unconfigured", builder.Configuration);
        using var host = builder.Build();

        await host.StartAsync(Ct);
        Assert.Equal(1000, host.Services.GetRequiredKeyedService<BoundedCache>("permissions").SizeLimit);
        await host.StopAsync(Ct);
    }

    [Fact]
    public void Options_built_in_code_are_checked_when_the_cache_is_created()
    {
        using var provider = new ServiceCollection()
            .AddStellaegisBoundedCache("permissions", _ => new BoundedCacheOptions { SizeLimit = 19 })
            .BuildServiceProvider();

        var exception = Assert.Throws<ArgumentException>(() => provider.GetRequiredKeyedService<BoundedCache>("permissions"));
        Assert.Contains("Cache 'permissions'", exception.Message);
    }
}
