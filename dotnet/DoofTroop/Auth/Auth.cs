using System.Threading.Tasks;
using Fireball.Fireworks;
using Fireball.Fireworks.Core;
using Fireball.Fireworks.Models;
using Microsoft.Extensions.Logging;

namespace DoofTroop.Auth
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
                    var gameState = gameSession.ParseGameState<GameState>();
                    if (gameState?.IsGameOver == true)
                    {
                        await _fireworks.CloseGameSession(gameSession.Id);
                    }
                }
            }

            if (message.GameSession == null)
            {
                var defaultGameState = new GameState
                {
                    Balance = message.Balance,
                    Currency = message.Currency,
                    GameMode = message.GameMode,
                    WalletPhase = Constants.WalletPhase.Idle
                };

                var gameSession = await _fireworks.CreatePermanentGameSession(defaultGameState, message);
                message.UpdateGameSession(gameSession);
            }

            return await _fireworks.SendSessionToClient(message);
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
