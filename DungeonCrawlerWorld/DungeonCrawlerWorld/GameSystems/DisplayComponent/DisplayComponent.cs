using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;

namespace DungeonCrawlerWorld.GameComponents.DisplayComponent
{
    public class DisplayComponent : IGameComponent
    {
        private DataAccess _dataAccess;
        private GraphicsDevice _graphicsDevice;
        private SpriteBatchService _spriteBatchService;

        private Texture2D _unitRectangle;

        private List<IDisplayComponent> DisplayComponents { get; set; }
        private MapDisplay MapDisplayComponent;

        public bool CanUpdateWhilePaused { get { return true; } }

        public DisplayComponent()
        {
        }

        public void Initialize()
        {
            var dataAccessService = GameServices.GetService<DataAccessService>();
            _dataAccess = dataAccessService.Connect();
            _graphicsDevice = GameServices.GetService<GraphicsDevice>();
            _spriteBatchService = GameServices.GetService<SpriteBatchService>();

            //TODO better option for this
            _unitRectangle = new Texture2D(_graphicsDevice, 1, 1);
            _unitRectangle.SetData(new[] { Color.White });

            CreateDisplayComponents();
            foreach (var displayComponent in DisplayComponents)
            {
                displayComponent.Initialize();
            }
        }

        public void LoadContent()
        {
            foreach (var displayComponent in DisplayComponents)
            {
                displayComponent.LoadContent();
            }
        }

        public void UnloadContent() { }

        public void Update( GameTime gameTime)
        {
            HandleUserInput();

            foreach(var displayComponent in DisplayComponents)
            {
                displayComponent.Update(gameTime);
            }
        }

        public void Draw(GameTime gameTime)
        {
            var spriteBatch = _spriteBatchService.StartSpriteBatch();

            foreach (var displayComponent in DisplayComponents)
            {
                displayComponent.Draw(gameTime, spriteBatch, _unitRectangle);
            }

            _spriteBatchService.EndSpriteBatch();
        }

        public void CreateDisplayComponents()
        {
            var gameMapSize = _dataAccess.RetrieveMapSize();
            MapDisplayComponent = new MapDisplay(
                    _dataAccess,
                    displayPosition: new Vector2(10, 15),
                    displayMapSize: new Vector2(1650, 920),
                    gameMapSize,
                    tileSize: new Point(12, 12));

            DisplayComponents = new List<IDisplayComponent>
            {
                new DebugDisplay(
                    _dataAccess, 
                    new Vector2(10, 0), 
                    new Vector2(1440, 20)),
                MapDisplayComponent,
                new SelectionDisplay(
                    _dataAccess, 
                    new Vector2(1670, 15), 
                    new Vector2(180, 1440))
            };
        }

        //TODO move display position to data
        //Change this to hit data access
        //Remove MapDisplayComponent variable
        private void HandleUserInput()
        {
            //TODO figure out a better method than hard-coding this display
            var inputMode = InputMode.Map;

            if (inputMode == InputMode.Map)
            {
                if (Keyboard.GetState().IsKeyDown(Keys.D))
                {
                    MapDisplayComponent.MoveRight();
                }
                if (Keyboard.GetState().IsKeyDown(Keys.A))
                {
                    MapDisplayComponent.MoveLeft();
                }
                if (Keyboard.GetState().IsKeyDown(Keys.W))
                {
                    MapDisplayComponent.MoveUp();
                }
                if (Keyboard.GetState().IsKeyDown(Keys.S))
                {
                    MapDisplayComponent.MoveDown();
                }

                var mouseState = Mouse.GetState();
                if (mouseState.LeftButton == ButtonState.Pressed && MapDisplayComponent.DisplayRectangle.Contains(mouseState.Position))
                {
                    var selectedTile = MapDisplayComponent.SelectTile(mouseState.Position);
                    if (selectedTile != null)
                    {
                        _dataAccess.SelectMapNode(selectedTile.MapPosition);
                    }
                }
            }
        }
    }
}
