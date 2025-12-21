namespace DungeonCrawlerWorld.Utilities
{
    public class MathUtility
    {
        public static int ClampInt(int value, int min, int max)
        {
            value = ((value > max) ? max : value);
            value = ((value < min) ? min : value);
            return value;
        }

        public static short ClampShort(short value, short min, short max)
        {
            value = ((value > max) ? max : value);
            value = ((value < min) ? min : value);
            return value;
        }
    }
}
