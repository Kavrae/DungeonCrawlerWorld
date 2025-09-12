using System.Collections.Generic;

using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace DungeonCrawlerWorld.Services
{
    public interface IFontManager
    {
        public SpriteFont GetFont(string fontName);
    }

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

        public SpriteFont GetFont(string fontName)
        {
            SpriteFont font;

            if ( spriteFontsCache.ContainsKey(fontName))
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
