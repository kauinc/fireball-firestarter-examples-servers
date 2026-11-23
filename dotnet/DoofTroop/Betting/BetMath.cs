using System;
using System.Collections.Generic;
using System.Linq;

namespace DoofTroop.Betting
{
    public static class BetMath
    {
        public static decimal RoundMoney(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);

        public static decimal ChipAmount(ChipFace chip) =>
            chip == null ? 0m : chip.Value * (chip.Count ?? 1);

        public static decimal StackTotal(IEnumerable<ChipFace> chips) =>
            RoundMoney(chips?.Sum(ChipAmount) ?? 0m);

        public static decimal BetsStakeTotal(IEnumerable<PlayerBet> bets) =>
            RoundMoney(bets?.Sum(b => StackTotal(b.Chips)) ?? 0m);

        public static bool IsComboBarTarget(BetTarget target) =>
            target?.Type is "combo" or "crazyCombo";

        public static List<ChipFace> MergeMetalChips(IEnumerable<ChipFace> chips)
        {
            var totals = new Dictionary<string, decimal>();
            foreach (var chip in chips ?? Enumerable.Empty<ChipFace>())
            {
                if (string.IsNullOrEmpty(chip?.Metal) || !(ChipAmount(chip) > 0)) continue;
                totals[chip.Metal] = RoundMoney(totals.GetValueOrDefault(chip.Metal) + ChipAmount(chip));
            }

            return Constants.ChipStackOrder
                .Where(metal => totals.GetValueOrDefault(metal) > 0)
                .Select(metal => new ChipFace { Metal = metal, Value = totals[metal] })
                .ToList();
        }

        public static List<ChipFace> MergeComboChips(IEnumerable<ChipFace> chips)
        {
            var total = StackTotal(chips);
            return total > 0
                ? new List<ChipFace> { new() { Metal = "gold", Value = total } }
                : new List<ChipFace>();
        }

        public static List<ChipFace> MergeChipsForTarget(BetTarget target, IEnumerable<ChipFace> chips) =>
            IsComboBarTarget(target) ? MergeComboChips(chips) : MergeMetalChips(chips);

        public static string BetTargetKey(BetTarget target)
        {
            if (target == null) return "unknown";
            switch (target.Type)
            {
                case "doof":
                    return $"doof:{target.Color}:{target.Pattern}";
                case "split":
                {
                    var cells = (target.Cells ?? new List<BetCell>())
                        .Select(c => $"{c.Color}:{c.Pattern}")
                        .OrderBy(x => x);
                    return $"split:{target.Coverage}:{string.Join("|", cells)}";
                }
                case "pattern":
                    return $"pattern:{target.Pattern}";
                case "color":
                    return $"color:{target.Color}";
                case "accessory":
                    return $"accessory:{target.Accessory}";
                case "combo":
                    return "combo:regular";
                case "crazyCombo":
                    return "combo:crazy";
                default:
                    return "unknown";
            }
        }

        public static string[] PositionsForMetal(string metal) => metal switch
        {
            "silver" => new[] { Constants.Places.First, Constants.Places.Second },
            "bronze" => new[] { Constants.Places.First, Constants.Places.Second, Constants.Places.Third },
            _ => new[] { Constants.Places.First }
        };

        public static bool IsAllowedChipValue(decimal value) =>
            Constants.ChipValues.Any(v => v == value)
            || BetMode.DefaultCoinChips.Any(v => v == value);

        public static bool IsCrazyComboComplete(Dictionary<string, CrazyComboSlot> picks) =>
            picks != null && Constants.Places.All.All(p => picks.ContainsKey(p) && picks[p] != null);

        public static PlayerBet CloneBet(PlayerBet bet)
        {
            if (bet == null) return null;
            return new PlayerBet
            {
                Id = bet.Id,
                Key = bet.Key,
                Positions = bet.Positions?.ToList() ?? new List<string>(),
                Target = CloneTarget(bet.Target),
                Chips = bet.Chips?.Select(c => new ChipFace
                {
                    Metal = c.Metal,
                    Value = c.Value,
                    Count = c.Count
                }).ToList() ?? new List<ChipFace>()
            };
        }

        public static BetTarget CloneTarget(BetTarget target)
        {
            if (target == null) return null;
            return new BetTarget
            {
                Type = target.Type,
                Coverage = target.Coverage,
                Color = target.Color,
                Pattern = target.Pattern,
                Accessory = target.Accessory,
                Anchor = target.Anchor,
                Cells = target.Cells?.Select(c => new BetCell
                {
                    Color = c.Color,
                    Pattern = c.Pattern
                }).ToList()
            };
        }

        public static List<PlayerBet> CloneBets(IEnumerable<PlayerBet> bets) =>
            bets?.Select(CloneBet).ToList() ?? new List<PlayerBet>();

        public static long ToCents(decimal amount) =>
            (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

        public static decimal FromCents(long cents) => cents / 100m;
    }
}
