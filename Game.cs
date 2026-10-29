using System;
using System.Linq;
using System.Threading.Tasks;
using Fireball.Fireworks;
using Fireball.Fireworks.Core;
using Fireball.Fireworks.IntegrationModule;
using Fireball.Fireworks.Models;
using Fireball.Fireworks.Rng;
using Microsoft.Extensions.Logging;

namespace SlotsSample
{
    public interface IGame
    {
        Task<MessageResult> HandleMessage(ParseResult result);
    }

    public class Game : IGame
    {
        private readonly IFireballLogger _logger;
        private readonly IFireworks _fireworks;
        private readonly IRng _fireballRng;

        public Game(ILogger<Game> logger, IFireworks fireworks, IRng fireballRng)
        {
            _logger = new FireballLogger(nameof(Game), logger);
            _fireworks = fireworks;
            _fireballRng = fireballRng;
        }

        public async Task<MessageResult> HandleMessage(ParseResult result)
        {
            if (result.IsSuccess)
            {
                switch (result.MessageName)
                {
                    case FireballConstants.MessagesNames.AUTHENTICATE:
                        return await Auth(result.ToMessage<AuthMessage>());
                    case FireballConstants.MessagesNames.SESSION:
                        return await OnAuthSuccess(result.ToMessage<SessionMessage>());
                    case FireballConstants.MessagesNames.AUTHENTICATE_REJECT:
                        return await OnAuthReject(result.ToMessage<ErrorMessage>());
                    
                    case GameMessages.SPIN_REQUEST:
                        return await MakeSpin(result.ToMessage<SpinRequest>());
                    case FireballConstants.MessagesNames.BET_PLACED:
                        return await CalculateWinning(result.ToMessage<IntegrationBetPlaced>());
                    case FireballConstants.MessagesNames.WINNING_PAID:
                        return await ConfirmWinning(result.ToMessage<IntegrationWinningPaid>());
                    
                    
                    case FireballConstants.MessagesNames.BET_PLACE_REJECTED:
                        return await RejectSpin(result.ToMessage<ErrorMessage>());
                    case FireballConstants.MessagesNames.WINNING_PAY_REJECTED:
                        return await RejectWinning(result.ToMessage<ErrorMessage>());
                    
                    default:
                        return MessageResult.ErrorResult($"Undefined message with name = {result.MessageName}");
                }
            }

            return MessageResult.ErrorResult("Error Parse message...");
        }
        
        private async Task<MessageResult> Auth(AuthMessage message)
        {
            _logger.Log($"authenticate: {message.ToJson()}");

            // send the authenticating message
            return await _fireworks.Authenticate(message);
        }
        
        private async Task<MessageResult> OnAuthSuccess(SessionMessage message)
        {
            _logger.Log($"session: {message.ToJson()}");
            if (message.GameSession != null)
            {
                var gameSessionsList = await _fireworks.GetAllGameSessions(message);

                foreach (var gameSession in gameSessionsList)
                {
                    // get game state from the game session
                    var gameState = gameSession.ParseGameState<SlotsGameState>();

                    // close game session with game-over game state
                    if (gameState.IsGameOver)
                    {
                        bool closed = await _fireworks.CloseGameSession(gameSession.Id);
                    }
                }
            }
            
            if (message.GameSession == null)
            {
                // create a new default custom game state
                var defaultGameState = new SlotsGameState();
                defaultGameState.BetAmount = SlotsConst.DefaultBet;
                defaultGameState.Balance = message.Balance;
                defaultGameState.Currency = message.Currency;

                // create a new permanent game session
                var gameSession = await _fireworks.CreatePermanentGameSession(defaultGameState, message);

                // update session message with new game session data
                message.UpdateGameSession(gameSession);
            }
            
            return await _fireworks.SendSessionToClient(message);
        }
        
