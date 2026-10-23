using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace UIEngine.Rendering;

/// <summary>What everything in the UI draws with: the device, the one SpriteEffectBatch, the unit rectangle, and the render-state stack that nested passes push and pop.</summary>
/// <remarks>
/// <para>
/// One per host, built by UiEngineBootstrapper and filled once by Initialize from the host's LoadContent; until then (and in a
/// headless test that never draws) its members are unset. Elements reach it as Element.Render -- ElementPoolService hands it to
/// every element it builds -- and anything outside the element tree that draws (the layer host, the map renderer) takes it.
/// </para>
/// <para>
/// The whole UI is one pass through the sprite effect, in call order: BeginFrame starts it each frame and EndFrame draws what is
/// left; a scrollable element pushes a pass with its own viewport or scissor and pops back to exactly what it inherited. Sprites
/// and text share it, so outlined text is two quads a glyph in its place, and the map lights its own draws by setting the
/// batch's Glow and clearing it.
/// </para>
/// </remarks>
public sealed class UiRenderService
{
    /// <summary>
    /// What a pass begins with that the UI varies -- the sampler, the rasterizer (its scissor clip) and the transform -- plus
    /// the two GraphicsDevice properties Element.Draw's scoped passes also swap (Viewport, ScissorRectangle): everything a
    /// nested pass needs to hand back the exact ambient state it inherited once its own scope ends, rather than resetting to
    /// some hardcoded default. See PushRenderState/PopRenderState.
    /// </summary>
    public readonly record struct RenderState(SamplerState SamplerState, RasterizerState? RasterizerState, Matrix TransformMatrix, Viewport Viewport, Rectangle ScissorRectangle);

    private readonly Stack<RenderState> _renderStateStack = new();
    private RenderState _currentRenderState;

    public GraphicsDevice GraphicsDevice { get; private set; } = null!;

    /// <summary>What every draw in the UI goes through, between BeginFrame and EndFrame.</summary>
    public SpriteEffectBatch Batch { get; private set; } = null!;

    public Texture2D UnitRectangle { get; private set; } = null!;

    /// <summary>
    /// Currently-active ambient render state. Element.Draw's own scoped passes only ever compose
    /// a new RenderState off this one (via `with`, overriding just the field(s) that pass cares
    /// about) rather than constructing one from scratch -- see Draw's two RequiresContentViewport
    /// blocks -- so anything they don't explicitly touch keeps flowing through unchanged no
    /// matter how deep the nesting.
    /// </summary>
    public RenderState CurrentRenderState => _currentRenderState;

    /// <summary>Captures the device and what draws with it -- called once, from the host's LoadContent, after all of them exist.</summary>
    public void Initialize(GraphicsDevice graphicsDevice, SpriteEffectBatch batch, Texture2D unitRectangle)
    {
        GraphicsDevice = graphicsDevice;
        Batch = batch;
        UnitRectangle = unitRectangle;
    }

    /// <summary>Begins the frame's pass under the root state -- point sampling, no clip or transform, the device's viewport -- which every nested Push/PopRenderState ultimately unwinds back to. Called once per frame, from the host's Draw.</summary>
    public void BeginFrame()
    {
        _renderStateStack.Clear();
        _currentRenderState = new RenderState(SamplerState.PointClamp, null, Matrix.Identity, GraphicsDevice.Viewport, GraphicsDevice.ScissorRectangle);
        ApplyCurrentRenderState();
    }

    /// <summary>Draws what the frame's pass still holds. Pairs with BeginFrame.</summary>
    public void EndFrame() => Batch.End();

    /// <summary>
    /// Ends the current pass, saves its state, and begins a new one with newState.
    /// Pairs with PopRenderState, which every caller must invoke exactly once for each
    /// PushRenderState (see Element.Draw's two RequiresContentViewport blocks, the only caller).
    /// Pop restores the exact prior state, not a plain Begin, so a nested scrollable Element hands
    /// back whatever RasterizerState/TransformMatrix/SamplerState its ancestor's pass had active.
    /// </summary>
    public void PushRenderState(RenderState newState)
    {
        Batch.End();
        _renderStateStack.Push(_currentRenderState);
        _currentRenderState = newState;
        ApplyCurrentRenderState();
    }

    /// <summary>See PushRenderState.</summary>
    public void PopRenderState()
    {
        Batch.End();
        _currentRenderState = _renderStateStack.Pop();
        ApplyCurrentRenderState();
    }

    private void ApplyCurrentRenderState()
    {
        GraphicsDevice.Viewport = _currentRenderState.Viewport;
        GraphicsDevice.ScissorRectangle = _currentRenderState.ScissorRectangle;
        Batch.Begin(_currentRenderState.SamplerState, _currentRenderState.RasterizerState, _currentRenderState.TransformMatrix);
    }
}
