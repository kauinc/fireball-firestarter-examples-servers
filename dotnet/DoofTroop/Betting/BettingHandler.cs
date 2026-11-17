using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DoofTroop.Messages;
using DoofTroop.Supabase;
using Fireball.Fireworks;
using Fireball.Fireworks.Core;
using Fireball.Fireworks.Models;
using Microsoft.Extensions.Logging;

namespace DoofTroop.Betting
{
    public interface IBettingHandler
    {
        Task<MessageResult> PlaceChip(PlaceChipRequest message);
        Task<MessageResult> RemoveChip(RemoveChipRequest message);
        Task<MessageResult> ClearBets(ClearBetsRequest message);
        Task<MessageResult> DoubleBets(DoubleBetsRequest message);
        Task<MessageResult> RepeatLast(RepeatLastRequest message);
        Task<MessageResult> UndoBets(UndoBetsRequest message);
        Task<MessageResult> SetComboPick(SetComboPickRequest message);
        Task<MessageResult> SetCrazyComboPicks(SetCrazyComboPicksRequest message);
        Task<MessageResult> CarryForward(CarryForwardRequest message);
    }

    public class BettingHandler : IBettingHandler
    {
        private readonly IFireballLogger _logger;
        private readonly IFireworks _fireworks;
        private readonly ISupabaseRoundsClient _rounds;

        public BettingHandler(
            ILogger<BettingHandler> logger,
            IFireworks fireworks,
            ISupabaseRoundsClient rounds)
        {
            _logger = new FireballLogger(nameof(BettingHandler), logger);
            _fireworks = fireworks;
            _rounds = rounds;
        }

        public async Task<MessageResult> PlaceChip(PlaceChipRequest message)
        {
            var (state, error) = await LoadEditableState(message, message.RoundId);
            if (error != null) return error;

            if (message.Target == null)
            {
                return await Reject(message, "Missing bet target.");
            }

            if (!BetMath.IsAllowedChipValue(message.ChipValue) || message.ChipValue <= 0)
            {
                return await Reject(message, "Invalid chip value.");
            }

            if (message.Target.Type == "combo" && state.ComboPick == null)
            {
                return await Reject(message, "Combo pick is required before placing a combo bet.");
            }

            if (message.Target.Type == "crazyCombo" && !BetMath.IsCrazyComboComplete(state.CrazyComboPicks))
            {
                return await Reject(message, "Crazy combo picks are incomplete.");
            }

            PushUndo(state);
            var metal = string.IsNullOrWhiteSpace(message.Metal) ? "gold" : message.Metal;
            var key = BetMath.BetTargetKey(message.Target);
            var existing = state.PendingBets.FirstOrDefault(b => b.Key == key);
            if (existing == null)
            {
                existing = new PlayerBet
                {
                    Id = NextBetId(state),
                    Key = key,
                    Target = BetMath.CloneTarget(message.Target),
                    Positions = message.Positions?.ToList()
                                ?? BetMath.PositionsForMetal(metal).ToList(),
                    Chips = new List<ChipFace>()
                };
                state.PendingBets.Add(existing);
            }

            var chips = existing.Chips.ToList();
            chips.Add(new ChipFace { Metal = metal, Value = message.ChipValue, Count = 1 });
            existing.Chips = BetMath.MergeChipsForTarget(existing.Target, chips);
            existing.Positions = BetMath.IsComboBarTarget(existing.Target)
                ? Constants.Places.All.ToList()
                : existing.Chips.SelectMany(c => BetMath.PositionsForMetal(c.Metal)).Distinct().ToList();

            if (BetMath.StackTotal(existing.Chips) > Constants.MaxBetPerTarget)
            {
                state.UndoStack.RemoveAt(state.UndoStack.Count - 1);
                return await Reject(message, $"Max bet per target is {Constants.MaxBetPerTarget}.");
            }

            MarkPending(state, message.RoundId);
            await _fireworks.SaveGameState(message.GameSession, state);
            return await _fireworks.SendMessageToClient(new BetsUpdatedResponse(message, state));
        }

        public async Task<MessageResult> RemoveChip(RemoveChipRequest message)
        {
            var (state, error) = await LoadEditableState(message, message.RoundId);
            if (error != null) return error;

            var bet = state.PendingBets.FirstOrDefault(b => b.Id == message.BetId);
            if (bet == null)
            {
                return await Reject(message, "Bet not found.");
            }

            PushUndo(state);
            if (!string.IsNullOrWhiteSpace(message.Metal))
            {
                bet.Chips = bet.Chips.Where(c => c.Metal != message.Metal).ToList();
            }
            else
            {
                bet.Chips.Clear();
            }

            if (bet.Chips.Count == 0)
            {
                state.PendingBets.Remove(bet);
            }

            MarkPending(state, message.RoundId);
            await _fireworks.SaveGameState(message.GameSession, state);
            return await _fireworks.SendMessageToClient(new BetsUpdatedResponse(message, state));
        }