        private async Task<MessageResult> OnAuthReject(ErrorMessage error)
        {
            _logger.LogError($"authentication rejected: {error.ToJson()}");
            return await _fireworks.SendErrorToClient(error, ErrorCode.Authentication);
        }
        
        private async Task<MessageResult> MakeSpin(SpinRequest message)
        {
            _logger.Log($"spin: request = {message.ToJson()}");
            
            // generate random result
            var symbols = await GetRandomResult(SlotsConst.ReelsCount);
            _logger.Log($"spin: symbols = [{string.Join(",", symbols)}]");
            
            // updating game state
            var gameState = await _fireworks.GetGameState<SlotsGameState>(message.GameSession);
            
            gameState.IsSpinning = true;
            gameState.BetAmount = message.BetAmount;
            gameState.Symbols = symbols;

            await _fireworks.SaveGameState(message.GameSession, gameState);

            // send a bet place message to integration
            _logger.Log($"spin: betAmount = {message.BetAmount} {message.Currency}");
            return await _fireworks.PlaceBet(FireballConstants.BetType.SPIN, gameState.BetAmount, message);
        }
        
        private async Task<MessageResult> CalculateWinning(IntegrationBetPlaced message)
        {
            _logger.Log($"spin: betPlaced = {message.ToJson()}");
            var gameState = await _fireworks.GetGameState<SlotsGameState>(message.GameSession);
            
            // calculate winning
            var multiplier = CalculateMultiplier(gameState.Symbols);
            var winAmount = (long)(gameState.BetAmount * multiplier);
            _logger.Log($"spin: multiplier = {multiplier}");

            // updating game state
            gameState.IsSpinning = false;
            gameState.WinAmount = winAmount;
            gameState.Balance = message.Balance + winAmount;
            
            await _fireworks.SaveGameState(message.GameSession, gameState);
            
            // send a pay win message to integration
            _logger.Log($"spin: winAmount = {winAmount} {message.Currency}");
            await _fireworks.PayWin(FireballConstants.BetType.SPIN, message.OperatorBetId, winAmount, message);

            // send the spin result message to the game client
            var response = new SpinResult(gameState, message);
            return await _fireworks.SendMessageToClient(response);
        }

        private async Task<MessageResult> RejectSpin(ErrorMessage message)
        {
            _logger.LogError($"spin rejected: {message.ToJson()}");
            
            // reset game state
            var gameState = await _fireworks.GetGameState<SlotsGameState>(message.GameSession);
            
            gameState.IsSpinning = false;
            gameState.WinAmount = 0;
            gameState.Symbols = null;
            
            await _fireworks.SaveGameState(message.GameSession, gameState);
            
            // send the error message to the client
            return await _fireworks.SendErrorToClient(message);
        }
        
        private async Task<MessageResult> ConfirmWinning(IntegrationWinningPaid message)
        {
            _logger.Log($"win paid: {message.ToJson()}");
            return MessageResult.SuccessResult("Win confirmed", message.ActionId);
        }
        
        private async Task<MessageResult> RejectWinning(ErrorMessage message)
        {
            _logger.LogError($"win pay rejected: {message.ToJson()}");
            return MessageResult.ErrorResult("Win pay rejected", message.ActionId);
        }

        private async Task<Symbol[]> GetRandomResult(int reelsCount)
        {
            var results = new Symbol[reelsCount];
            var symbolsCount = Enum.GetValues(typeof(Symbol)).Length;
            
            for (int i = 0; i < reelsCount; i++)
            {
                results[i] = (Symbol)(await _fireballRng.NextInt(0, symbolsCount));
            }
            
            return results;
        }
        
        private float CalculateMultiplier(Symbol[] symbols)
        {
            if (symbols[0] == symbols[1] &&
                symbols[1] == symbols[2])
            {
                return SlotsConst.TriplePayouts[symbols[0]];
            }

            if (symbols[0] == symbols[1])
                return SlotsConst.DoublePayouts[symbols[0]];

            return 0f;
        }
    }
}