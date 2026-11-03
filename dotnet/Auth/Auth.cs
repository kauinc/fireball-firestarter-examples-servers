using System.Threading.Tasks;
using Fireball.Fireworks;
using Fireball.Fireworks.Core;
using Fireball.Fireworks.Models;
using Microsoft.Extensions.Logging;

namespace SlotsSample
{
    public interface IAuth
    {
        Task<MessageResult> TryAuth(AuthMessage message);
        Task<MessageResult> OnAuthSuccess(SessionMessage message);
        Task<MessageResult> OnAuthReject(ErrorMessage error);
    }
    
    public class Auth : IAuth
    {
        private readonly IFireballLogger _logger;
        private readonly IFireworks _fireworks;

        public Auth(ILogger<Slots> logger, IFireworks fireworks)
        {
            _logger = new FireballLogger(nameof(Slots), logger);
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

        public async Task<MessageResult> OnAuthReject(ErrorMessage error)
        {
            _logger.LogError($"authentication rejected: {error.ToJson()}");
            return await _fireworks.SendErrorToClient(error, ErrorCode.Authentication);
        }
    }
}