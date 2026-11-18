using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DoofTroop.Betting;
using DoofTroop.Cancel;
using DoofTroop.Messages;
using DoofTroop.Supabase;
using Fireball.Fireworks;
using Fireball.Fireworks.Core;
using Fireball.Fireworks.IntegrationModule;
using Fireball.Fireworks.Models;
using Microsoft.Extensions.Logging;

namespace DoofTroop.Lock
{
    public interface ILockHandler
    {
        Task<MessageResult> LockBets(LockBetsRequest message);
        Task<MessageResult> EnsureLockedAsync(BaseMessage message, string roundId);
        Task<MessageResult> OnBetPlaced(IntegrationBetPlaced message);
        Task<MessageResult> OnBetRejected(ErrorMessage message);
    }

    public class LockHandler : ILockHandler
    {
        private readonly IFireballLogger _logger;
        private readonly IFireworks _fireworks;
        private readonly ISupabaseRoundsClient _rounds;
        private readonly ICancelHandler _cancel;

        public LockHandler(
            ILogger<LockHandler> logger,
            IFireworks fireworks,
            ISupabaseRoundsClient rounds,
            ICancelHandler cancel)
        {
            _logger = new FireballLogger(nameof(LockHandler), logger);
            _fireworks = fireworks;
            _rounds = rounds;
            _cancel = cancel;
        }

        public async Task<MessageResult> LockBets(LockBetsRequest message)
        {
            return await EnsureLockedAsync(message, message.RoundId);
        }

        public async Task<MessageResult> EnsureLockedAsync(BaseMessage message, string roundId)
        {
            if (string.IsNullOrWhiteSpace(roundId))
            {
                return await Reject(message, "roundId is required.");
            }

            var state = await _fireworks.GetGameState<GameState>(message.GameSession) ?? new GameState();

            if (state.CurrentRoundId == roundId
                && state.WalletPhase == Constants.WalletPhase.Locked
                && state.LockedBets?.Count > 0)
            {
                var lockedRound = await _rounds.GetRoundByIdAsync(roundId);
                if (lockedRound != null && lockedRound.IsCancelled())
                {
                    _logger.Log($"Lock on CANCELLED locked round={roundId}; routing to cancel.");
                    return await _cancel.CancelRoundFrom(message, roundId);
                }

                return await _fireworks.SendMessageToClient(new BetsLockedResponse(message, state));
            }

            if (state.CurrentRoundId == roundId && state.WalletPhase == Constants.WalletPhase.Settled)
            {
                return MessageResult.SuccessResult("Already settled for round.", message.ActionId);
            }

            if (state.CurrentRoundId == roundId && state.WalletPhase == Constants.WalletPhase.Rejected)
            {
                return await Reject(message, "Bets were rejected for this round.");
            }

            if (state.CurrentRoundId == roundId && state.WalletPhase == Constants.WalletPhase.Locking)
            {
                if (state.LastActionId == message.ActionId)
                {
                    return MessageResult.SuccessResult("Lock already in progress.", message.ActionId);
                }

                return await Reject(message, "Lock already in progress for another action.");
            }

            var round = await _rounds.GetRoundByIdAsync(roundId);
            if (round == null)
            {
                return await Reject(message, "Round not found.");
            }

            if (round.IsCancelled())
            {
                _logger.Log($"Lock on CANCELLED round={roundId}; routing to cancel.");
                return await _cancel.CancelRoundFrom(message, roundId);
            }

            if (round.IsBettingOpen())
            {
                return await Reject(message, "Cannot lock while betting is still open.");
            }

            if (!round.IsBettingClosedOrLater())
            {
                return await Reject(message, $"Cannot lock in round status {round.Status}.");
            }

            if (state.CurrentRoundId != roundId)
            {
                return await Reject(message, "No pending bets for this round on the server.");
            }

            var pending = state.PendingBets ?? new List<PlayerBet>();
            if (pending.Count == 0)
            {
                state.WalletPhase = Constants.WalletPhase.Idle;
                await _fireworks.SaveGameState(message.GameSession, state);
                return await Reject(message, "No pending bets to lock.");
            }

            var totalStake = BetMath.BetsStakeTotal(pending);
            var stakeCents = BetMath.ToCents(totalStake);
            if (stakeCents <= 0)
            {
                return await Reject(message, "Total stake must be positive.");
            }

            state.CurrentRoundId = roundId;
            state.RoundStatusSnapshot = round.Status;
            state.TestMoneyOnly = round.TestMoneyOnly;
            state.WalletPhase = Constants.WalletPhase.Locking;
            state.LastActionId = message.ActionId;
            state.LockedBets = BetMath.CloneBets(pending);
            state.LockedStakeCents = stakeCents;
            state.Currency = message.Currency ?? state.Currency;

            await _fireworks.SaveGameState(message.GameSession, state);

            _logger.Log($"PlaceBet round={roundId} stakeCents={stakeCents}");
            return await _fireworks.PlaceBet(FireballConstants.BetType.SPIN, stakeCents, message);
        }

        public async Task<MessageResult> OnBetPlaced(IntegrationBetPlaced message)
        {
            _logger.Log($"bet-placed: {message.ToJson()}");
            var state = await _fireworks.GetGameState<GameState>(message.GameSession) ?? new GameState();

            if (state.WalletPhase == Constants.WalletPhase.Locked
                && state.OperatorBetId == message.OperatorBetId)
            {
                return await _fireworks.SendMessageToClient(new BetsLockedResponse(message, state));
            }

            state.WalletPhase = Constants.WalletPhase.Locked;
            state.OperatorBetId = message.OperatorBetId;
            state.FireworksRoundId = message.RoundId?.ToString() ?? state.FireworksRoundId;
            state.Balance = message.Balance;
            state.Currency = message.Currency ?? state.Currency;
            state.PendingBets = new List<PlayerBet>();
            state.UndoStack = new List<UndoSnapshot>();

            await _fireworks.SaveGameState(message.GameSession, state);
            return await _fireworks.SendMessageToClient(new BetsLockedResponse(message, state));
        }

        public async Task<MessageResult> OnBetRejected(ErrorMessage message)
        {
            _logger.LogError($"bet-place-rejected: {message.ToJson()}");
            var state = await _fireworks.GetGameState<GameState>(message.GameSession) ?? new GameState();

            state.WalletPhase = Constants.WalletPhase.Rejected;
            state.PendingBets = new List<PlayerBet>();
            state.LockedBets = new List<PlayerBet>();
            state.LockedStakeCents = 0;
            state.OperatorBetId = null;
            state.FireworksRoundId = null;
            state.UndoStack = new List<UndoSnapshot>();
            if (message.Balance.HasValue)
            {
                state.Balance = message.Balance.Value;
            }

            await _fireworks.SaveGameState(message.GameSession, state);
            return await _fireworks.SendErrorToClient(
                new ErrorMessage(
                    Constants.Messages.BetsRejected,
                    message.Code,
                    message.Reason ?? "Bet placement rejected.",
                    message));
        }

        private async Task<MessageResult> Reject(BaseMessage message, string text)
        {
            _logger.LogError(text);
            return await _fireworks.SendErrorToClient(
                new ErrorMessage(
                    Constants.Messages.BetsRejected,
                    ErrorCode.InGame,
                    text,
                    message));
        }
    }
}
