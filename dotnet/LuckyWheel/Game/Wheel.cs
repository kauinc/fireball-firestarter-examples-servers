using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fireball.Fireworks;
using Fireball.Fireworks.Core;
using Fireball.Fireworks.IntegrationModule;
using Fireball.Fireworks.JackpotsModule;
using Fireball.Fireworks.Models;
using Fireball.Fireworks.Rng;
using Microsoft.Extensions.Logging;

namespace LuckyWheel
{
    public interface IWheel
    {
        Task<MessageResult> TrySpin(SpinRequest message);
        Task<MessageResult> OnBetPlaced(IntegrationBetPlaced message);
        Task<MessageResult> OnBetRejected(ErrorMessage message);
        Task<MessageResult> OnWinConfirmed(IntegrationWinningPaid message);
        Task<MessageResult> OnWinRejected(ErrorMessage message);
        Task<MessageResult> OnWinJackpotConfirmed(IntegrationJackpotPaid message);
        Task<MessageResult> OnWinJackpotRejected(ErrorMessage message);
    }

    public class Wheel : IWheel
    {
        private readonly IFireballLogger _logger;
        private readonly IFireworks _fireworks;
        private readonly IJackpots _jackpots;
        private readonly IRng _fireballRng;
        
        public Wheel(ILogger<Wheel> logger, IFireworks fireworks, IJackpots jackpots, IRng fireballRng)
        {
            _logger = new FireballLogger(nameof(Wheel), logger);
            _fireworks = fireworks;
            _jackpots = jackpots;
            _fireballRng = fireballRng;
        }

        public async Task<MessageResult> TrySpin(SpinRequest message)
        {
            _logger.Log($"spin: request = {message.ToJson()}");

            // updating game state
            var gameState = await _fireworks.GetGameState<WheelGameState>(message.GameSession);

            gameState.IsSpinning = true;
            gameState.BetAmount = message.BetAmount;

            // calculate planned contribution to jackpot
            gameState.JackpotContribution = CalculateContribution(message.BetAmount);
            
            await _fireworks.SaveGameState(message.GameSession, gameState);

            // send a bet place message to integration with jackpot contributions
            _logger.Log($"spin: betAmount = {message.BetAmount} {message.Currency}");
            return await _fireworks.PlaceBet(
                FireballConstants.BetType.SPIN,
                gameState.BetAmount,
                message,
                jackpotContributions: new List<JackpotContribution>()
                {
                    new()
                    {
                        Id = GameConst.JackpotTemplateId,
                        Contribution = gameState.JackpotContribution,
                    }
                });
        }

        public async Task<MessageResult> OnBetPlaced(IntegrationBetPlaced message)
        {
            _logger.Log($"spin: betPlaced = {message.ToJson()}");
            var gameState = await _fireworks.GetGameState<WheelGameState>(message.GameSession);
            
            // contribute to the jackpot and try to win it
            var jackpotResult = await _jackpots.ContributeAndGetOutcome(
                GameConst.JackpotTemplateId, 
                gameState.JackpotContribution, 
                message);
            
            _logger.Log($"spin: jackpot value = {jackpotResult.NewJackpotAmount} {message.Currency}");

            long winAmount;
            Symbol winSymbol;
            if (jackpotResult.Won)
            {
                // set jackpot win
                winSymbol = Symbol.Jackpot;
                winAmount = jackpotResult.AmountWon;
            }
            else
            {
                // generate random result and calculate winning
                winSymbol = await GetRandomResult();
                winAmount = CalculateWinAmount(winSymbol, message.Amount);
            }

            _logger.Log($"spin: winSymbol = {winSymbol}");
            _logger.Log($"spin: winAmount = {winAmount} {message.Currency}");

            // updating game state
            gameState.IsSpinning = false;
            gameState.Symbol = winSymbol;
            gameState.WinAmount = winAmount;
            gameState.Balance = message.Balance + winAmount;
            
            await _fireworks.SaveGameState(message.GameSession, gameState);

            if (jackpotResult.Won)
            {
                // send a pay jackpot message to integration
                var jackpotEntry = new JackpotEntry()
                {
                    Amount = jackpotResult.AmountWon,
                    Savings = jackpotResult.Savings,
                    JackpotId = jackpotResult.JackpotId,
                    TemplateId = jackpotResult.TemplateId,
                };
                await _fireworks.PayJackpot(
                    new List<JackpotEntry>() { jackpotEntry }, 
                    message.OperatorBetId, 
                    message);
            }
            else
            {
                // send a pay win message to integration
                await _fireworks.PayWin(
                    FireballConstants.BetType.SPIN, 
                    message.OperatorBetId, 
                    winAmount, 
                    message);
            }

            // send the spin result message to the game client
            var response = new SpinResult(gameState, message);
            return await _fireworks.SendMessageToClient(response);
        }

        public async Task<MessageResult> OnBetRejected(ErrorMessage message)
        {
            _logger.LogError($"spin rejected: {message.ToJson()}");
            
            // reset game state
            var gameState = await _fireworks.GetGameState<WheelGameState>(message.GameSession);
            
            gameState.IsSpinning = false;
            gameState.WinAmount = 0;
            gameState.JackpotContribution = 0;
            
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

        public async Task<MessageResult> OnWinJackpotConfirmed(IntegrationJackpotPaid message)
        {
            _logger.Log($"jackpot paid: {message.ToJson()}");
            return MessageResult.SuccessResult("Jackpot Win confirmed", message.ActionId);
        }

        public async Task<MessageResult> OnWinJackpotRejected(ErrorMessage message)
        {
            _logger.Log($"jackpot pay rejected: {message.ToJson()}");
            return MessageResult.ErrorResult("Jackpot pay rejected", message.ActionId);
        }

        private async Task<Symbol> GetRandomResult()
        {
            var symbols = GameConst.SymbolWeights.Keys.ToList();
            var weights = GameConst.SymbolWeights.Values.ToList();
            var index = await _fireballRng.IndexByWeights(weights);

            return symbols[index];
        }
        
        private long CalculateWinAmount(Symbol symbol, long betAmount)
        {
            return (long)(GameConst.SymbolPayouts[symbol] * betAmount);
        }

        private long CalculateContribution(long betAmount)
        {
            return (long)(GameConst.JackpotContributionPercent * betAmount);
        }
    }
}