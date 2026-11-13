using Fireball.Fireworks.Models;

namespace HelloWorld;

public class HelloRequest : BaseMessage
{
    public const string NAME = "hello-request";
        
    public string MessageText;
}