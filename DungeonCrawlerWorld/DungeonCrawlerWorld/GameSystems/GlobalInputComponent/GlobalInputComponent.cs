using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.GameComponents.GlobalInputComponent
{
    public class GlobalInputComponent : IGameComponent
    {
        //TODO register the service
        private DataAccess _dataAccess;
        private KeyboardState PreviousKeyboardState;
        public bool CanUpdateWhilePaused { get { return true; } }

        public GlobalInputComponent()
        {
        }

        public void Draw(GameTime gameTime) { }

        public void Initialize()
        {
            var dataAccessService = GameServices.GetService<DataAccessService>();
            _dataAccess = dataAccessService.Connect();

            PreviousKeyboardState = Keyboard.GetState();
        }

        public void LoadContent() { }

        public void UnloadContent() { }

        public void Update(GameTime gameTime)
        {
            var currentKeyboardState = Keyboard.GetState();
            if (currentKeyboardState.IsKeyDown(Keys.Space) && !PreviousKeyboardState.IsKeyDown(Keys.Space))
            {
                _dataAccess.ToggleIsPaused();
                //TODO move to global input manager component and trigger _dataAccess.ToggleIsPause
                //TODO should also not trigger when in DataEntry mode, where space is treated as a character. Change DisplayMode to InputMode.
            }

            PreviousKeyboardState = currentKeyboardState;
        }
    }
}
