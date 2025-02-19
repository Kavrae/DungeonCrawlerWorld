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

        private Dictionary<string, SpriteFont> spriteFonts;
        private readonly string defaultFontName;

        public FontService(ContentManager contentManager)
        {
            this.contentManager = contentManager;

            defaultFontName = "defaultFont";
            spriteFonts = new Dictionary<string, SpriteFont>
            {
                { defaultFontName, this.contentManager.Load<SpriteFont>(defaultFontName) }
            };
        }

        public SpriteFont GetFont(string fontName)
        {
            SpriteFont font;

            if ( spriteFonts.ContainsKey(fontName))
            {
                font = spriteFonts[fontName];
            }
            else
            {
                font = contentManager.Load<SpriteFont>(fontName);

                if (font != null)
                {
                    spriteFonts[fontName] = font;
                }
                else
                {
                    font = spriteFonts[defaultFontName];
                }
            }

            return font;
        }
    }
}
