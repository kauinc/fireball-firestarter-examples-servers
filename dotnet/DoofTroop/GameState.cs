using System.Collections.Generic;
using DoofTroop.Betting;

namespace DoofTroop
{
    public class GameState
    {
        public bool IsGameOver;

        public string CurrentRoundId;
        public string RoundStatusSnapshot;
        public bool? TestMoneyOnly;

        public List<PlayerBet> PendingBets = new();
        public List<PlayerBet> LockedBets = new();
        public List<PlayerBet> LastRoundBets = new();
        public List<PlayerBet> CarryForwardBets = new();

        public ComboPick ComboPick;
        public Dictionary<string, CrazyComboSlot> CrazyComboPicks = new();

        public string WalletPhase = Constants.WalletPhase.Idle;
        public string OperatorBetId;
        public string FireworksRoundId;
        /// <summary>RGS replay id from PlaceBet / win-paid; used by SaveGameStateForReplay.</summary>
        public string LastReplayId;
        public string LastActionId;
        public long LockedStakeCents;
        public string Currency;
        public string GameMode;
        public long Balance;

        public SettlementResult LastSettlement;
        public List<UndoSnapshot> UndoStack = new();
        public int BetSequence;
    }
}
