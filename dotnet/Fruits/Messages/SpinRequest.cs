using Fireball.Fireworks.Models;

namespace SlotsSample;

public class SpinRequest : BaseMessage
{
    public long BetAmount;

    public SpinRequest()
    {
        Name = GameMessages.SPIN_REQUEST;
    }
}