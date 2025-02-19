using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    //TODO implement announcement display, starting with "Pause" window.
    //This will be used extensively and should be placable in various locations.
    public class AnnouncementDisplay : UserInterfaceComponent
    {
        private int _maxWidth = 620; //TODO default size and position calculation on constructor
        //TODO best way to send a new announcement?
        //TODO Close button (x) on top right.
        //always pause during announcement.  Space both unpauses + closes the announcement.

        public AnnouncementDisplay(Point position, Point size) : base(position, size)
        {
        }

        public override void Draw(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
        {
            throw new NotImplementedException();
        }

        public override void Initialize()
        {
            throw new NotImplementedException();
        }

        public override void LoadContent()
        {
            throw new NotImplementedException();
        }

        public override void Update(GameTime gameTime)
        {
            throw new NotImplementedException();
        }
    }
}
