using System;
using System.Threading;
using System.Threading.Tasks;
using CloudNative.CloudEvents;
using Fireball.Fireworks;
using Fireball.Fireworks.Core;
using Fireball.Fireworks.IntegrationModule;
using Fireball.Fireworks.Models;
using Google.Cloud.Functions.Framework;
using Google.Cloud.Functions.Hosting;
using Google.Events.Protobuf.Cloud.PubSub.V1;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SlotsSample
{
    public class Startup : FunctionsStartup
    {
        public override void ConfigureServices(WebHostBuilderContext context, IServiceCollection services)
        {
            services.AddFireworks();
            services.AddSingleton<IAuth, Auth>();
            services.AddSingleton<ISlots, Slots>();

            base.ConfigureServices(context, services);
        }
    }

    [FunctionsStartup(typeof(Startup))]
    public class Function : ICloudEventFunction<MessagePublishedData>
    {
        private readonly IFireballLogger _logger;
        private readonly IFireworks _fireworks;
        private readonly IAuth _auth;
        private readonly ISlots _slots;

        public Function(ILogger<Function> logger, IFireworks fireworks, IAuth auth, ISlots slots)
        {
            _logger = new FireballLogger(nameof(Function), logger);
            _fireworks = fireworks;
            _auth = auth;
            _slots = slots;
        }

        public async Task HandleAsync(CloudEvent cloudEvent, MessagePublishedData data, CancellationToken cancellationToken)
        {
            try
            {
                FireballLogger.LogLevel = LogLevel.Debug;
                
                string messageJson = data.Message?.TextData;
                ParseResult result = await _fireworks.ParseMessage(messageJson);
                MessageResult messageResult = await HandleMessage(result);
                if (messageResult != null && messageResult.IsError())
                {
                    _logger.LogError($"Result: {messageResult?.Message}");
                }
                else
                {
                    _logger.Log($"Result: {messageResult?.Message}");
                }
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
                        return await _auth.TryAuth(result.ToMessage<AuthMessage>());
                    case FireballConstants.MessagesNames.SESSION:
                        return await _auth.OnAuthSuccess(result.ToMessage<SessionMessage>());
                    case FireballConstants.MessagesNames.AUTHENTICATE_REJECT:
                        return await _auth.OnAuthReject(result.ToMessage<ErrorMessage>());
                    
                    case GameMessages.SPIN_REQUEST:
                        return await _slots.TrySpin(result.ToMessage<SpinRequest>());
                    case FireballConstants.MessagesNames.BET_PLACED:
                        return await _slots.OnBetPlaced(result.ToMessage<IntegrationBetPlaced>());
                    case FireballConstants.MessagesNames.BET_PLACE_REJECTED:
                        return await _slots.OnBetRejected(result.ToMessage<ErrorMessage>());
                    case FireballConstants.MessagesNames.WINNING_PAID:
                        return await _slots.OnWinConfirmed(result.ToMessage<IntegrationWinningPaid>());
                    case FireballConstants.MessagesNames.WINNING_PAY_REJECTED:
                        return await _slots.OnWinRejected(result.ToMessage<ErrorMessage>());
                    
                    default:
                        return MessageResult.ErrorResult($"Undefined message with name = {result.MessageName}");
                }
            }

            return MessageResult.ErrorResult("Error Parse message...");
        }
    }
}