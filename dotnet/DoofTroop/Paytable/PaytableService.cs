using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DoofTroop.Betting;
using Newtonsoft.Json;

namespace DoofTroop.Paytable
{
    public interface IPaytableService
    {
        decimal RoundMoney(decimal value);
        decimal GetBetTotal(PlayerBet bet);
        bool IsComboBarTarget(BetTarget target);
        string[] PositionsForMetal(string metal);
        decimal PayoutMultiplier(BetTarget target, IList<string> positions, ComboPick comboPick = null, string metal = null);
        SettlementResult ResolveBetOutcomes(
            IList<PlayerBet> bets,
            IList<PodiumWinner> winners,
            ComboPick comboPick,
            Dictionary<string, CrazyComboSlot> crazyComboPicks);
        List<PodiumWinner> WinnersFromPlacements(IList<object> placements);
        bool PlacementsEqual(IList<int> claimed, IList<int> official);
    }

    public class PaytableService : IPaytableService
    {
        private readonly BalancingConfig _balancing;

        public PaytableService()
        {
            _balancing = LoadBalancing();
        }

        public decimal RoundMoney(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);

        public decimal GetBetTotal(PlayerBet bet)
        {
            if (bet?.Chips == null) return 0m;
            var total = bet.Chips.Sum(c => c.Value * (c.Count ?? 1));
            return RoundMoney(total);
        }

        public bool IsComboBarTarget(BetTarget target) =>
            target?.Type is "combo" or "crazyCombo";

        public string[] PositionsForMetal(string metal) => metal switch
        {
            "silver" => new[] { Constants.Places.First, Constants.Places.Second },
            "bronze" => new[] { Constants.Places.First, Constants.Places.Second, Constants.Places.Third },
            _ => new[] { Constants.Places.First }
        };

        public decimal PayoutMultiplier(
            BetTarget target,
            IList<string> positions,
            ComboPick comboPick = null,
            string metal = null)
        {
            if (target?.Type == null) return 1m;

            if (target.Type == "crazyCombo")
            {
                return _balancing.Special.GetValueOrDefault("crazyCombo", 1m);
            }

            if (target.Type == "combo")
            {
                var key = SpecialKeyForComboPick(comboPick);
                return _balancing.Special.GetValueOrDefault(key, _balancing.Special.GetValueOrDefault("colorCombo", 1m));
            }

            var betKey = StandardBetKey(target);
            if (betKey == null || !_balancing.Standard.TryGetValue(betKey, out var scopes))
            {
                return 1m;
            }

            var scope = metal != null ? ScopeForMetal(metal) : PositionScope(positions);
            return scopes.Get(scope);
        }

        public SettlementResult ResolveBetOutcomes(
            IList<PlayerBet> bets,
            IList<PodiumWinner> winners,
            ComboPick comboPick,
            Dictionary<string, CrazyComboSlot> crazyComboPicks)
        {
            var result = new SettlementResult();
            if (bets == null || winners == null || winners.Count == 0)
            {
                return result;
            }

            foreach (var bet in bets)
            {
                var stake = GetBetTotal(bet);
                decimal payout = 0m;
                var won = false;

                if (IsComboBarTarget(bet.Target))
                {
                    won = DoesBetWin(bet, winners, bet.Positions, comboPick, crazyComboPicks);
                    var multiplier = PayoutMultiplier(bet.Target, bet.Positions, comboPick);
                    payout = won ? RoundMoney(stake * multiplier) : 0m;
                }
                else
                {
                    foreach (var chip in bet.Chips ?? Enumerable.Empty<ChipFace>())
                    {
                        var faceStake = RoundMoney(chip.Value * (chip.Count ?? 1));
                        if (faceStake <= 0) continue;

                        var positions = PositionsForMetal(chip.Metal).ToList();
                        if (!DoesBetWin(bet, winners, positions, comboPick, crazyComboPicks))
                        {
                            continue;
                        }

                        won = true;
                        var multiplier = PayoutMultiplier(bet.Target, positions, comboPick, chip.Metal);
                        payout = RoundMoney(payout + faceStake * multiplier);
                    }
                }

                result.ById[bet.Id] = new SettlementOutcome
                {
                    Won = won,
                    Stake = stake,
                    Payout = payout
                };

                if (won)
                {
                    result.WinCount += 1;
                    result.TotalWin += payout;
                }
            }

            result.TotalWin = RoundMoney(result.TotalWin);
            result.DidWin = result.WinCount > 0;
            result.Winners = winners.ToList();
            return result;
        }

        public List<PodiumWinner> WinnersFromPlacements(IList<object> placements)
        {
            if (placements == null || placements.Count < 3)
            {
                return null;
            }

            var winners = new List<PodiumWinner>();
            for (var i = 0; i < Constants.Places.All.Length; i++)
            {
                var entry = placements[i];
                int doofIndex;
                if (entry is long l) doofIndex = (int)l;
                else if (entry is int ii) doofIndex = ii;
                else if (entry is string s && int.TryParse(s, out var parsed)) doofIndex = parsed;
                else if (entry is Newtonsoft.Json.Linq.JValue jv && int.TryParse(jv.ToString(), out var jParsed)) doofIndex = jParsed;
                else return null;

                var doof = DoofIndex.FromIndex(doofIndex);
                if (doof == null) return null;

                winners.Add(new PodiumWinner
                {
                    Place = Constants.Places.All[i],
                    Color = doof.Value.Color,
                    Pattern = doof.Value.Pattern,
                    Accessory = doof.Value.Accessory,
                    DoofIndex = doofIndex
                });
            }

            return winners;
        }