        public async Task<MessageResult> ClearBets(ClearBetsRequest message)
        {
            var (state, error) = await LoadEditableState(message, message.RoundId);
            if (error != null) return error;

            PushUndo(state);
            state.PendingBets.Clear();
            state.ComboPick = null;
            state.CrazyComboPicks = EmptyCrazyPicks();
            MarkPending(state, message.RoundId);
            await _fireworks.SaveGameState(message.GameSession, state);
            return await _fireworks.SendMessageToClient(new BetsUpdatedResponse(message, state));
        }

        public async Task<MessageResult> DoubleBets(DoubleBetsRequest message)
        {
            var (state, error) = await LoadEditableState(message, message.RoundId);
            if (error != null) return error;

            PushUndo(state);
            foreach (var bet in state.PendingBets)
            {
                bet.Chips = BetMath.MergeChipsForTarget(
                    bet.Target,
                    bet.Chips.Select(c => new ChipFace
                    {
                        Metal = c.Metal,
                        Value = BetMath.RoundMoney(c.Value * 2),
                        Count = c.Count
                    }));

                if (BetMath.StackTotal(bet.Chips) > Constants.MaxBetPerTarget)
                {
                    state.UndoStack.RemoveAt(state.UndoStack.Count - 1);
                    return await Reject(message, $"Doubling would exceed max bet {Constants.MaxBetPerTarget}.");
                }
            }

            MarkPending(state, message.RoundId);
            await _fireworks.SaveGameState(message.GameSession, state);
            return await _fireworks.SendMessageToClient(new BetsUpdatedResponse(message, state));
        }

        public async Task<MessageResult> RepeatLast(RepeatLastRequest message)
        {
            var (state, error) = await LoadEditableState(message, message.RoundId);
            if (error != null) return error;

            if (state.LastRoundBets == null || state.LastRoundBets.Count == 0)
            {
                return await Reject(message, "No previous bets to repeat.");
            }

            PushUndo(state);
            state.PendingBets = state.LastRoundBets.Select(b =>
            {
                var clone = BetMath.CloneBet(b);
                clone.Id = NextBetId(state);
                return clone;
            }).ToList();

            if (state.PendingBets.Any(b => BetMath.StackTotal(b.Chips) > Constants.MaxBetPerTarget))
            {
                state.UndoStack.RemoveAt(state.UndoStack.Count - 1);
                return await Reject(message, "Repeated bets exceed max per target.");
            }

            MarkPending(state, message.RoundId);
            await _fireworks.SaveGameState(message.GameSession, state);
            return await _fireworks.SendMessageToClient(new BetsUpdatedResponse(message, state));
        }

        public async Task<MessageResult> UndoBets(UndoBetsRequest message)
        {
            var (state, error) = await LoadEditableState(message, message.RoundId);
            if (error != null) return error;

            if (state.UndoStack == null || state.UndoStack.Count == 0)
            {
                return await Reject(message, "Nothing to undo.");
            }

            var snapshot = state.UndoStack[^1];
            state.UndoStack.RemoveAt(state.UndoStack.Count - 1);
            state.PendingBets = BetMath.CloneBets(snapshot.Bets);
            state.ComboPick = snapshot.ComboPick == null
                ? null
                : new ComboPick { Kind = snapshot.ComboPick.Kind, Key = snapshot.ComboPick.Key };
            state.CrazyComboPicks = snapshot.CrazyComboPicks != null
                ? snapshot.CrazyComboPicks.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value == null
                        ? null
                        : new CrazyComboSlot { Color = kv.Value.Color, Pattern = kv.Value.Pattern })
                : EmptyCrazyPicks();

            MarkPending(state, message.RoundId);
            await _fireworks.SaveGameState(message.GameSession, state);
            return await _fireworks.SendMessageToClient(new BetsUpdatedResponse(message, state));
        }

        public async Task<MessageResult> SetComboPick(SetComboPickRequest message)
        {
            var (state, error) = await LoadEditableState(message, message.RoundId);
            if (error != null) return error;

            PushUndo(state);
            state.ComboPick = message.ComboPick;
            MarkPending(state, message.RoundId);
            await _fireworks.SaveGameState(message.GameSession, state);
            return await _fireworks.SendMessageToClient(new BetsUpdatedResponse(message, state));
        }

