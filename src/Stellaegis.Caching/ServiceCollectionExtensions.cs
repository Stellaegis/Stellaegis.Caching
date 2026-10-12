using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Stellaegis.Caching;

/// <summary>Dependency injection helpers for <see cref="BoundedCache"/>.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="BoundedCache"/> as a keyed singleton under <paramref name="serviceKey"/>.
    /// Resolve it with <c>[FromKeyedServices(serviceKey)]</c> or <c>GetRequiredKeyedService</c>.
    /// </summary>
    /// <remarks>
    /// The keyed registration lets several dedicated caches (e.g. authorization, vault) coexist in a
    /// single process — each with its own bound, TTL, and metrics — without sharing the application's
    /// unbounded <c>IMemoryCache</c>. The instance is disposed with the container.
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
