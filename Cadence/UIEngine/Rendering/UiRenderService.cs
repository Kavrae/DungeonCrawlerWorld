using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace UIEngine.Rendering;

/// <summary>What everything in the UI draws with: the device, the one SpriteEffectBatch, the unit rectangle, and the render-state stack that scrolled and clipped content pushes and pops.</summary>
/// <remarks>
/// <para>
/// One per host, built by UiEngineBootstrapper and filled once by Initialize from the host's LoadContent; until then (and in a
/// headless test that never draws) its members are unset. Elements reach it as Element.Render -- ElementPoolService hands it to
/// every element it builds -- and anything outside the element tree that draws (the layer host, the map renderer) takes it.
/// </para>
/// <para>
/// The whole UI is one pass through the sprite effect, in call order: BeginFrame starts it each frame and EndFrame draws what is
/// left; a scrollable element pushes the offset its content is drawn at and the rectangle it is clipped to, and pops back to
/// exactly what it inherited -- neither ends the pass (SpriteEffectBatch.DrawOffset, ClipRectangle). Sprites and text share
/// it, so outlined text is two quads a glyph in its place, and the map lights its own draws by setting the batch's Glow and
/// clearing it.
/// </para>
/// </remarks>
public sealed class UiRenderService
{
    /// <summary>What a nested scope draws under: the offset added to its positions and the rectangle it may show in (null for anywhere), in screen pixels.</summary>
    public readonly record struct RenderState(Vector2 DrawOffset, Rectangle? ClipRectangle)
    {
        public static RenderState Root => new(Vector2.Zero, null);

        /// <summary>This state clipped to rectangle as well -- the intersection with any clip already in force, so a nested scope never shows outside its ancestors.</summary>
        public RenderState ClippedTo(Rectangle rectangle) =>
            this with { ClipRectangle = ClipRectangle is { } inherited ? Rectangle.Intersect(inherited, rectangle) : rectangle };
    }

    private readonly Stack<RenderState> _renderStateStack = new();
    private RenderState _currentRenderState;

    public GraphicsDevice GraphicsDevice { get; private set; } = null!;

    /// <summary>What every draw in the UI goes through, between BeginFrame and EndFrame.</summary>
    public SpriteEffectBatch Batch { get; private set; } = null!;

    public Texture2D UnitRectangle { get; private set; } = null!;

    /// <summary>The state draws are under now; Element.Draw composes each scope's from it (ClippedTo, a new DrawOffset).</summary>
    public RenderState CurrentRenderState => _currentRenderState;

    /// <summary>Captures the device and what draws with it -- called once, from the host's LoadContent, after all of them exist.</summary>
    public void Initialize(GraphicsDevice graphicsDevice, SpriteEffectBatch batch, Texture2D unitRectangle)
    {
        GraphicsDevice = graphicsDevice;
        Batch = batch;
        UnitRectangle = unitRectangle;
    }

    /// <summary>Begins the frame's pass under the root state -- no offset, no clip -- which every nested Push/PopRenderState unwinds back to. Called once per frame, from the host's Draw.</summary>
    public void BeginFrame()
    {
        _renderStateStack.Clear();
        _currentRenderState = RenderState.Root;
        Batch.Begin();
    }

    /// <summary>Draws what the frame's pass still holds. Pairs with BeginFrame.</summary>
    public void EndFrame() => Batch.End();

    /// <summary>Saves the current state and draws under newState until the matching PopRenderState, which every caller invokes exactly once for each push (Element.Draw's two RequiresContentViewport blocks).</summary>
    public void PushRenderState(RenderState newState)
    {
        _renderStateStack.Push(_currentRenderState);
        _currentRenderState = newState;
        ApplyCurrentRenderState();
    }

    /// <summary>Returns to exactly the state the matching PushRenderState saved.</summary>
    public void PopRenderState()
    {
        _currentRenderState = _renderStateStack.Pop();
        ApplyCurrentRenderState();
    }

    private void ApplyCurrentRenderState()
    {
        Batch.DrawOffset = _currentRenderState.DrawOffset;
        Batch.ClipRectangle = _currentRenderState.ClipRectangle;
    }
}
