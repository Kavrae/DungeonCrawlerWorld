using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace DungeonCrawlerWorld.Services
{
    public interface IFontManager
    {
        public SpriteFont GetFont(string fontName);
    }

    public class FontService : IFontManager
    {
        public ContentManager _contentManager;

        private Dictionary<string, SpriteFont> _spriteFonts;
        private readonly string _defaultFontName;

        public FontService(ContentManager contentManager)
        {
            _contentManager = contentManager;

            _defaultFontName = "defaultFont";
            _spriteFonts = new Dictionary<string, SpriteFont>
            {
                { _defaultFontName, _contentManager.Load<SpriteFont>(_defaultFontName) }
            };
        }

        public SpriteFont GetFont(string fontName)
        {
            SpriteFont font;

            if ( _spriteFonts.ContainsKey(fontName))
            {
                font = _spriteFonts[fontName];
            }
            else
            {
                font = _contentManager.Load<SpriteFont>(fontName);

                if (font != null)
                {
                    _spriteFonts[fontName] = font;
                }
                else
                {
                    font = _spriteFonts[_defaultFontName];
                }
            }

            return font;
        }
    }
}
