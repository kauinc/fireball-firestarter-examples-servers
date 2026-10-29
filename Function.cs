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

namespace SlotsSample
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
            _logger = new FireballLogger(nameof(Function), logger); ;
            _fireworks = fireworks;
            _game = game;
        }

        public async Task HandleAsync(CloudEvent cloudEvent, MessagePublishedData data, CancellationToken cancellationToken)
        {
            try
            {
                FireballLogger.LogLevel = LogLevel.Debug;
                
                string messageJson = data.Message?.TextData;
                ParseResult result = await _fireworks.ParseMessage(messageJson);
                MessageResult messageResult = await _game.HandleMessage(result);
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
    }
}