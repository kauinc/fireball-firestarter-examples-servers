using System;
using System.Collections.Generic;
using System.Linq;
using Fireball.Fireworks;
using Fireball.Fireworks.Models;

namespace DoofTroop.Betting
{
    /// <summary>
    /// GameMode-aware chip ladders, rate scaling, and stake conversion (coins vs money).
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

        /// <summary>
        /// Ladder / max-bet rate from server_vars (fallback 1).
        /// </summary>
        public static int CurrencyRate(BaseMessage message)
        {
            var serverVars = message?.GetServerVariables<ServerVars>();
            return CurrencyRate(message?.Currency, serverVars?.core_currency_rates);
        }

        public static int CurrencyRate(string currency, IDictionary<string, int> rates)
        {
            if (rates == null || string.IsNullOrEmpty(currency))
            {
                return 1;
            }

            return rates.TryGetValue(currency, out var rate) ? Math.Max(1, rate) : 1;
        }

        /// <summary>
        /// Exposure rate (Aphrodite-style): USD/EUR → 1; else client_vars then server_vars.
        /// Returns 0 when unresolved (skip exposure cap).
        /// </summary>
        public static int ExposureCurrencyRate(BaseMessage message)
        {
            if (string.Equals(message?.Currency, "USD", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message?.Currency, "EUR", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            var currency = message?.Currency;
            if (string.IsNullOrEmpty(currency))
            {
                return 0;
            }

            var clientVars = message?.GetClientVariables<ClientVars>();
            if (clientVars?.core_currency_rates != null
                && clientVars.core_currency_rates.TryGetValue(currency, out var clientRate)
                && clientRate > 0)
            {
                return clientRate;
            }

            var serverVars = message?.GetServerVariables<ServerVars>();
            if (serverVars?.core_currency_rates != null
                && serverVars.core_currency_rates.TryGetValue(currency, out var serverRate)
                && serverRate > 0)
            {
                return serverRate;
            }

            return 0;
        }

        /// <summary>
        /// Max stake per target in HUD / face units.
        /// Coins: fixed; money: base max × currency rate.
        /// </summary>
        public static decimal MaxBetPerTarget(BaseMessage message)
        {
            if (IsCoins(message))
            {
                return Constants.MaxBetPerTargetCoins;
            }

            var rate = CurrencyRate(message);
            return BetMath.RoundMoney(Constants.MaxBetPerTarget * rate);
        }

        public static decimal MaxBetPerTarget(bool coins, int currencyRate) =>
            coins
                ? Constants.MaxBetPerTargetCoins
                : BetMath.RoundMoney(Constants.MaxBetPerTarget * Math.Max(1, currencyRate));

        /// <summary>
        /// Default chip face from core_bet_default × rate, snapped to the allowed ladder.
        /// </summary>
        public static decimal DefaultChipFace(BaseMessage message)
        {
            var allowed = AllowedChipFaces(message);
            return DefaultChipFace(
                IsCoins(message),
                message?.GetServerVariables<ServerVars>(),
                CurrencyRate(message),
                allowed);
        }

        public static decimal DefaultChipFace(
            bool coins,
            ServerVars serverVars,
            int currencyRate,
            IReadOnlyList<decimal> allowedFaces)
        {
            if (allowedFaces == null || allowedFaces.Count == 0)
            {
                return coins ? 1m : DefaultMoneyChips[0];
            }

            if (serverVars == null || serverVars.core_bet_default <= 0)
            {
                return allowedFaces[0];
            }

            decimal candidate = coins
                ? serverVars.core_bet_default
                : BetMath.RoundMoney(serverVars.core_bet_default * Math.Max(1, currencyRate) / 100m);

            return SnapToNearestFace(allowedFaces, candidate);
        }

        /// <summary>
        /// Cap win to exposure_max (RGS / minor units) for money mode. Coins and exposure_max≤0 skip.
        /// </summary>
        public static (long capped, bool wasCapped) ApplyExposureCap(BaseMessage message, long winRgs)
        {
            if (IsCoins(message) || winRgs <= 0)
            {
                return (winRgs, false);
            }

            var serverVars = message?.GetServerVariables<ServerVars>();
            if (serverVars == null || serverVars.exposure_max <= 0)
            {
                return (winRgs, false);
            }

            var rate = ExposureCurrencyRate(message);
            return ApplyExposureCap(winRgs, serverVars.exposure_max, rate);
        }

        public static (long capped, bool wasCapped) ApplyExposureCap(
            long winRgs,
            long exposureMax,
            int exposureRate)
        {
            if (winRgs <= 0 || exposureMax <= 0 || exposureRate <= 0)
            {
                return (winRgs, false);
            }

            var winUsd = winRgs / exposureRate;
            if (winUsd < exposureMax)
            {
                return (winRgs, false);
            }

            var capped = exposureMax * exposureRate;
            return (capped, capped != winRgs);
        }

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
                var rate = CurrencyRate(message?.Currency, serverVars.core_currency_rates);
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

        public static decimal SnapToNearestFace(IReadOnlyList<decimal> allowed, decimal value)
        {
            if (allowed == null || allowed.Count == 0)
            {
                return value;
            }

            decimal best = allowed[0];
            decimal bestDist = Math.Abs(best - value);
            for (var i = 1; i < allowed.Count; i++)
            {
                var dist = Math.Abs(allowed[i] - value);
                if (dist < bestDist)
                {
                    best = allowed[i];
                    bestDist = dist;
                }
            }

            return best;
        }
    }
}
