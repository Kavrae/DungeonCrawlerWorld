using System;
using System.Collections.Generic;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;
using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    public class MapWindow : Window
    {
        private readonly World world;
        private GraphicsDevice graphicsDevice;

        private Dictionary<FontType, SpriteFont> fonts;

        private Point currentScrollPosition;
        private Point maxScrollPosition;

        private Tile[,] tiles;
        private Point currentTileSize;
        private int tileColumns;
        private int tileRows;
        private int tileDepth;
        private Vector2? selectedTileCoordinates;
        private bool resetAllTiles;
        private bool isScrolling;
        private bool skipUpdate;

        private ZoomLevel currentZoomLevel;
        private Dictionary<ZoomLevel, Point> tileSizes;

        public MapWindow(World world, Point? tileSize, WindowOptions windowOptions) : base(null, windowOptions)
        {
            this.world = world;

            currentScrollPosition = new Point(0, 0);
            currentZoomLevel = ZoomLevel.Team;

            tileSizes = new Dictionary<ZoomLevel, Point>
            {
                { ZoomLevel.Team, new Point(12,12)},
                { ZoomLevel.Neighborhood, new Point(6,6)},
                { ZoomLevel.Borough, new Point(3,3)}
            };

            currentTileSize = tileSize ?? tileSizes[currentZoomLevel];

            tileDepth = this.world.Map.Size.Z;
            resetAllTiles = false;
            isScrolling = false;
            skipUpdate = false;
        }

        public override void Initialize()
        {
            base.Initialize();

            graphicsDevice = GameServices.GetService<GraphicsDevice>();

            var defaultMediumFont = FontService.GetFont("DefaultMediumFont");
            var defaultLargeFont = FontService.GetFont("DefaultLargeFont");
            var defaultHugeFont = FontService.GetFont("DefaultHugeFont");
            fonts = new Dictionary<FontType, SpriteFont>
            {
                { FontType.DefaultMedium, defaultMediumFont },
                { FontType.DefaultLarge, defaultLargeFont },
                { FontType.DefaultHuge, defaultHugeFont }
            };

            tileColumns = (int)Math.Floor(ContentSize.X / currentTileSize.X);
            tileRows = (int)Math.Floor(ContentSize.Y / currentTileSize.Y);

            UpdateMaxScrollPosition();
            ResetTiles();
        }

        public override void LoadContent()
        {
            base.LoadContent();
        }

        public override void Update(GameTime gameTime)
        {
            if (isScrolling)
            {
                skipUpdate = !skipUpdate;
            }

            if (skipUpdate || tiles == null)
            {
                return;
            }

            for (var x = 0; x < tileColumns; x++)
            {
                var mapNodeX = currentScrollPosition.X + x;
                for (var y = 0; y < tileRows; y++)
                {
                    var mapNodeY = currentScrollPosition.Y + y;

                    var tile = tiles[x, y];
                    var mapNodePositionChanged = mapNodeX != tile.MapNodePosition.X || mapNodeY != tile.MapNodePosition.Y;
                    if (mapNodePositionChanged)
                    {
                        tile.MapNodePosition = new Point(mapNodeX, mapNodeY);
                    }

                    for (var z = 0; z < tileDepth; z++)
                    {
                        var mapNode = world.Map.MapNodes[mapNodeX, mapNodeY, z];
                        if (mapNode.HasChanged || mapNodePositionChanged)
                        {
                            if (mapNode.HasChanged)
                            {
                                mapNode.HasChanged = false;
                                world.Map.MapNodes[mapNodeX, mapNodeY, z] = mapNode;
                            }
                            tile.HasChanged = true;
                        }
                    }

                    if (tile.HasChanged || resetAllTiles)
                    {
                        tile.HasChanged = false;
                        tile.BackgroundColor = null;
                        tile.Glyph = null;
                        tile.ClassGlypyh = null;

                        var backgroundFound = false;
                        var backgroundComponent = new BackgroundComponent();
                        var glyphFound = false;
                        var glyphComponent = new GlyphComponent();
                        var classGlyphFound = false;
                        var classGlyphComponent = new ClassGlyphComponent();

                        for (var z = tileDepth - 1; z >= 0; z--)
                        {
                            var mapNode = world.Map.MapNodes[tile.MapNodePosition.X, tile.MapNodePosition.Y, z];
                            if (mapNode.EntityId != null)
                            {
                                if (!backgroundFound)
                                {
                                    backgroundFound = ComponentRepo.BackgroundComponents.TryGetValue(mapNode.EntityId.Value, out backgroundComponent);
                                }
                                if (!glyphFound)
                                {
                                    glyphFound = ComponentRepo.GlyphComponents.TryGetValue(mapNode.EntityId.Value, out glyphComponent);

                                    if (glyphFound)
                                    {
                                        classGlyphFound = ComponentRepo.ClassGlyphComponents.TryGetValue(mapNode.EntityId.Value, out classGlyphComponent);
                                    }
                                }

                                if (backgroundFound && glyphFound)
                                {
                                    break;
                                }
                            }
                        }

                        if (backgroundFound)
                        {
                            tile.BackgroundColor = backgroundComponent.BackgroundColor;
                        }

                        if (glyphFound && ComponentRepo.TransformComponents.TryGetValue(glyphComponent.EntityId, out TransformComponent transformComponent))
                        {
                            //Note : If the transform position doesn't match the tile's mapNode position, it's a multi-tile transform and shouldn't be drawn again.
                            //Check for X and Y coordinate, because it could match any of the Z coordinates
                            if (transformComponent.Position.X == tile.MapNodePosition.X
                                && transformComponent.Position.Y == tile.MapNodePosition.Y)
                            {
                                tile.ForegroundColor = glyphComponent.GlyphColor;
                                tile.Glyph = glyphComponent.Glyph;
                                tile.GlyphDrawPosition = new Vector2(
                                    tile.DrawRectangle.Left + glyphComponent.GlyphOffset.X + ((transformComponent.Size.X - 1) * currentTileSize.X / 2),
                                    tile.DrawRectangle.Top + glyphComponent.GlyphOffset.Y);
                                tile.Size = transformComponent.Size;

                                if (classGlyphFound)
                                {
                                    tile.ClassGlypyh = classGlyphComponent.Glyph;
                                    tile.ClassGlyphDrawPosition = tile.GlyphDrawPosition + new Vector2((int)(currentTileSize.X * (transformComponent.Size.X - 0.5)), currentTileSize.Y / -2);
                                }
                            }
                        }
                        tiles[x, y] = tile;
                    }
                }
            }

            resetAllTiles = false;
            isScrolling = false;

            base.Update(gameTime);
        }

        public override void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
        {
            if (tiles != null)
            {
                DrawBackgrounds(gameTime, spriteBatch, unitRectangle);
                DrawGlyphs(gameTime, spriteBatch);
            }
        }

        public void DrawBackgrounds(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
        {
            //TODO tile size scaling
            for (var column = 0; column < tileColumns; column++)
            {
                for (var row = 0; row < tileRows; row++)
                {
                    var tile = tiles[column, row];

                    if (tile.BackgroundColor != null)
                    {
                        var backgroundRectangle = tile.IsSelected
                                ? tile.InnerDrawRectangle
                                : tile.DrawRectangle;

                        spriteBatch.Draw(unitRectangle, backgroundRectangle, tile.BackgroundColor.Value);
                    }
                }
            }
        }

        public void DrawGlyphs(GameTime gameTime, SpriteBatch spriteBatch)
        {
            for (var column = 0; column < tileColumns; column++)
            {
                for (var row = 0; row < tileRows; row++)
                {
                    var tile = tiles[column, row];

                    if (tile.ForegroundColor != null && !string.IsNullOrWhiteSpace(tile.Glyph))
                    {
                        var fontType = FontType.DefaultMedium;
                        if (tile.Size.X == 2)
                        {
                            fontType = FontType.DefaultLarge;
                        }
                        else if (tile.Size.X == 3)
                        {
                            fontType = FontType.DefaultHuge;
                        }

                        spriteBatch.DrawString(fonts[fontType], tile.Glyph, tile.GlyphDrawPosition, tile.ForegroundColor.Value);

                        if(!string.IsNullOrWhiteSpace(tile.ClassGlypyh) )
                        {
                            spriteBatch.DrawString(fonts[FontType.DefaultMedium], tile.ClassGlypyh, tile.ClassGlyphDrawPosition, tile.ForegroundColor.Value);
                        }
                    }
                }
            }
        }

        private void ResetTiles()
        {
            var currentTileSize = tileSizes[currentZoomLevel];

            tiles = new Tile[tileColumns, tileRows];
            for (var column = 0; column < tileColumns; column++)
            {
                for (var row = 0; row < tileRows; row++)
                {
                    var tilePosition = new Vector2(
                        _contentAbsolutePosition.X + (column * currentTileSize.X),
                        _contentAbsolutePosition.Y + (row * currentTileSize.Y));

                    var newTile = new Tile
                    {
                        DrawRectangle = new Rectangle((int)tilePosition.X, (int)tilePosition.Y, currentTileSize.X, currentTileSize.Y),
                        InnerDrawRectangle = new Rectangle((int)tilePosition.X + 1, (int)tilePosition.Y + 1, currentTileSize.X - 2, currentTileSize.Y - 2),
                        MapNodePosition = new Point(column, row)
                    };
                    tiles[column, row] = newTile;
                }
            }
        }

        private void UpdateMaxScrollPosition()
        {
            maxScrollPosition = world.Map.Size.ToPointXY() - new Point(tileColumns, tileRows);
        }

        public void UpdateZoomLevel(ZoomLevel newZoomLevel)
        {
            currentZoomLevel = newZoomLevel;
        }

        //TODO scroll selected map node too
        public void UpdateScrollPosition(Point scrollChange)
        {
            currentScrollPosition = new Point
            {
                X = MathHelper.Clamp(currentScrollPosition.X + scrollChange.X, 0, maxScrollPosition.X),
                Y = MathHelper.Clamp(currentScrollPosition.Y + scrollChange.Y, 0, maxScrollPosition.Y)
            };
            resetAllTiles = true;
            isScrolling = true;
        }

        //TODO select mapNode vs select entity. Former stays on that mapNode. Latter follows the entity selected and updates selected tile
        public void SelectMapNodes(Vector2 mousePosition)
        {
            if (tiles != null)
            {
                var relativeMapDisplayMousePosition = mousePosition - _contentAbsolutePosition;
                var x = (int)(relativeMapDisplayMousePosition.X / currentTileSize.X);
                var y = (int)(relativeMapDisplayMousePosition.Y / currentTileSize.Y);

                if (x < tileColumns && y < tileRows)
                {
                    var newTileCoordinates = new Vector2(x, y);

                    if (selectedTileCoordinates != newTileCoordinates)
                    {
                        if (selectedTileCoordinates != null)
                        {
                            var oldTile = tiles[(int)selectedTileCoordinates.Value.X, (int)selectedTileCoordinates.Value.Y];
                            oldTile.IsSelected = false;
                            tiles[(int)selectedTileCoordinates.Value.X, (int)selectedTileCoordinates.Value.Y] = oldTile;
                        }

                        selectedTileCoordinates = newTileCoordinates;
                        var newTile = tiles[x, y];
                        newTile.IsSelected = true;
                        tiles[x, y] = newTile;

                        world.SelectedMapNodes ??= new MapNode[world.Map.Size.Z];
                        for (var z = 0; z < world.Map.Size.Z; z++)
                        {
                            world.SelectedMapNodes[z] = world.Map.MapNodes[newTile.MapNodePosition.X, newTile.MapNodePosition.Y, z];
                        }
                    }
                }
            }
        }

        public override void HandleTitleClickDown(Vector2 mousePosition)
        {
            //Does nothing
        }

        public override void HandleContentClickDown(Vector2 mousePosition)
        {
            SelectMapNodes(mousePosition);
        }
    }
}
