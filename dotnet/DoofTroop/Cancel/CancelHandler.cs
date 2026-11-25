using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DoofTroop.Auth;
using DoofTroop.Betting;
using DoofTroop.Messages;
using DoofTroop.Supabase;
using Fireball.Fireworks;
using Fireball.Fireworks.Core;
using Fireball.Fireworks.Models;
using Microsoft.Extensions.Logging;

namespace DoofTroop.Cancel
{
    public interface ICancelHandler
    {
        Task<MessageResult> CancelRound(CancelRoundRequest message);
        /// <summary>
        /// Build a cancel request from another game message (settle/lock dual-trigger).
        /// </summary>
        Task<MessageResult> CancelRoundFrom(BaseMessage source, string roundId);
    }

    public class CancelHandler : ICancelHandler
    {
        private readonly IFireballLogger _logger;
        private readonly IFireworks _fireworks;
        private readonly ISupabaseRoundsClient _rounds;

        public CancelHandler(
            ILogger<CancelHandler> logger,
            IFireworks fireworks,
            ISupabaseRoundsClient rounds)
        {
            _logger = new FireballLogger(nameof(CancelHandler), logger);
            _fireworks = fireworks;
            _rounds = rounds;
        }

        public Task<MessageResult> CancelRoundFrom(BaseMessage source, string roundId)
        {
            var request = new CancelRoundRequest();
            request.CopyBaseParams(source);
            request.Name = Constants.Messages.CancelRound;
            request.RoundId = roundId;
            return CancelRound(request);
        }

        public async Task<MessageResult> CancelRound(CancelRoundRequest message)
        {
            if (string.IsNullOrWhiteSpace(message.RoundId))
            {
                return await Reject(message, "roundId is required.");
            }

            var round = await _rounds.GetRoundByIdAsync(message.RoundId);
            if (round == null)
            {
                return await Reject(message, "Round not found.");
            }

            var channelError = RoundAccess.ValidateChannel(round);
            if (channelError != null)
            {
                return await Reject(message, channelError);
            }

            if (!round.IsCancelled())
            {
                return await Reject(message, "Supabase round is not CANCELLED.");
            }

            var state = await _fireworks.GetGameState<GameState>(message.GameSession) ?? new GameState();
            var bindError = SessionGuard.ValidateAndApply(state, message);
            if (bindError != null)
            {
                return await Reject(message, bindError);
            }

            if (state.CurrentRoundId == message.RoundId
                && state.WalletPhase == Constants.WalletPhase.Refunded)
            {
                return await _fireworks.SendMessageToClient(
                    new RefundResultResponse(
                        message,
                        state,
                        BetMode.FromRgsAmount(message, state.LockedStakeCents)));
            }

            if (state.CurrentRoundId == message.RoundId
                && state.WalletPhase == Constants.WalletPhase.Refunding)
            {
                if (!string.IsNullOrEmpty(state.SettleActionId)
                    && state.SettleActionId != message.ActionId)
                {
                    return await Reject(message, "Refund already in progress.");
                }

                return await CompleteRefundingAsync(message, state, round);
            }

            if (state.CurrentRoundId == message.RoundId
                && state.WalletPhase == Constants.WalletPhase.Settled)
            {
                return await Reject(message, "Round already settled; cannot refund.");
            }

            if (state.CurrentRoundId == message.RoundId
                && state.WalletPhase == Constants.WalletPhase.Paying)
            {
                return await Reject(message, "Settlement in progress; cannot refund.");
            }

            // Dual-trigger: if pending bets exist and betting closed via cancel path before lock.
            if (state.CurrentRoundId == message.RoundId
                && state.WalletPhase is Constants.WalletPhase.Pending
                && (state.PendingBets?.Count ?? 0) > 0)
            {
                // Never PlaceBet on a cancelled round — drop pending instead.
                state.PendingBets = new List<PlayerBet>();
                state.UndoStack = new List<UndoSnapshot>();
                state.WalletPhase = Constants.WalletPhase.Idle;
                state.LockedStakeCents = 0;
                await _fireworks.SaveGameState(message.GameSession, state);
                return await _fireworks.SendMessageToClient(
                    new RefundResultResponse(message, state, 0m));
            }

            if (state.CurrentRoundId != message.RoundId
                || state.WalletPhase is Constants.WalletPhase.Idle or Constants.WalletPhase.Rejected)
            {
                state.PendingBets = new List<PlayerBet>();
                state.LockedBets = new List<PlayerBet>();
                state.WalletPhase = Constants.WalletPhase.Idle;
                await _fireworks.SaveGameState(message.GameSession, state);
                return await _fireworks.SendMessageToClient(
                    new RefundResultResponse(message, state, 0m));
            }

            if (state.WalletPhase == Constants.WalletPhase.Locking)
            {
                return await Reject(message, "Lock in progress; retry cancel after bets-locked or bets-rejected.");
            }

            if (state.WalletPhase != Constants.WalletPhase.Locked
                || string.IsNullOrEmpty(state.OperatorBetId))
            {
                state.PendingBets = new List<PlayerBet>();
                state.LockedBets = new List<PlayerBet>();
                state.WalletPhase = Constants.WalletPhase.Idle;
                state.LockedStakeCents = 0;
                await _fireworks.SaveGameState(message.GameSession, state);
                return await _fireworks.SendMessageToClient(
                    new RefundResultResponse(message, state, 0m));
            }

            // Claim Refunding BEFORE PayWin.
            state.SettleActionId = message.ActionId;
            state.PendingWinCents = state.LockedStakeCents;
            state.LastWinId ??= Guid.NewGuid();
            state.WalletPhase = Constants.WalletPhase.Refunding;
            state.LastActionId = message.ActionId;
            state.RoundStatusSnapshot = round.Status;
            await _fireworks.SaveGameState(message.GameSession, state);

            state = await _fireworks.GetGameState<GameState>(message.GameSession) ?? state;
            if (state.WalletPhase != Constants.WalletPhase.Refunding
                || state.SettleActionId != message.ActionId)
            {
                return await Reject(message, "Lost refund race; refund already claimed.");
            }

            return await CompleteRefundingAsync(message, state, round);
        }

