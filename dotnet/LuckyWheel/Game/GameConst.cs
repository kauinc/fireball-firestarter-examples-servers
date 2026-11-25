using System.Collections.Generic;

namespace LuckyWheel
{
    public static class GameConst
    {
        public const string JackpotTemplateId = "92d5b3f8-595e-4eda-a417-2df52ac9e4f0";
        public const float JackpotContributionPercent = 0.5f;
        public const long DefaultBet = 100;
        public const int SectorsCount = 8;

        public static readonly Dictionary<Symbol, float> SymbolPayouts = new()
        {
            { Symbol.Blueberry, 2.0f },
            { Symbol.Lemon, 2.0f },
            { Symbol.Plum, 3.0f },
            { Symbol.Orange, 3.0f },
            { Symbol.Strawberry, 4.0f },
            { Symbol.Cherry, 5.0f },
            { Symbol.Banana, 5.0f },
            { Symbol.Watermelon, 7.0f },
        };
    }
}