using System.Collections.Generic;

namespace DungeonCrawlerWorld.Actions
{
    //TODO Unused. Sort out later.
    public class GameEvent
    {
        public int Priority { get; set; }
        public List<IAction> Actions { get; set; }

        public GameEvent(int priority = 0)
        {
            Priority = priority;
            Actions = new List<IAction>();
        }

        public List<IAction> GetActions()
        {
            return Actions;
        }

        public void AddAction(IAction action)
        {
            Actions.Add(action);
        }
    }
}
