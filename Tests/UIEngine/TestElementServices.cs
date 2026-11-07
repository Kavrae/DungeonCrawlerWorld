using System.Runtime.CompilerServices;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Tests.UIEngine;

/// <summary>The ElementServices a test builds its elements with: one render service per pool, with no device, shared by every element and controller over that pool.</summary>
internal static class TestElementServices
{
    private static readonly ConditionalWeakTable<ElementPoolService, PoolServices> ServicesByPool = [];

    /// <summary>The services for pool with fontService, the same instance every time a pool is asked for with the same fonts.</summary>
    public static ElementServices For(ElementPoolService pool, FontService fontService)
    {
        var poolServices = ServicesByPool.GetValue(pool, static _ => new PoolServices());
        if (!poolServices.ServicesByFonts.TryGetValue(fontService, out var services))
        {
            services = new ElementServices(fontService, pool, poolServices.RenderService);
            poolServices.ServicesByFonts.Add(fontService, services);
        }

        return services;
    }

    /// <summary>The services for pool with the shared test fonts.</summary>
    public static ElementServices For(ElementPoolService pool) => For(pool, TestFonts.Shared);

    /// <summary>The render service every element over pool draws with, for a controller built beside them.</summary>
    public static UiRenderService RenderFor(ElementPoolService pool) => ServicesByPool.GetValue(pool, static _ => new PoolServices()).RenderService;

    private sealed class PoolServices
    {
        public UiRenderService RenderService { get; } = new();

        public Dictionary<FontService, ElementServices> ServicesByFonts { get; } = [];
    }
}
