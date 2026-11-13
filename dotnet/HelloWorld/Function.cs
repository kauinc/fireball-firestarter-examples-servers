using System;
using System.Threading;
using System.Threading.Tasks;
using CloudNative.CloudEvents;
using Fireball.Fireworks;
using Fireball.Fireworks.Core;
using Fireball.Fireworks.Models;
using Google.Cloud.Functions.Framework;
using Google.Cloud.Functions.Hosting;
using Google.Events.Protobuf.Cloud.PubSub.V1;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelloWorld
{
    public class Startup : FunctionsStartup
    {
        public override void ConfigureServices(WebHostBuilderContext context, IServiceCollection services)
        {
            services.AddFireworks();
            services.AddSingleton<IGame, Game>();

            base.ConfigureServices(context, services);
        }
    }

    [FunctionsStartup(typeof(Startup))]
    public class Function : ICloudEventFunction<MessagePublishedData>
    {
        private readonly IFireballLogger _logger;
        private readonly IFireworks _fireworks;
        private readonly IGame _game;

        public Function(ILogger<Function> logger, IFireworks fireworks, IGame game)
        {
            _logger = new FireballLogger(nameof(Function), logger);
            _fireworks = fireworks;
            _game = game;
        }

        public async Task HandleAsync(CloudEvent cloudEvent, MessagePublishedData data, CancellationToken cancellationToken)
        {
            try
            {
                var messageJson = data.Message?.TextData;
                var result = await _fireworks.ParseMessage(messageJson);
                var messageResult = await HandleMessage(result);
                
                _logger.Log($"Result: {messageResult?.Message}", 
                    messageResult != null && messageResult.IsError() ? 
                        LogLevel.Error : 
                        LogLevel.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Exception: {ex}");
            }
        }
        
        private async Task<MessageResult> HandleMessage(ParseResult result)
        {
            if (result.IsSuccess)
            {
                switch (result.MessageName)
                {
                    case FireballConstants.MessagesNames.AUTHENTICATE:
                        return await _game.TryAuth(result.ToMessage<AuthMessage>());
                    
                    case FireballConstants.MessagesNames.SESSION:
                        return await _game.OnAuthSuccess(result.ToMessage<SessionMessage>());
                    
                    case FireballConstants.MessagesNames.AUTHENTICATE_REJECT:
                        return await _game.OnAuthReject(result.ToMessage<ErrorMessage>());
                    
                    case HelloRequest.NAME:
                        return await _game.CheckMessage(result.ToMessage<HelloRequest>());
                    
                    case FireballConstants.MessagesNames.DISCONNECTED:
                        return await _game.OnDisconnected(result.ToMessage<BaseMessage>());
                    
                    default:
                        return MessageResult.ErrorResult($"Undefined message with name = {result.MessageName}");
                }
            }

            return MessageResult.ErrorResult("Error Parse message...");
        }
    }
}