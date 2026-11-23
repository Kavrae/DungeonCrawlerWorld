using UIEngine.Fonts;

namespace TestSupport;

/// <summary>The one FontService of a test run.</summary>
/// <remarks>
/// It owns native FreeType handles and a glyph atlas it fills as glyphs are measured. Two things must both hold: one
/// instance per process, because several finalizing at once crashed the test host in FT_Done_Face at shutdown, and no two
/// threads using it at once, because that corrupted glyph measurements. CAD0004 fails the build for a test that reaches
/// it without [DoNotParallelize].
/// </remarks>
[SerialTestResource("Fonts")]
public static class TestFonts
{
    public static readonly FontService Shared = new("Fonts");
}
