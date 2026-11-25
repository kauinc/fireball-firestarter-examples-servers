using Fireball.Fireworks.Models;

namespace LuckyWheel
{
    public class SpinResult : BaseMessage
    {
        public const string NAME = "spin-result";
        
        public Symbol Symbol;
        public long WinAmount;
        public long Balance;

        public SpinResult(WheelGameState gameState, BaseMessage baseMessage)
        {
            CopyBaseParams(baseMessage);

            Name = NAME;
            Symbol = gameState.Symbol;
            WinAmount = gameState.WinAmount;
            Balance = gameState.Balance;
        }
    }
}