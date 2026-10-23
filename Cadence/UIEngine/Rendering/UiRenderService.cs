using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace UIEngine.Rendering;

/// <summary>What everything in the UI draws with: the device, the shared SpriteBatch and SpriteEffectBatch, the unit rectangle, and the render-state stack that nested passes push and pop.</summary>
/// <remarks>
/// <para>
/// One per host, built by UiEngineBootstrapper and filled once by Initialize from the host's LoadContent; until then (and in a
/// headless test that never draws) its members are unset. Elements reach it as Element.Render -- ElementPoolService hands it to
/// every element it builds -- and anything outside the element tree that draws (the layer host, the map renderer) takes it.
/// </para>
/// <para>
/// The stack holds the ambient SpriteBatch pass: ResetRenderState starts it each frame, a scrollable element pushes a pass with its
/// own viewport or scissor and pops back to exactly what it inherited, and a sprite-effect pass (the map) ends the SpriteBatch pass,
/// draws through SpriteEffectBatch under the same state, and resumes it.
/// </para>
/// </remarks>
public sealed class UiRenderService
{
    /// <summary>
    /// Full SpriteBatch.Begin parameter set plus the two GraphicsDevice properties Element.Draw's
    /// scoped passes also swap (Viewport, ScissorRectangle) -- everything a nested pass needs to
    /// hand back the exact ambient state it inherited once its own scope ends, rather than
    /// resetting to some hardcoded default. See PushRenderState/PopRenderState.
    /// </summary>
    public readonly record struct RenderState(SamplerState SamplerState, DepthStencilState? DepthStencilState, RasterizerState? RasterizerState, Effect? Effect, Matrix TransformMatrix, Viewport Viewport, Rectangle ScissorRectangle);

    private readonly Stack<RenderState> _renderStateStack = new();
    private RenderState _currentRenderState;

    public GraphicsDevice GraphicsDevice { get; private set; } = null!;

    public SpriteBatch SpriteBatch { get; private set; } = null!;

    /// <summary>What a pass through the sprite effect draws with, between BeginSpriteEffectPass and EndSpriteEffectPass.</summary>
    public SpriteEffectBatch SpriteEffectBatch { get; private set; } = null!;

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
    public void Initialize(GraphicsDevice graphicsDevice, SpriteBatch spriteBatch, SpriteEffectBatch spriteEffectBatch, Texture2D unitRectangle)
    {
        GraphicsDevice = graphicsDevice;
        SpriteBatch = spriteBatch;
        SpriteEffectBatch = spriteEffectBatch;
        UnitRectangle = unitRectangle;
    }

    /// <summary>
    /// Establishes the root ambient state every nested Push/PopRenderState call ultimately
    /// unwinds back to. Called once per frame, from the host's Draw right after
    /// SpriteBatchRenderer.StartSpriteBatch, whose own Begin call this has to mirror exactly --
    /// there's no way to ask a SpriteBatch what parameters its currently-active Begin used.
    /// </summary>
    public void ResetRenderState()
    {
        _renderStateStack.Clear();
        _currentRenderState = new RenderState(SamplerState.PointClamp, null, null, null, Matrix.Identity, GraphicsDevice.Viewport, GraphicsDevice.ScissorRectangle);
    }

    /// <summary>
    /// Ends the currently-active SpriteBatch pass, saves it, and begins a new one with newState.
    /// Pairs with PopRenderState, which every caller must invoke exactly once for each
    /// PushRenderState (see Element.Draw's two RequiresContentViewport blocks, the only caller).
    /// Pop restores the exact prior state, not a plain Begin, so a nested scrollable Element hands
    /// back whatever RasterizerState/TransformMatrix/SamplerState its ancestor's pass had active.
    /// </summary>
    public void PushRenderState(RenderState newState)
    {
        SpriteBatch.End();
        _renderStateStack.Push(_currentRenderState);
        _currentRenderState = newState;
        ApplyCurrentRenderState();
    }

    /// <summary>See PushRenderState.</summary>
    public void PopRenderState()
    {
        SpriteBatch.End();
        _currentRenderState = _renderStateStack.Pop();
        ApplyCurrentRenderState();
    }

    /// <summary>
    /// Ends the active SpriteBatch pass and begins SpriteEffectBatch under the current render state's sampler, rasterizer (its
    /// scissor clip) and transform -- the viewport and scissor rectangle are already the device's. Pairs with
    /// EndSpriteEffectPass, which every caller must invoke exactly once.
    /// </summary>
    public SpriteEffectBatch BeginSpriteEffectPass()
    {
        SpriteBatch.End();
        SpriteEffectBatch.Begin(_currentRenderState.SamplerState, _currentRenderState.RasterizerState, _currentRenderState.TransformMatrix);
        return SpriteEffectBatch;
    }

    /// <summary>Draws what the sprite effect pass collected, then resumes the SpriteBatch pass exactly as it was. See BeginSpriteEffectPass.</summary>
    public void EndSpriteEffectPass()
    {
        SpriteEffectBatch.End();
        ApplyCurrentRenderState();
    }

    private void ApplyCurrentRenderState()
    {
        GraphicsDevice.Viewport = _currentRenderState.Viewport;
        GraphicsDevice.ScissorRectangle = _currentRenderState.ScissorRectangle;
        SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, _currentRenderState.SamplerState, _currentRenderState.DepthStencilState, _currentRenderState.RasterizerState, _currentRenderState.Effect, _currentRenderState.TransformMatrix);
    }
}
