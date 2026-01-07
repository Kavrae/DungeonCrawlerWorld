using System;

namespace DungeonCrawlerWorld.Utilities
{
    public class MathUtility
    {
        private static Random randomizer { get; set; } = new Random();

        /// <summary>
        /// Integer implementation of FNA's MathHelper.Clamp
        /// </summary>
        public static int ClampInt(int value, int min, int max)
        {
            value = ((value > max) ? max : value);
            value = ((value < min) ? min : value);
            return value;
        }

        /// <summary>
        /// Short implementation of FNA's MathHelper.Clamp
        /// </summary>
        public static short ClampShort(short value, short min, short max)
        {
            value = ((value > max) ? max : value);
            value = ((value < min) ? min : value);
            return value;
        }

        /// <summary>
        /// Randomly select a value between 0 and maximumValue, excluding the specified values to skip.
        /// Values must be in ascending order.
        /// </summary>
        public static int RandomExceptFor(int maximumValue, int[] valuesToSkip, int valueToSkipCount)
        {
            int result = randomizer.Next(maximumValue - valueToSkipCount);

            for (int index = 0; index < valueToSkipCount; index++)
            {
                if (result < valuesToSkip[index])
                {
                    return result;
                }
                result++;
            }
            return result;
        }
    }
}
