using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DoofTroop.Betting;
using DoofTroop.Cancel;
using DoofTroop.Lock;
using DoofTroop.Messages;
using DoofTroop.Paytable;
using DoofTroop.Supabase;
using Fireball.Fireworks;
using Fireball.Fireworks.Core;
using Fireball.Fireworks.IntegrationModule;
using Fireball.Fireworks.Models;
using Microsoft.Extensions.Logging;

namespace DoofTroop.Settlement
{
    public interface ISettlementHandler
    {
        Task<MessageResult> SettleRound(SettleRoundRequest message);
        Task<MessageResult> OnWinConfirmed(IntegrationWinningPaid message);
        Task<MessageResult> OnWinRejected(ErrorMessage message);
    }

    public class SettlementHandler : ISettlementHandler
    {
        private readonly IFireballLogger _logger;
        private readonly IFireworks _fireworks;
        private readonly ISupabaseRoundsClient _rounds;
        private readonly IPaytableService _paytable;
        private readonly ILockHandler _lockHandler;
        private readonly ICancelHandler _cancel;

        public SettlementHandler(
            ILogger<SettlementHandler> logger,
            IFireworks fireworks,
            ISupabaseRoundsClient rounds,
            IPaytableService paytable,
            ILockHandler lockHandler,
            ICancelHandler cancel)
        {
            _logger = new FireballLogger(nameof(SettlementHandler), logger);
            _fireworks = fireworks;
            _rounds = rounds;
            _paytable = paytable;
            _lockHandler = lockHandler;
            _cancel = cancel;
        }

