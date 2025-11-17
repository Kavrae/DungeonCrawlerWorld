namespace DungeonCrawlerWorld.Utilities
{
    public class MathUtility
    {
        public static int Clamp(int value, int min, int max)
        {
            value = ((value > max) ? max : value);
            value = ((value < min) ? min : value);
            return value;
        }
    }
}
