using System;
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

        /// <summary>Launch fingerprint — set on auth, checked on wallet mutations.</summary>
        public string BoundPlayerId;
        public string BoundGameId;
        public string BoundEnvironment;
        public string BoundGameMode;
        public string BoundCurrency;

        public List<PlayerBet> PendingBets = new();
        public List<PlayerBet> LockedBets = new();
        public List<PlayerBet> LastRoundBets = new();
        public List<PlayerBet> CarryForwardBets = new();

        public ComboPick ComboPick;
        public Dictionary<string, CrazyComboSlot> CrazyComboPicks = new();

        public string WalletPhase = Constants.WalletPhase.Idle;
        public string OperatorBetId;
        /// <summary>Fireworks bet id from bet-placed (used in PayWin).</summary>
        public Guid? FireworksBetId;
        /// <summary>Fireworks round id from bet-placed (used in PayWin).</summary>
        public Guid? FireworksRoundId;
        /// <summary>RGS replay id from PlaceBet / win-paid; used by SaveGameStateForReplay.</summary>
        public string LastReplayId;
        public string LastActionId;
        public long LockedStakeCents;
        public string Currency;
        public string GameMode;
        public long Balance;

        /// <summary>Server hash of locked bets at lock time (client BetsHash ignored).</summary>
        public string LockedBetsHash;

        public long PendingWinCents;
        public string SettleActionId;
        public Guid? LastWinId;

        public SettlementResult LastSettlement;
        public List<UndoSnapshot> UndoStack = new();
        public int BetSequence;
    }
}
