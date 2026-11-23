using System;
using System.Collections.Generic;
using System.Linq;
using Fireball.Fireworks;
using Fireball.Fireworks.Models;

namespace DoofTroop.Betting
{
    /// <summary>
    /// GameMode-aware chip ladders and stake conversion (coins vs money).
    /// </summary>
    public static class BetMode
    {
        public static readonly decimal[] DefaultMoneyChips = Constants.ChipValues;

        public static readonly decimal[] DefaultCoinChips =
        {
            1m, 2m, 5m, 10m, 20m, 50m, 100m
        };

        public static bool IsCoins(BaseMessage message) =>
            string.Equals(message?.GameMode, FireballConstants.GameMode.COINS, StringComparison.OrdinalIgnoreCase)
            || string.Equals(message?.Currency, "COINS", StringComparison.OrdinalIgnoreCase)
            || string.Equals(message?.Currency, "COIN", StringComparison.OrdinalIgnoreCase);

        public static bool IsCoins(string gameMode, string currency) =>
            string.Equals(gameMode, FireballConstants.GameMode.COINS, StringComparison.OrdinalIgnoreCase)
            || string.Equals(currency, "COINS", StringComparison.OrdinalIgnoreCase)
            || string.Equals(currency, "COIN", StringComparison.OrdinalIgnoreCase);

        public static decimal MaxBetPerTarget(BaseMessage message) =>
            IsCoins(message) ? Constants.MaxBetPerTargetCoins : Constants.MaxBetPerTarget;

        /// <summary>
        /// Face values accepted for place-chip (currency units for money, coin units for coins).
        /// </summary>
        public static IReadOnlyList<decimal> AllowedChipFaces(BaseMessage message)
        {
            var serverVars = message?.GetServerVariables<ServerVars>();

            if (IsCoins(message))
            {
                if (serverVars?.coin_bets != null && serverVars.coin_bets.Count > 0)
                {
                    return serverVars.coin_bets.Select(v => (decimal)v).ToList();
                }

                return DefaultCoinChips;
            }

            if (serverVars?.core_bet_values != null && serverVars.core_bet_values.Count > 0)
            {
                var rate = CurrencyRate(message, serverVars);
                // core_bet_values are minor units (cents); UI/chip faces are major units.
                return serverVars.core_bet_values
                    .Select(v => BetMath.RoundMoney(v * rate / 100m))
                    .Where(v => v > 0)
                    .Distinct()
                    .ToList();
            }

            return DefaultMoneyChips;
        }

        public static bool IsAllowedChipValue(BaseMessage message, decimal value)
        {
            if (value <= 0) return false;
            var allowed = AllowedChipFaces(message);
            return allowed.Any(v => v == value);
        }

        /// <summary>
        /// Convert stake face total → RGS long.
        /// money: ×100 cents; coins: 1:1 integer units.
        /// </summary>
        public static long ToRgsAmount(BaseMessage message, decimal amount) =>
            IsCoins(message)
                ? (long)Math.Round(amount, MidpointRounding.AwayFromZero)
                : BetMath.ToCents(amount);

        /// <summary>
        /// RGS long → stake face total for responses / refunds.
        /// </summary>
        public static decimal FromRgsAmount(BaseMessage message, long amount) =>
            IsCoins(message) ? amount : BetMath.FromCents(amount);

        public static long ToRgsAmount(bool coins, decimal amount) =>
            coins
                ? (long)Math.Round(amount, MidpointRounding.AwayFromZero)
                : BetMath.ToCents(amount);

        public static decimal FromRgsAmount(bool coins, long amount) =>
            coins ? amount : BetMath.FromCents(amount);

        private static int CurrencyRate(BaseMessage message, ServerVars serverVars)
        {
            if (serverVars?.core_currency_rates == null || string.IsNullOrEmpty(message?.Currency))
            {
                return 1;
            }

            return serverVars.core_currency_rates.TryGetValue(message.Currency, out var rate)
                ? Math.Max(1, rate)
                : 1;
        }
    }
}
