using System.IO;

using FontStashSharp;

namespace DungeonCrawlerWorld.Services
{
    public interface IFontManager
    {
        public SpriteFontBase GetFont(int fontSize);
    }

    /// <summary>
    /// Service to encapsulate the instantiation and retrieval of Font assets.
    /// </summary>
    public class FontService : IFontManager
    {
        private readonly FontSystem _fontSystem;
        private const string _fontDirectory = "Content/Fonts";

        public FontService()
        {
            _fontSystem = new FontSystem(); 
            _fontSystem.AddFont(File.ReadAllBytes($"{_fontDirectory}/DroidSans.ttf"));
        }

        /// <summary>
        /// Retrieves a SpriteFontBase associated for the provided font size
        /// </summary>
        public SpriteFontBase GetFont( int fontSize)
        {
            return _fontSystem.GetFont(fontSize);
        }
    }
}
