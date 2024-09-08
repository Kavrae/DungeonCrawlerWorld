using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace DungeonCrawlerWorld.GameComponents.DisplayComponent
{
    public class MapDisplay : IDisplayComponent
    {
        // Display Component
        public FontService FontService { get; set; }
        public Vector2 Position { get; set; }
        public Vector2 Size { get; set; }
        public Rectangle DisplayRectangle { get; set; }

        private DataAccess _dataAccess;

        public int DisplayColumnCount;
        public int DisplayRowCount;

        public Vector2 ScrollPosition;
        public float ScrollSpeed = 0.5f;
        public int ScrollColumnMin;
        public int ScrollColumnMax;
        public int ScrollRowMin;
        public int ScrollRowMax;
        public Rectangle DisplayedMapRectangle => new((int)ScrollPosition.X, (int)ScrollPosition.Y, DisplayColumnCount, DisplayRowCount);

        private MapTile[,] _mapTiles;
        public Point TileSize;

        public Point MousePosition;

        public bool RedrawWholeMapDisplay;

        public MapDisplay(DataAccess dataAccess, Vector2 displayPosition, Vector2 displayMapSize, Vector2 mapSize, Point? tileSize )
        {
            FontService = GameServices.GetService<FontService>();

            _dataAccess = dataAccess;

            Position = displayPosition;
            Size = displayMapSize;
            DisplayRectangle = new Rectangle((int)displayPosition.X, (int)displayPosition.Y, (int)displayMapSize.X, (int)displayMapSize.Y);

            TileSize = tileSize ?? new Point(12, 12);
            DisplayColumnCount = (int)(displayMapSize.X / TileSize.X);
            DisplayRowCount = (int)(displayMapSize.Y / TileSize.Y);

            ScrollPosition = new Vector2(0, 0);
            ScrollColumnMin = 0;
            ScrollColumnMax = (int)(mapSize.X - DisplayColumnCount);
            ScrollRowMin = 0;
            ScrollRowMax = (int)(mapSize.Y - DisplayRowCount);

            _mapTiles = new MapTile[DisplayColumnCount, DisplayRowCount];
            RedrawWholeMapDisplay = true;
        }

        public void Initialize() { }

        public void LoadContent() { }

        public void MoveLeft()
        {
            ScrollPosition = new Vector2(Math.Clamp(ScrollPosition.X - ScrollSpeed, ScrollColumnMin, ScrollColumnMax), ScrollPosition.Y);
            RedrawWholeMapDisplay = true;
        }

        public void MoveRight()
        {
            ScrollPosition = new Vector2(Math.Clamp(ScrollPosition.X + ScrollSpeed, ScrollColumnMin, ScrollColumnMax), ScrollPosition.Y);
            RedrawWholeMapDisplay = true;
        }

        public void MoveUp()
        {
            ScrollPosition = new Vector2(ScrollPosition.X, Math.Clamp(ScrollPosition.Y - ScrollSpeed, ScrollRowMin, ScrollRowMax));
            RedrawWholeMapDisplay = true;
        }

        public void MoveDown()
        {
            ScrollPosition = new Vector2(ScrollPosition.X, Math.Clamp(ScrollPosition.Y + ScrollSpeed, ScrollRowMin, ScrollRowMax));
            RedrawWholeMapDisplay = true;
        }

        public void Update(GameTime gameTime)
        {
            var mapNodes = _dataAccess.RetrieveMapNodes(DisplayedMapRectangle);

            for (int column = 0; column < DisplayColumnCount; column++)
            {
                for (int row = 0; row < DisplayRowCount; row++)
                {
                    var mapNode = mapNodes[column, row];
                    var displayPosition = new Point((int)Position.X + (column * TileSize.X), (int)Position.Y + (row * TileSize.Y));
                    _mapTiles[column, row] = new MapTile(mapNode, displayPosition, TileSize);
                }
            }
        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
        {
            var font = FontService.GetFont("defaultFont");
            for (int displayColumn = 0; displayColumn < DisplayColumnCount; displayColumn++)
            {
                for (int displayRow = 0; displayRow < DisplayRowCount; displayRow++)
                {
                    _mapTiles[displayColumn, displayRow].Draw(spriteBatch, font, unitRectangle);
                }
            }
        }

        public MapTile SelectTile(Point mousePosition)
        {
            MapTile selectedTile = null;
            foreach (var mapTile in _mapTiles)
            {
                if( mapTile.DisplayRectangle.Contains(mousePosition))
                {
                    selectedTile = mapTile;
                }
            }
            return selectedTile;
        }
    }
}
