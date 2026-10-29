using System.Collections.Generic;
using Fireball.Fireworks.Models;

namespace SlotsSample
{
    public static class GameMessages
    {
        public const string SPIN_REQUEST = "spin-request";
        public const string SPIN_RESULT = "spin-result";
    }
    
    public class SpinRequest : BaseMessage
    {
        public long BetAmount;

        public SpinRequest()
        {
            Name = GameMessages.SPIN_REQUEST;
        }
    }

    public class SpinResult : BaseMessage
    {
        public Symbol[] Symbols;
        public long WinAmount;
        public long Balance;
        
        public SpinResult(SlotsGameState gameState, BaseMessage baseMessage)
        {
            CopyBaseParams(baseMessage);

            Name = GameMessages.SPIN_RESULT;
            Symbols = gameState.Symbols;
            WinAmount = gameState.WinAmount;
            Balance = gameState.Balance;
        }
    }
}