        public async Task<MessageResult> SettleRound(SettleRoundRequest message)
        {
            if (string.IsNullOrWhiteSpace(message.RoundId))
            {
                return await Reject(message, "roundId is required.");
            }

            var state = await _fireworks.GetGameState<GameState>(message.GameSession) ?? new GameState();

            if (state.CurrentRoundId == message.RoundId
                && state.WalletPhase == Constants.WalletPhase.Settled
                && state.LastSettlement != null)
            {
                _logger.Log($"Idempotent settle replay for round {message.RoundId}");
                return await _fireworks.SendMessageToClient(
                    new SettleResultResponse(
                        message,
                        state.LastSettlement,
                        state.WalletPhase,
                        state.Balance));
            }

            if (state.WalletPhase == Constants.WalletPhase.Rejected
                && state.CurrentRoundId == message.RoundId)
            {
                return await Reject(message, "No locked bets — place-bet was rejected.");
            }

            // Early cancel dual-trigger: never settle a cancelled round.
            var earlyRound = await _rounds.GetRoundByIdAsync(message.RoundId);
            if (earlyRound != null && earlyRound.IsCancelled())
            {
                _logger.Log($"Settle on CANCELLED round={message.RoundId}; routing to cancel.");
                return await _cancel.CancelRoundFrom(message, message.RoundId);
            }

            // Dual-trigger: lock if not yet locked.
            if (state.CurrentRoundId == message.RoundId
                && state.WalletPhase is Constants.WalletPhase.Pending or Constants.WalletPhase.Idle
                && (state.PendingBets?.Count ?? 0) > 0)
            {
                var lockResult = await _lockHandler.EnsureLockedAsync(message, message.RoundId);
                if (lockResult != null && lockResult.IsError())
                {
                    return lockResult;
                }

                // PlaceBet is async via callback; settle must wait for Locked.
                state = await _fireworks.GetGameState<GameState>(message.GameSession) ?? state;
                if (state.WalletPhase != Constants.WalletPhase.Locked)
                {
                    return MessageResult.SuccessResult(
                        "Lock initiated; retry settle after bets-locked.",
                        message.ActionId);
                }
            }

            state = await _fireworks.GetGameState<GameState>(message.GameSession) ?? state;

            if (state.CurrentRoundId != message.RoundId
                || state.WalletPhase != Constants.WalletPhase.Locked
                || state.LockedBets == null
                || state.LockedBets.Count == 0)
            {
                return await Reject(message, "No locked bets for this round.");
            }

            var round = await _rounds.GetRoundByIdAsync(message.RoundId);
            if (round == null)
            {
                return await Reject(message, "Round not found in Supabase.");
            }

            if (round.IsCancelled())
            {
                _logger.Log($"Settle on CANCELLED round={message.RoundId}; routing to cancel.");
                return await _cancel.CancelRoundFrom(message, message.RoundId);
            }

            if (!round.HasPodiumSignal())
            {
                return await Reject(message, "Podium has not started yet.");
            }

            var official = round.PlacementIndexes();
            if (official == null || official.Count < 3)
            {
                return await Reject(message, "Official placements are missing or invalid.");
            }

            if (message.ClaimedPlacements != null && message.ClaimedPlacements.Count > 0)
            {
                if (!_paytable.PlacementsEqual(message.ClaimedPlacements, official))
                {
                    _logger.LogError(
                        $"Placement mismatch round={message.RoundId} claimed=[{string.Join(",", message.ClaimedPlacements)}] official=[{string.Join(",", official)}]");
                    return await Reject(message, "Claimed placements do not match Supabase.");
                }
            }

            var winners = _paytable.WinnersFromPlacements(official.Cast<object>().ToList());
            if (winners == null)
            {
                return await Reject(message, "Unable to decode podium winners.");
            }

            var settlement = _paytable.ResolveBetOutcomes(
                state.LockedBets,
                winners,
                state.ComboPick,
                state.CrazyComboPicks);
            settlement.RoundId = message.RoundId;
            settlement.OfficialPlacements = official.ToArray();

            var winRgs = BetMode.ToRgsAmount(message, settlement.TotalWin);
            var (cappedWin, wasCapped) = BetMode.ApplyExposureCap(message, winRgs);
            if (wasCapped)
            {
                _logger.LogWarning(
                    $"MaxExposure capped round={message.RoundId} winRgs={winRgs} -> {cappedWin} currency={message.Currency}");
                winRgs = cappedWin;
                settlement.TotalWin = BetMode.FromRgsAmount(message, winRgs);
            }

            state.LastSettlement = settlement;
            state.LastActionId = message.ActionId;
            state.RoundStatusSnapshot = round.Status;
            state.TestMoneyOnly = round.TestMoneyOnly;
            state.GameMode = message.GameMode ?? state.GameMode;
            // Mark settled after PayWin request is accepted path — store intent first.
            await _fireworks.SaveGameState(message.GameSession, state);

            _logger.Log($"PayWin round={message.RoundId} winRgs={winRgs} mode={message.GameMode}");
            await _fireworks.PayWin(
                FireballConstants.WinningType.SPIN,
                state.OperatorBetId,
                winRgs,
                message);

            state.WalletPhase = Constants.WalletPhase.Settled;
            state.LastRoundBets = BetMath.CloneBets(state.LockedBets);
            state.CarryForwardBets = state.LockedBets
                .Where(b => settlement.ById.TryGetValue(b.Id, out var o) && o.Won)
                .Select(BetMath.CloneBet)
                .ToList();
            state.Balance += winRgs;

            await _fireworks.SaveGameState(message.GameSession, state);
            return await _fireworks.SendMessageToClient(
                new SettleResultResponse(
                    message,
                    settlement,
                    state.WalletPhase,
                    state.Balance));
        }

        public async Task<MessageResult> OnWinConfirmed(IntegrationWinningPaid message)
        {
            _logger.Log($"winning-paid: {message.ToJson()}");
            var state = await _fireworks.GetGameState<GameState>(message.GameSession) ?? new GameState();
            state.Balance = message.Balance;
            await _fireworks.SaveGameState(message.GameSession, state);
            return MessageResult.SuccessResult("Win confirmed", message.ActionId);
        }

        public async Task<MessageResult> OnWinRejected(ErrorMessage message)
        {
            _logger.LogError($"winning-pay-rejected: {message.ToJson()}");
            return MessageResult.ErrorResult("Win pay rejected", message.ActionId);
        }

        private async Task<MessageResult> Reject(BaseMessage message, string text)
        {
            _logger.LogError(text);
            return await _fireworks.SendErrorToClient(
                new ErrorMessage(
                    Constants.Messages.SettleResult,
                    ErrorCode.InGame,
                    text,
                    message));
        }
    }
}
