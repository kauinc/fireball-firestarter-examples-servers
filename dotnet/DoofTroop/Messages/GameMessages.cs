using System.Collections.Generic;
using DoofTroop.Betting;
using Fireball.Fireworks.Models;

namespace DoofTroop.Messages
{
    public class PlaceChipRequest : BaseMessage
    {
        public string RoundId;
        public BetTarget Target;
        public string Metal;
        public decimal ChipValue;
        public List<string> Positions;
    }

    public class RemoveChipRequest : BaseMessage
    {
        public string RoundId;
        public string BetId;
        public string Metal;
    }

    public class ClearBetsRequest : BaseMessage
    {
        public string RoundId;
    }

    public class DoubleBetsRequest : BaseMessage
    {
        public string RoundId;
    }

    public class RepeatLastRequest : BaseMessage
    {
        public string RoundId;
    }

    public class UndoBetsRequest : BaseMessage
    {
        public string RoundId;
    }

    public class SetComboPickRequest : BaseMessage
    {
        public string RoundId;
        public ComboPick ComboPick;
    }

    public class SetCrazyComboPicksRequest : BaseMessage
    {
        public string RoundId;
        public Dictionary<string, CrazyComboSlot> CrazyComboPicks;
    }

    public class CarryForwardRequest : BaseMessage
    {
        public string RoundId;
        public List<PlayerBet> Bets;
        public ComboPick ComboPick;
        public Dictionary<string, CrazyComboSlot> CrazyComboPicks;
    }

    public class LockBetsRequest : BaseMessage
    {
        public string RoundId;
    }

    public class SettleRoundRequest : BaseMessage
    {
        public string RoundId;
        public List<int> ClaimedPlacements;
        public string BetsHash;
    }

    public class CancelRoundRequest : BaseMessage
    {
        public string RoundId;
    }

    public class BetsUpdatedResponse : BaseMessage
    {
        public List<PlayerBet> Bets;
        public ComboPick ComboPick;
        public Dictionary<string, CrazyComboSlot> CrazyComboPicks;
        public decimal TotalStake;
        public string RoundId;
        public string WalletPhase;

        public BetsUpdatedResponse(BaseMessage source, GameState state)
        {
            CopyBaseParams(source);
            Name = Constants.Messages.BetsUpdated;
            RoundId = state.CurrentRoundId;
            Bets = state.PendingBets;
            ComboPick = state.ComboPick;
            CrazyComboPicks = state.CrazyComboPicks;
            TotalStake = BetMath.BetsStakeTotal(state.PendingBets);
            WalletPhase = state.WalletPhase;
        }
    }

    public class BetsLockedResponse : BaseMessage
    {
        public string RoundId;
        public List<PlayerBet> Bets;
        public decimal TotalStake;
        public string OperatorBetId;
        public string WalletPhase;
        /// <summary>Absolute wallet balance in cents (Fruits/Ares pattern).</summary>
        public long Balance;

        public BetsLockedResponse(BaseMessage source, GameState state)
        {
            CopyBaseParams(source);
            Name = Constants.Messages.BetsLocked;
            RoundId = state.CurrentRoundId;
            Bets = state.LockedBets;
            TotalStake = BetMode.FromRgsAmount(
                BetMode.IsCoins(state.GameMode, state.Currency),
                state.LockedStakeCents);
            OperatorBetId = state.OperatorBetId;
            WalletPhase = state.WalletPhase;
            Balance = state.Balance;
        }
    }

    public class SettleResultResponse : BaseMessage
    {
        public SettlementResult Settlement;
        public string WalletPhase;
        /// <summary>Absolute wallet balance in cents after applying the win.</summary>
        public long Balance;

        public SettleResultResponse(
            BaseMessage source,
            SettlementResult settlement,
            string walletPhase,
            long balance)
        {
            CopyBaseParams(source);
            Name = Constants.Messages.SettleResult;
            Settlement = settlement;
            WalletPhase = walletPhase;
            Balance = balance;
        }
    }

    public class RefundResultResponse : BaseMessage
    {
        public string RoundId;
        public string WalletPhase;
        public decimal RefundedStake;
        /// <summary>Absolute wallet balance in cents after refund.</summary>
        public long Balance;

        public RefundResultResponse(BaseMessage source, GameState state, decimal refundedStake)
        {
            CopyBaseParams(source);
            Name = Constants.Messages.RefundResult;
            RoundId = state.CurrentRoundId;
            WalletPhase = state.WalletPhase;
            RefundedStake = refundedStake;
            Balance = state.Balance;
        }
    }
}
