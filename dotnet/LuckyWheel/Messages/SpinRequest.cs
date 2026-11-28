using Fireball.Fireworks.Models;

namespace LuckyWheel
{
    public class SpinRequest : BaseMessage
    {
        public const string NAME = "spin-request";
        
        public long BetAmount;

        public SpinRequest()
        {
            Name = NAME;
        }
    }
}