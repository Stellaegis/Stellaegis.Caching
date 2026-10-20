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
    /// configured from the child section of <paramref name="sectionName"/> named after the key, and
    /// validated when the host starts. Resolve it with <c>[FromKeyedServices(serviceKey)]</c> or
    /// <c>GetRequiredKeyedService</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each cache reads its own entry of a single section, so several caches are configured side by side:
    /// <c>Stellaegis:Caching:permissions</c>, <c>Stellaegis:Caching:documents</c>. The options are named
    /// options, registered under the same key. A missing entry leaves every option at its default.
    /// </para>
    /// <para>
    /// The options are validated when the host starts, by what <see cref="BoundedCacheOptions.FindError"/>
    /// says: a host whose cache would remove no entry per compaction fails to start, naming the cache. The
    /// options being named, the validation is this library's rather than
    /// <c>AddStellaegisOptions</c>'s, which binds unnamed options.
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
        services.AddOptions<BoundedCacheOptions>(key)
            .Bind(configuration.GetSection(sectionName).GetSection(key))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<BoundedCacheOptions>, BoundedCacheOptionsValidator>());
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
    /// application's unbounded <c>IMemoryCache</c>. The instance is disposed with the container. Options
    /// built in code are checked when the cache is created, at its first resolution.
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

    /// <summary>Validates every named <see cref="BoundedCacheOptions"/> when the host starts, by what the options say.</summary>
    private sealed class BoundedCacheOptionsValidator : IValidateOptions<BoundedCacheOptions>
    {
        public ValidateOptionsResult Validate(string? name, BoundedCacheOptions options)
            => options.FindError() is { } error
                ? ValidateOptionsResult.Fail($"Cache '{name ?? Options.DefaultName}': {error}")
                : ValidateOptionsResult.Success;
    }
}
