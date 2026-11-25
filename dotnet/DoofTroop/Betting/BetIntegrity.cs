using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Fireball.Fireworks.Models;
using Newtonsoft.Json;

namespace DoofTroop.Betting
{
    /// <summary>
    /// Server-side bet payload validation and lock hash helpers.
    /// </summary>
    public static class BetIntegrity
    {
        public static bool IsAllowedTargetType(string type) =>
            !string.IsNullOrEmpty(type)
            && Constants.BetTargetTypes.All.Any(t =>
                string.Equals(t, type, StringComparison.Ordinal));

        public static bool IsValidComboPick(ComboPick pick)
        {
            if (pick == null) return true;
            if (string.IsNullOrEmpty(pick.Kind) || string.IsNullOrEmpty(pick.Key)) return false;

            if (!Constants.ComboKinds.All.Any(k =>
                    string.Equals(k, pick.Kind, StringComparison.Ordinal)))
            {
                return false;
            }

            return pick.Kind switch
            {
                Constants.ComboKinds.Colors =>
                    Constants.DoofColors.Contains(pick.Key),
                Constants.ComboKinds.Patterns =>
                    Constants.DoofPatterns.Contains(pick.Key),
                Constants.ComboKinds.Accessories =>
                    Constants.DoofAccessories.Contains(pick.Key),
                _ => false
            };
        }

        public static bool IsValidCrazyComboPicks(Dictionary<string, CrazyComboSlot> picks)
        {
            if (picks == null) return true;

            foreach (var place in Constants.Places.All)
            {
                if (!picks.TryGetValue(place, out var slot) || slot == null)
                {
                    continue;
                }

                if (!Constants.DoofColors.Contains(slot.Color)
                    || !Constants.DoofPatterns.Contains(slot.Pattern))
                {
                    return false;
                }
            }

            return true;
        }

        public static string ValidatePendingBoard(GameState state, BaseMessage message)
        {
            if (state.ComboPick != null && !IsValidComboPick(state.ComboPick))
            {
                return "Invalid combo pick.";
            }

            if (!IsValidCrazyComboPicks(state.CrazyComboPicks))
            {
                return "Invalid crazy combo picks.";
            }

            foreach (var bet in state.PendingBets ?? Enumerable.Empty<PlayerBet>())
            {
                if (bet?.Target == null || !IsAllowedTargetType(bet.Target.Type))
                {
                    return "Invalid bet target.";
                }

                if (bet.Target.Type == Constants.BetTargetTypes.Combo && state.ComboPick == null)
                {
                    return "Combo pick is required before placing a combo bet.";
                }

                if (bet.Target.Type == Constants.BetTargetTypes.CrazyCombo
                    && !BetMath.IsCrazyComboComplete(state.CrazyComboPicks))
                {
                    return "Crazy combo picks are incomplete.";
                }

                foreach (var chip in bet.Chips ?? Enumerable.Empty<ChipFace>())
                {
                    if (chip == null || !BetMode.IsAllowedChipValue(message, chip.Value))
                    {
                        return "Invalid chip value.";
                    }
                }

                if (BetMath.StackTotal(bet.Chips) > BetMode.MaxBetPerTarget(message))
                {
                    return $"Max bet per target is {BetMode.MaxBetPerTarget(message)}.";
                }
            }

            return null;
        }

        public static string ComputeBetsHash(IEnumerable<PlayerBet> bets)
        {
            var payload = JsonConvert.SerializeObject(bets ?? Enumerable.Empty<PlayerBet>());
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(bytes);
        }
    }
}
