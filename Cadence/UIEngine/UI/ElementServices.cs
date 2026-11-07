using UIEngine.Fonts;
using UIEngine.Rendering;

namespace UIEngine.UI;

/// <summary>The services every element is built with: fonts, the element pool and the render service.</summary>
/// <remarks>One per host, handed to every element constructor. A non-element takes only the services it uses, never this bundle.</remarks>
public sealed class ElementServices(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService)
{
    public FontService FontService { get; } = fontService;

    public ElementPoolService ElementPoolService { get; } = elementPoolService;

    public UiRenderService RenderService { get; } = renderService;
}
