using Fireball.Fireworks.Models;

namespace HelloWorld;

public class HelloResponse : BaseMessage
{
    public const string NAME = "hello-response";
        
    public string MessageText;
        
    public HelloResponse(string text, BaseMessage baseMessage)
    {
        CopyBaseParams(baseMessage);

        Name = NAME;
        MessageText = text;
    }
}