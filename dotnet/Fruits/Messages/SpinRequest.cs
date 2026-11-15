using Fireball.Fireworks.Models;

namespace FruitsSample
{
    public class SpinRequest : BaseMessage
    {
        public long BetAmount;

        public SpinRequest()
        {
            Name = GameMessages.SPIN_REQUEST;
        }
    }
}