        public bool PlacementsEqual(IList<int> claimed, IList<int> official)
        {
            if (claimed == null || official == null || claimed.Count != official.Count)
            {
                return false;
            }

            for (var i = 0; i < claimed.Count; i++)
            {
                if (claimed[i] != official[i]) return false;
            }

            return true;
        }

        private bool DoesBetWin(
            PlayerBet bet,
            IList<PodiumWinner> winners,
            IList<string> positions,
            ComboPick comboPick,
            Dictionary<string, CrazyComboSlot> crazyComboPicks)
        {
            var target = bet?.Target;
            if (target == null || winners == null || winners.Count == 0) return false;

            if (target.Type == "combo")
            {
                return DoesComboWin(comboPick, winners);
            }

            if (target.Type == "crazyCombo")
            {
                return DoesCrazyComboWin(crazyComboPicks, winners);
            }

            var places = positions != null && positions.Count > 0
                ? positions
                : Constants.Places.All;

            var relevant = winners.Where(w => places.Contains(w.Place)).ToList();
            if (relevant.Count == 0) return false;

            return target.Type switch
            {
                "doof" => relevant.Any(w => w.Color == target.Color && w.Pattern == target.Pattern),
                "split" => relevant.Any(w =>
                    (target.Cells ?? new List<BetCell>()).Any(c => c.Color == w.Color && c.Pattern == w.Pattern)),
                "color" => relevant.Any(w => w.Color == target.Color),
                "pattern" => relevant.Any(w => w.Pattern == target.Pattern),
                "accessory" => relevant.Any(w =>
                    DoofIndex.AccessoryFor(w.Color, w.Pattern) == target.Accessory),
                _ => false
            };
        }

        private static bool DoesComboWin(ComboPick comboPick, IList<PodiumWinner> winners)
        {
            if (comboPick?.Kind == null || comboPick.Key == null) return false;
            if (winners.Count < Constants.Places.All.Length) return false;

            var podium = Constants.Places.All
                .Select(place => winners.FirstOrDefault(w => w.Place == place))
                .ToList();
            if (podium.Any(w => w == null)) return false;

            return comboPick.Kind switch
            {
                "colors" => podium.All(w => w.Color == comboPick.Key),
                "patterns" => podium.All(w => w.Pattern == comboPick.Key),
                "accessories" => podium.All(w =>
                    DoofIndex.AccessoryFor(w.Color, w.Pattern) == comboPick.Key),
                _ => false
            };
        }

        private static bool DoesCrazyComboWin(
            Dictionary<string, CrazyComboSlot> picks,
            IList<PodiumWinner> winners)
        {
            if (picks == null) return false;
            if (Constants.Places.All.Any(p => !picks.ContainsKey(p) || picks[p] == null))
            {
                return false;
            }

            return Constants.Places.All.All(place =>
            {
                var pick = picks[place];
                var winner = winners.FirstOrDefault(w => w.Place == place);
                return winner != null
                       && pick.Color == winner.Color
                       && pick.Pattern == winner.Pattern;
            });
        }

        private static string ScopeForMetal(string metal) => metal switch
        {
            "silver" => "top2",
            "bronze" => "top3",
            _ => "first"
        };

        private static string PositionScope(IList<string> positions)
        {
            var count = positions?.Count ?? 0;
            if (count <= 1) return "first";
            if (count == 2) return "top2";
            return "top3";
        }

        private static string StandardBetKey(BetTarget target)
        {
            if (target?.Type == null) return null;
            if (target.Type == "split")
            {
                return target.Coverage == 4 ? "split4" : "split2";
            }

            return target.Type is "accessory" or "pattern" or "color" or "doof"
                ? target.Type
                : null;
        }

        private static string SpecialKeyForComboPick(ComboPick comboPick)
        {
            if (comboPick?.Kind == null || comboPick.Key == null) return "colorCombo";
            if (comboPick.Kind == "accessories")
            {
                if (comboPick.Key == "Hats") return "hatsCombo";
                if (comboPick.Key == "Glasses") return "glassesCombo";
            }

            if (comboPick.Kind == "patterns")
            {
                if (comboPick.Key == "Solid") return "solidCombo";
                if (comboPick.Key == "Dots") return "dotsCombo";
                if (comboPick.Key == "Stripes") return "stripesCombo";
            }

            if (comboPick.Kind == "colors") return "colorCombo";
            return "colorCombo";
        }

        private static BalancingConfig LoadBalancing()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("dooftroopBalancing.json", StringComparison.OrdinalIgnoreCase));

            if (resourceName != null)
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                using var reader = new StreamReader(stream!);
                return JsonConvert.DeserializeObject<BalancingConfig>(reader.ReadToEnd())
                       ?? throw new InvalidOperationException("Failed to deserialize paytable.");
            }

            var path = Path.Combine(AppContext.BaseDirectory, "Paytable", "dooftroopBalancing.json");
            if (!File.Exists(path))
            {
                path = Path.Combine(AppContext.BaseDirectory, "dooftroopBalancing.json");
            }

            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<BalancingConfig>(json)
                   ?? throw new InvalidOperationException("Failed to deserialize paytable.");
        }
    }
}
