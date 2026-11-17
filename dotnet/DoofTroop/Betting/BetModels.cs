using System.Collections.Generic;

namespace DoofTroop.Betting
{
    public class ChipFace
    {
        public string Metal;
        public decimal Value;
        public int? Count;
    }

    public class BetCell
    {
        public string Color;
        public string Pattern;
    }

    public class BetTarget
    {
        public string Type;
        public int Coverage;
        public string Color;
        public string Pattern;
        public string Accessory;
        public List<BetCell> Cells;
        public object Anchor;
    }

    public class PlayerBet
    {
        public string Id;
        public string Key;
        public List<ChipFace> Chips = new();
        public List<string> Positions = new();
        public BetTarget Target;
    }

    public class ComboPick
    {
        public string Kind;
        public string Key;
    }

    public class CrazyComboSlot
    {
        public string Color;
        public string Pattern;
    }

    public class SettlementOutcome
    {
        public bool Won;
        public decimal Stake;
        public decimal Payout;
    }

    public class SettlementResult
    {
        public bool DidWin;
        public decimal TotalWin;
        public int WinCount;
        public Dictionary<string, SettlementOutcome> ById = new();
        public List<PodiumWinner> Winners = new();
        public string RoundId;
        public int[] OfficialPlacements;
    }

    public class PodiumWinner
    {
        public string Place;
        public string Color;
        public string Pattern;
        public string Accessory;
        public int DoofIndex;
    }

    public class UndoSnapshot
    {
        public List<PlayerBet> Bets;
        public ComboPick ComboPick;
        public Dictionary<string, CrazyComboSlot> CrazyComboPicks;
    }
}
