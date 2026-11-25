using System.Threading.Tasks;
using DoofTroop.Auth;
using DoofTroop.Betting;
using DoofTroop.Cancel;
using DoofTroop.Lock;
using DoofTroop.Messages;
using DoofTroop.Settlement;
using Fireball.Fireworks;
using Fireball.Fireworks.Core;
using Fireball.Fireworks.IntegrationModule;
using Fireball.Fireworks.Models;
using Microsoft.Extensions.Logging;

namespace DoofTroop
{
    public interface IGame
    {
        Task<MessageResult> SwitchMessage(ParseResult result);
    }

    public class Game : IGame
    {
        private readonly IFireballLogger _logger;
        private readonly IFireworks _fireworks;
        private readonly IAuth _auth;
        private readonly IBettingHandler _betting;
        private readonly ILockHandler _lockHandler;
        private readonly ISettlementHandler _settlement;
        private readonly ICancelHandler _cancel;

        public Game(
            ILogger<Game> logger,
            IFireworks fireworks,
            IAuth auth,
            IBettingHandler betting,
            ILockHandler lockHandler,
            ISettlementHandler settlement,
            ICancelHandler cancel)
        {
            _logger = new FireballLogger(nameof(Game), logger);
            _fireworks = fireworks;
            _auth = auth;
            _betting = betting;
            _lockHandler = lockHandler;
            _settlement = settlement;
            _cancel = cancel;
        }

        public async Task<MessageResult> SwitchMessage(ParseResult result)
        {
            if (!result.IsSuccess)
            {
                return MessageResult.ErrorResult("Error Parse message...");
            }

            _logger.Log($"SwitchMessage: {result.MessageName}");

            switch (result.MessageName)
            {
                case FireballConstants.MessagesNames.AUTHENTICATE:
                    return await _auth.TryAuth(result.ToMessage<AuthMessage>());
                case FireballConstants.MessagesNames.SESSION:
                    return await _auth.OnAuthSuccess(result.ToMessage<SessionMessage>());
                case FireballConstants.MessagesNames.AUTHENTICATE_REJECT:
                    return await _auth.OnAuthReject(result.ToMessage<ErrorMessage>());
                case FireballConstants.MessagesNames.DISCONNECTED:
                    return await _auth.OnDisconnected(result.ToMessage<BaseMessage>());

                case Constants.Messages.PlaceChip:
                    return await _betting.PlaceChip(result.ToMessage<PlaceChipRequest>());
                case Constants.Messages.RemoveChip:
                    return await _betting.RemoveChip(result.ToMessage<RemoveChipRequest>());
                case Constants.Messages.ClearBets:
                    return await _betting.ClearBets(result.ToMessage<ClearBetsRequest>());
                case Constants.Messages.DoubleBets:
                    return await _betting.DoubleBets(result.ToMessage<DoubleBetsRequest>());
                case Constants.Messages.RepeatLast:
                    return await _betting.RepeatLast(result.ToMessage<RepeatLastRequest>());
                case Constants.Messages.UndoBets:
                    return await _betting.UndoBets(result.ToMessage<UndoBetsRequest>());
                case Constants.Messages.SetComboPick:
                    return await _betting.SetComboPick(result.ToMessage<SetComboPickRequest>());
                case Constants.Messages.SetCrazyComboPicks:
                    return await _betting.SetCrazyComboPicks(result.ToMessage<SetCrazyComboPicksRequest>());
                case Constants.Messages.CarryForward:
                    return await _betting.CarryForward(result.ToMessage<CarryForwardRequest>());

                case Constants.Messages.LockBets:
                    return await _lockHandler.LockBets(result.ToMessage<LockBetsRequest>());
                case FireballConstants.MessagesNames.BET_PLACED:
                    return await _lockHandler.OnBetPlaced(result.ToMessage<IntegrationBetPlaced>());
                case FireballConstants.MessagesNames.BET_PLACE_REJECTED:
                    return await _lockHandler.OnBetRejected(result.ToMessage<ErrorMessage>());

                case Constants.Messages.SettleRound:
                    return await _settlement.SettleRound(result.ToMessage<SettleRoundRequest>());
                case FireballConstants.MessagesNames.WINNING_PAID:
                    return await _settlement.OnWinConfirmed(result.ToMessage<IntegrationWinningPaid>());
                case FireballConstants.MessagesNames.WINNING_PAY_REJECTED:
                    return await _settlement.OnWinRejected(result.ToMessage<ErrorMessage>());

                case Constants.Messages.CancelRound:
                    return await _cancel.CancelRound(result.ToMessage<CancelRoundRequest>());

                case FireballConstants.MessagesNames.ERROR:
                {
                    var error = result.ToMessage<ErrorMessage>();
                    _logger.LogError($"error: {error.ToJson()}");
                    return await _fireworks.SendErrorToClient(error);
                }

                default:
                    return MessageResult.ErrorResult($"Undefined message with name = {result.MessageName}");
            }
        }
    }
}
