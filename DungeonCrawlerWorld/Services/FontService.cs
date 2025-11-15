using System.Collections.Generic;

using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace DungeonCrawlerWorld.Services
{
    public interface IFontManager
    {
        public SpriteFont GetFont(string fontName);
    }

    /// <summary>
    /// Service to encapsulate the instantiation and retrieval of SpriteFont assets.
    /// Each font will be loaded once during the first retrieval and then stored in the spriteFontsCache for future retrieval.
    /// DefaultFont will be utilized whenever a font is missing, instead of crashing the game.
    /// </summary>
    public class FontService : IFontManager
    {
        public ContentManager contentManager;

        private Dictionary<string, SpriteFont> spriteFontsCache;
        private readonly string defaultFontName;

        public FontService(ContentManager contentManager)
        {
            this.contentManager = contentManager;

            defaultFontName = "defaultFont";
            spriteFontsCache = new Dictionary<string, SpriteFont>
            {
                { defaultFontName, this.contentManager.Load<SpriteFont>(defaultFontName) }
            };
        }

        /// <summary>
        /// Retrieves the SpriteFont associated with the provided fontName.
        /// TODO The input value needs to be replaced with a font enum for safety.
        /// </summary>
        public SpriteFont GetFont(string fontName)
        {
            SpriteFont font;

            if (spriteFontsCache.ContainsKey(fontName))
            {
                font = spriteFontsCache[fontName];
            }
            else
            {
                font = contentManager.Load<SpriteFont>(fontName)
                    ?? spriteFontsCache[defaultFontName];

                spriteFontsCache[fontName] = font;
            }

            return font;
        }
    }
}
