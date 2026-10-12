using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Stellaegis.Caching;

/// <summary>Dependency injection helpers for <see cref="BoundedCache"/>.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="BoundedCache"/> as a keyed singleton under <paramref name="serviceKey"/>,
    /// configured from the child section of <paramref name="sectionName"/> named after the key.
    /// Resolve it with <c>[FromKeyedServices(serviceKey)]</c> or <c>GetRequiredKeyedService</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each cache reads its own entry of a single section, so several caches are configured side by side:
    /// <c>Stellaegis:Caching:permissions</c>, <c>Stellaegis:Caching:documents</c>. The options are named
    /// options, registered under the same key. A missing entry leaves every option at its default.
    /// </para>
    /// <para>
    /// The key is also a configuration path segment: a <c>:</c> in it addresses a nested section.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceKey">The key the cache is registered and resolved under, and its configuration entry.</param>
    /// <param name="configuration">The configuration holding the section.</param>
    /// <param name="sectionName">The section holding one entry per cache.</param>
    public static IServiceCollection AddStellaegisBoundedCache(
        this IServiceCollection services,
        string serviceKey,
        IConfiguration configuration,
        string sectionName = BoundedCacheOptions.SectionName)
    {
        var key = Guard.NotWhiteSpace(serviceKey);
        Guard.NotNull(configuration);
        Guard.NotWhiteSpace(sectionName);
        services.Configure<BoundedCacheOptions>(key, configuration.GetSection(sectionName).GetSection(key));
        return services.AddStellaegisBoundedCache(
            key, sp => sp.GetRequiredService<IOptionsMonitor<BoundedCacheOptions>>().Get(key));
    }

    /// <summary>
    /// Registers a <see cref="BoundedCache"/> as a keyed singleton under <paramref name="serviceKey"/>.
    /// Resolve it with <c>[FromKeyedServices(serviceKey)]</c> or <c>GetRequiredKeyedService</c>.
    /// </summary>
    /// <remarks>
    /// The keyed registration lets several dedicated caches (e.g. one for permissions, one for configuration)
    /// coexist in a single process — each with its own bound, TTL, and metrics — without sharing the
    /// application's unbounded <c>IMemoryCache</c>. The instance is disposed with the container.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceKey">The key the cache is registered and resolved under.</param>
    /// <param name="optionsFactory">Resolves the cache options, e.g. from bound <c>IOptions</c>.</param>
    public static IServiceCollection AddStellaegisBoundedCache(
        this IServiceCollection services,
        string serviceKey,
        Func<IServiceProvider, BoundedCacheOptions> optionsFactory)
    {
        var key = Guard.NotWhiteSpace(serviceKey);
        Guard.NotNull(optionsFactory);
        services.TryAddKeyedSingleton<BoundedCache>(key, (sp, k) =>
            new BoundedCache((string)k!, optionsFactory(sp)));
        return services;
    }
}
