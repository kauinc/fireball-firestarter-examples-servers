using System;
using System.Threading;
using System.Threading.Tasks;
using CloudNative.CloudEvents;
using DoofTroop.Auth;
using DoofTroop.Betting;
using DoofTroop.Cancel;
using DoofTroop.Lock;
using DoofTroop.Paytable;
using DoofTroop.Settlement;
using DoofTroop.Supabase;
using Fireball.Fireworks;
using Fireball.Fireworks.Core;
using Google.Cloud.Functions.Framework;
using Google.Cloud.Functions.Hosting;
using Google.Events.Protobuf.Cloud.PubSub.V1;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DoofTroop
{
    public class Startup : FunctionsStartup
    {
        public override void ConfigureServices(WebHostBuilderContext context, IServiceCollection services)
        {
            services.AddFireworks();
            services.AddHttpClient<ISupabaseRoundsClient, SupabaseRoundsClient>();
            services.AddSingleton<IPaytableService, PaytableService>();
            services.AddSingleton<IAuth, Auth.Auth>();
            services.AddSingleton<IBettingHandler, BettingHandler>();
            services.AddSingleton<ILockHandler, LockHandler>();
            services.AddSingleton<ISettlementHandler, SettlementHandler>();
            services.AddSingleton<ICancelHandler, CancelHandler>();
            services.AddSingleton<IGame, Game>();

            base.ConfigureServices(context, services);
        }
    }

    [FunctionsStartup(typeof(Startup))]
    public class Function : ICloudEventFunction<MessagePublishedData>
    {
        private readonly ILogger _logger;
        private readonly IFireworks _fireworks;
        private readonly IGame _game;

        public Function(ILogger<Function> logger, IFireworks fireworks, IGame game)
        {
            _logger = logger;
            _fireworks = fireworks;
            _game = game;
        }

        public async Task HandleAsync(
            CloudEvent cloudEvent,
            MessagePublishedData data,
            CancellationToken cancellationToken)
        {
            try
            {
                var messageJson = data.Message?.TextData;
                _logger.LogInformation("[DoofTroop] -----------------------------------------------------------------");
                _logger.LogInformation("[DoofTroop] Message Json: {Message}", messageJson);

                var result = await _fireworks.ParseMessage(messageJson);
                var messageResult = await _game.SwitchMessage(result);

                _logger.LogInformation(
                    "[DoofTroop] {MessageName} result: {Result}",
                    result.MessageName,
                    messageResult?.ToJson());
                _logger.LogInformation("[DoofTroop] -----------------------------------------------------------------");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DoofTroop] Exception");
            }
        }
    }
}
