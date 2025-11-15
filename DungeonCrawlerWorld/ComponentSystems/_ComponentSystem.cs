using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.ComponentSystems
{
    /// <summary>
    /// Define the required properties of a system to act upon components via Update()
    /// The drawing of components is handled the user interface manager instead.
    /// </summary>
    public interface ComponentSystem
    {
        /// <summary>
        /// All components must have an update method, but not necessarily a draw method.
        /// </summary>
        public void Update(GameTime gameTime);

        /// <summary>
        /// The number of frames per update.
        /// This improves performance by ensuring that systems only Update when necessary on offset and out of sync frames.
        /// Note : if the framesPerUpdate is set to 5, there will be 4 frames between each update.
        /// </summary>
        public byte FramesPerUpdate { get; }
    }
}
