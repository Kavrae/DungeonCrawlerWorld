using Presentation.Fonts;

namespace Tests.Presentation;

/// <summary>
/// One shared FontService for the entire test run. FontSystem owns real native FreeType
/// face/library handles (this package, FNA.NET.FontStashSharp, bundles FreeType as its built-in
/// rasterizer -- needed for DroidSansJapanese.ttf/Symbola-Emoji.ttf coverage StbTrueType can't
/// provide) and mutates a dynamic glyph atlas lazily as new glyphs get measured/rendered.
/// </summary>
/// <remarks>
/// Two things had to both be true, not just one:
/// 1. Exactly one instance for the whole run, not one per test (45+ call sites previously) and
///    not even one per parallel worker thread (tried first, via [ThreadStatic] -- still left
///    several native FreeType contexts alive and competing for finalization at once, and still
///    crashed intermittently). Every undisposed FontService here only ever gets cleaned up by its
///    finalizer, same as PresentationBootstrapper.Build's own single real-game instance -- but
///    unlike that instance (never crashes, nothing else is finalizing at the same time), more
///    than one FreeType-owning object competing for finalization is what caused an intermittent
///    0xC0000005 access violation inside FT_Done_Face at test-host shutdown. One instance for the
///    whole run reduces that count to exactly the same as production's already-proven-safe case.
/// 2. No two threads ever touch it at once. A single mutable FontSystem/its dynamic atlas is not
///    thread-safe -- genuinely sharing one instance across MSTestSettings.cs's parallel test
///    threads (tried second, a plain shared instance with no serialization) corrupted glyph
///    measurements under concurrent access (intermittent LabelRendererTests failures). Every test
///    class that touches this field is therefore marked [DoNotParallelize] (see each one's own
///    class attribute) -- MSTest pulls all such classes out of the parallel pool and runs them
///    serially relative to each other, which is sufficient here since no other test class ever
///    references TestFonts at all.
/// </remarks>
internal static class TestFonts
{
    public static readonly FontService Shared = new("Fonts");
}
