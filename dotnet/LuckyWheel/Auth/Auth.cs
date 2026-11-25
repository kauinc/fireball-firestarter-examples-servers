using System.Collections.Generic;
using System.Threading.Tasks;
using Fireball.Fireworks;
using Fireball.Fireworks.Core;
using Fireball.Fireworks.Models;
using Microsoft.Extensions.Logging;

namespace LuckyWheel
{
    public interface IAuth
    {
        Task<MessageResult> TryAuth(AuthMessage message);
        Task<MessageResult> OnAuthSuccess(SessionMessage message);
        Task<MessageResult> OnAuthReject(ErrorMessage error);
        Task<MessageResult> OnDisconnected(BaseMessage message);
    }
    
    public class Auth : IAuth
    {
        private readonly IFireballLogger _logger;
        private readonly IFireworks _fireworks;

        public Auth(ILogger<Auth> logger, IFireworks fireworks)
        {
            _logger = new FireballLogger(nameof(Auth), logger);
            _fireworks = fireworks;
        }

        public async Task<MessageResult> TryAuth(AuthMessage message)
        {
            _logger.Log($"authenticate: {message.ToJson()}");

            // send the authenticating message
            return await _fireworks.Authenticate(message);
        }

        public async Task<MessageResult> OnAuthSuccess(SessionMessage message)
        {
            _logger.Log($"session: {message.ToJson()}");
            if (message.GameSession != null)
            {
                var gameSessionsList = await _fireworks.GetAllGameSessions(message);

                foreach (var gameSession in gameSessionsList)
                {
                    // get game state from the game session
                    var gameState = gameSession.ParseGameState<WheelGameState>();

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
                var defaultGameState = new WheelGameState();
                defaultGameState.BetAmount = GameConst.DefaultBet;
                defaultGameState.Balance = message.Balance;
                defaultGameState.Currency = message.Currency;

                // create a new permanent game session
                var gameSession = await _fireworks.CreatePermanentGameSession(defaultGameState, message);

                // update session message with new game session data
                message.UpdateGameSession(gameSession);
            }

            // adding jackpot ids to session
            var jackpots = new List<string>() { GameConst.JackpotTemplateId };
            
            // send session to the client
            return await _fireworks.SendSessionToClient(message, jackpots);
        }

        public async Task<MessageResult> OnAuthReject(ErrorMessage error)
        {
            _logger.LogError($"authentication rejected: {error.ToJson()}");
            return await _fireworks.SendErrorToClient(error, ErrorCode.Authentication);
        }

        public async Task<MessageResult> OnDisconnected(BaseMessage message)
        {
            _logger.Log($"disconnected: {message.ToJson()}");
            return MessageResult.SuccessResult("successfully disconnected", message.ActionId);
        }
    }
}