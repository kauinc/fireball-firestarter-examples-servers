using System.Threading.Tasks;
using Fireball.Fireworks;
using Fireball.Fireworks.Core;
using Fireball.Fireworks.Models;
using Microsoft.Extensions.Logging;

namespace HelloWorld
{
    public interface IGame
    {
        Task<MessageResult> TryAuth(AuthMessage message);
        Task<MessageResult> OnAuthSuccess(SessionMessage message);
        Task<MessageResult> OnAuthReject(ErrorMessage error);
        Task<MessageResult> CheckMessage(HelloRequest message);
        Task<MessageResult> OnDisconnected(BaseMessage message);
    }
    
    public class Game : IGame
    {
        private readonly IFireballLogger _logger;
        private readonly IFireworks _fireworks;

        public Game(ILogger<Game> logger, IFireworks fireworks)
        {
            _logger = new FireballLogger(nameof(Game), logger);
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
                    var gameState = gameSession.ParseGameState<GameState>();

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
                var defaultGameState = new GameState();

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

        public async Task<MessageResult> CheckMessage(HelloRequest message)
        {
            _logger.Log($"message received: {message.MessageText}");
            
            // update game state
            var gameState = await _fireworks.GetGameState<GameState>(message.GameSession);
            gameState.HelloCount += 1;
            await _fireworks.SaveGameState(message.GameSession, gameState);
            
            // send response to the client
            var response = new HelloResponse("Hello, Client!", message);
            return await _fireworks.SendMessageToClient(response);
        }

        public async Task<MessageResult> OnDisconnected(BaseMessage message)
        {
            _logger.Log($"disconnected: {message.ToJson()}");
            return MessageResult.SuccessResult("successfully disconnected", message.ActionId);
        }
    }
}