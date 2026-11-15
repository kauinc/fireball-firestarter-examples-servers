using System.Collections.Generic;

namespace FruitsSample
{
    public static class SlotsConst
    {
        public const long DefaultBet = 100;
        public const int ReelsCount = 3;

        public static readonly Dictionary<Symbol, float> DoublePayouts = new()
        {
            { Symbol.None, 0.0f },
            { Symbol.Blueberry, 2.0f },
            { Symbol.Lemon, 2.0f },
            { Symbol.Plum, 3.0f },
            { Symbol.Orange, 3.0f },
            { Symbol.Strawberry, 4.0f },
            { Symbol.Cherry, 5.0f },
            { Symbol.Banana, 5.0f },
            { Symbol.Watermelon, 7.0f },
            { Symbol.Seven, 25.0f }
        };

        public static readonly Dictionary<Symbol, float> TriplePayouts = new()
        {
            { Symbol.None, 0.0f },
            { Symbol.Blueberry, 5.0f },
            { Symbol.Lemon, 5.0f },
            { Symbol.Plum, 7.0f },
            { Symbol.Orange, 7.0f },
            { Symbol.Strawberry, 8.0f },
            { Symbol.Cherry, 10.0f },
            { Symbol.Banana, 12.0f },
            { Symbol.Watermelon, 15.0f },
            { Symbol.Seven, 100.0f }
        };
    }
}