        private async Task<MessageResult> CompleteRefundingAsync(
            CancelRoundRequest message,
            GameState state,
            RoundRow round)
        {
            var refundCents = state.PendingWinCents > 0 ? state.PendingWinCents : state.LockedStakeCents;
            state.LastWinId ??= Guid.NewGuid();

            _logger.Log(
                $"Refunding cancelled round={message.RoundId} operatorBetId={state.OperatorBetId} cents={refundCents} winId={state.LastWinId}");

            // Fireworks game CFs do not expose a dedicated Refund helper in common samples.
            // Returning the locked stake via PayWin restores the player balance through the
            // standard winning integration path. Prefer a native bet-refund API when available.
            var win = await _fireworks.PayWin(
                FireballConstants.WinningType.SPIN,
                state.OperatorBetId,
                refundCents,
                message,
                betId: state.FireworksBetId,
                roundId: state.FireworksRoundId,
                roundClosed: true,
                winId: state.LastWinId);

            if (win?.WinId != null)
            {
                state.LastWinId = win.WinId;
            }

            state.WalletPhase = Constants.WalletPhase.Refunded;
            state.PendingWinCents = 0;
            state.Balance += refundCents;
            // Keep board in LastRoundBets so RGS replay still shows the cancelled stake.
            state.LastRoundBets = BetMath.CloneBets(state.LockedBets);
            state.PendingBets = new List<PlayerBet>();
            state.LockedBets = new List<PlayerBet>();
            state.LastActionId = message.ActionId;
            state.RoundStatusSnapshot = round.Status;

            await ReplayPersistence.SaveAsync(
                _fireworks,
                message.GameSession,
                state,
                message.ReplayId);
            return await _fireworks.SendMessageToClient(
                new RefundResultResponse(
                    message,
                    state,
                    BetMode.FromRgsAmount(message, refundCents)));
        }

        private async Task<MessageResult> Reject(BaseMessage message, string text)
        {
            _logger.LogError(text);
            return await _fireworks.SendErrorToClient(
                new ErrorMessage(
                    Constants.Messages.RefundResult,
                    ErrorCode.InGame,
                    text,
                    message));
        }
    }
}
