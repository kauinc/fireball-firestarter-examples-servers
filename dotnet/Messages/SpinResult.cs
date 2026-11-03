using Fireball.Fireworks.Models;

namespace SlotsSample;

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