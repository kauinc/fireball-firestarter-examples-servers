namespace DoofTroop.Paytable
{
    public static class DoofIndex
    {
        public static (string Color, string Pattern, string Accessory)? FromIndex(int index)
        {
            var colorCount = Constants.DoofColors.Length;
            var patternCount = Constants.DoofPatterns.Length;
            if (index < 0 || index >= colorCount * patternCount)
            {
                return null;
            }

            var colorIndex = index % colorCount;
            var patternIndex = index / colorCount;
            var accessoryIndex = (index + patternIndex) % 2;

            return (
                Constants.DoofColors[colorIndex],
                Constants.DoofPatterns[patternIndex],
                Constants.DoofAccessories[accessoryIndex]
            );
        }

        public static string AccessoryFor(string color, string pattern)
        {
            var colorIndex = System.Array.IndexOf(Constants.DoofColors, color);
            var patternIndex = System.Array.IndexOf(Constants.DoofPatterns, pattern);
            if (colorIndex < 0 || patternIndex < 0)
            {
                return null;
            }

            var index = patternIndex * Constants.DoofColors.Length + colorIndex;
            return FromIndex(index)?.Accessory;
        }
    }
}