        public async Task<MessageResult> SetCrazyComboPicks(SetCrazyComboPicksRequest message)
        {
            var (state, error) = await LoadEditableState(message, message.RoundId);
            if (error != null) return error;

            PushUndo(state);
            state.CrazyComboPicks = message.CrazyComboPicks ?? EmptyCrazyPicks();
            MarkPending(state, message.RoundId);
            await _fireworks.SaveGameState(message.GameSession, state);
            return await _fireworks.SendMessageToClient(new BetsUpdatedResponse(message, state));
        }

        public async Task<MessageResult> CarryForward(CarryForwardRequest message)
        {
            var (state, error) = await LoadEditableState(message, message.RoundId);
            if (error != null) return error;

            PushUndo(state);
            var source = message.Bets?.Count > 0
                ? message.Bets
                : state.CarryForwardBets;

            state.PendingBets = (source ?? new List<PlayerBet>()).Select(b =>
            {
                var clone = BetMath.CloneBet(b);
                clone.Id = NextBetId(state);
                return clone;
            }).ToList();

            if (message.ComboPick != null) state.ComboPick = message.ComboPick;
            if (message.CrazyComboPicks != null) state.CrazyComboPicks = message.CrazyComboPicks;

            if (state.PendingBets.Any(b => BetMath.StackTotal(b.Chips) > Constants.MaxBetPerTarget))
            {
                state.UndoStack.RemoveAt(state.UndoStack.Count - 1);
                return await Reject(message, "Carry-forward exceeds max bet per target.");
            }

            MarkPending(state, message.RoundId);
            await _fireworks.SaveGameState(message.GameSession, state);
            return await _fireworks.SendMessageToClient(new BetsUpdatedResponse(message, state));
        }

        private async Task<(GameState state, MessageResult error)> LoadEditableState(
            BaseMessage message,
            string roundId)
        {
            if (string.IsNullOrWhiteSpace(roundId))
            {
                return (null, await Reject(message, "roundId is required."));
            }

            var round = await _rounds.GetRoundByIdAsync(roundId);
            if (round == null)
            {
                return (null, await Reject(message, "Round not found."));
            }

            if (!round.IsBettingOpen())
            {
                return (null, await Reject(message, "Betting is closed for this round."));
            }

            var state = await _fireworks.GetGameState<GameState>(message.GameSession) ?? new GameState();

            if (state.WalletPhase is Constants.WalletPhase.Locking
                or Constants.WalletPhase.Locked
                or Constants.WalletPhase.Settled)
            {
                if (state.CurrentRoundId == roundId)
                {
                    return (null, await Reject(message, $"Cannot edit bets in phase {state.WalletPhase}."));
                }
            }

            if (!string.IsNullOrEmpty(state.CurrentRoundId)
                && state.CurrentRoundId != roundId
                && state.WalletPhase is Constants.WalletPhase.Pending or Constants.WalletPhase.Idle)
            {
                // New betting round — reset board for the new id.
                state.PendingBets = new List<PlayerBet>();
                state.UndoStack = new List<UndoSnapshot>();
                state.ComboPick = null;
                state.CrazyComboPicks = EmptyCrazyPicks();
                state.WalletPhase = Constants.WalletPhase.Idle;
                state.OperatorBetId = null;
                state.FireworksRoundId = null;
                state.LockedBets = new List<PlayerBet>();
                state.LockedStakeCents = 0;
                state.LastSettlement = null;
            }

            state.CurrentRoundId = roundId;
            state.RoundStatusSnapshot = round.Status;
            state.TestMoneyOnly = round.TestMoneyOnly;
            return (state, null);
        }

        private static void MarkPending(GameState state, string roundId)
        {
            state.CurrentRoundId = roundId;
            state.WalletPhase = state.PendingBets.Count > 0
                ? Constants.WalletPhase.Pending
                : Constants.WalletPhase.Idle;
        }

        private static void PushUndo(GameState state)
        {
            state.UndoStack ??= new List<UndoSnapshot>();
            state.UndoStack.Add(new UndoSnapshot
            {
                Bets = BetMath.CloneBets(state.PendingBets),
                ComboPick = state.ComboPick == null
                    ? null
                    : new ComboPick { Kind = state.ComboPick.Kind, Key = state.ComboPick.Key },
                CrazyComboPicks = state.CrazyComboPicks?.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value == null
                        ? null
                        : new CrazyComboSlot { Color = kv.Value.Color, Pattern = kv.Value.Pattern })
            });

            while (state.UndoStack.Count > Constants.MaxUndo)
            {
                state.UndoStack.RemoveAt(0);
            }
        }

        private static string NextBetId(GameState state)
        {
            state.BetSequence += 1;
            return $"bet-{state.BetSequence}";
        }

        private static Dictionary<string, CrazyComboSlot> EmptyCrazyPicks() =>
            Constants.Places.All.ToDictionary(p => p, _ => (CrazyComboSlot)null);

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
