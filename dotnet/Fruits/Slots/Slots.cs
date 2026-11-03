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
    public interface ISlots
    {
        Task<MessageResult> TrySpin(SpinRequest message);
        Task<MessageResult> OnBetPlaced(IntegrationBetPlaced message);
        Task<MessageResult> OnBetRejected(ErrorMessage message);
        Task<MessageResult> OnWinConfirmed(IntegrationWinningPaid message);
        Task<MessageResult> OnWinRejected(ErrorMessage message);
    }

    public class Slots : ISlots
    {
        private readonly IFireballLogger _logger;
        private readonly IFireworks _fireworks;
        private readonly IRng _fireballRng;


        public Slots(ILogger<Slots> logger, IFireworks fireworks, IRng fireballRng)
        {
            _logger = new FireballLogger(nameof(Slots), logger);
            _fireworks = fireworks;
            _fireballRng = fireballRng;
        }

        public async Task<MessageResult> TrySpin(SpinRequest message)
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
        
        public async Task<MessageResult> OnBetPlaced(IntegrationBetPlaced message)
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

        public async Task<MessageResult> OnBetRejected(ErrorMessage message)
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
        
        public async Task<MessageResult> OnWinConfirmed(IntegrationWinningPaid message)
        {
            _logger.Log($"win paid: {message.ToJson()}");
            return MessageResult.SuccessResult("Win confirmed", message.ActionId);
        }
        
        public async Task<MessageResult> OnWinRejected(ErrorMessage message)
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