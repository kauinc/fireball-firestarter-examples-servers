using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace DoofTroop.Supabase
{
    public interface ISupabaseRoundsClient
    {
        Task<RoundRow> GetRoundByIdAsync(string roundId);
        Task<RoundRow> GetLatestRoundAsync(string channelId = null);
    }

    public class SupabaseRoundsClient : ISupabaseRoundsClient
    {
        private static readonly string RoundColumns = string.Join(",",
            "id", "channel_id", "status", "placements", "finisher_times_ms",
            "betting_deadline", "podium_transition_started_at", "podium_entered_at",
            "test_money_only", "round_version", "created_at", "updated_at");

        private readonly HttpClient _http;
        private readonly ILogger<SupabaseRoundsClient> _logger;
        private readonly string _channelId;

        public SupabaseRoundsClient(HttpClient http, ILogger<SupabaseRoundsClient> logger)
        {
            _http = http;
            _logger = logger;
            _channelId = Environment.GetEnvironmentVariable("DOOF_CHANNEL_ID")
                         ?? Constants.DefaultChannelId;

            var url = Environment.GetEnvironmentVariable("SUPABASE_URL")
                      ?? Constants.DefaultSupabaseUrl;
            var key = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY")
                      ?? Environment.GetEnvironmentVariable("VITE_SUPABASE_ANON_KEY")
                      ?? string.Empty;

            _http.BaseAddress = new Uri(url.TrimEnd('/') + "/");
            _http.DefaultRequestHeaders.Clear();
            if (!string.IsNullOrWhiteSpace(key))
            {
                _http.DefaultRequestHeaders.Add("apikey", key);
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
            }
        }

        public async Task<RoundRow> GetRoundByIdAsync(string roundId)
        {
            if (string.IsNullOrWhiteSpace(roundId)) return null;

            var path =
                $"rest/v1/rounds?select={RoundColumns}&id=eq.{Uri.EscapeDataString(roundId)}&limit=1";
            return await GetSingleAsync(path);
        }

        public async Task<RoundRow> GetLatestRoundAsync(string channelId = null)
        {
            var channel = channelId ?? _channelId;
            var path =
                $"rest/v1/rounds?select={RoundColumns}&channel_id=eq.{Uri.EscapeDataString(channel)}&order=created_at.desc&limit=1";
            return await GetSingleAsync(path);
        }

        private async Task<RoundRow> GetSingleAsync(string path)
        {
            try
            {
                using var response = await _http.GetAsync(path);
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Supabase GET failed {Status}: {Body}", response.StatusCode, body);
                    return null;
                }

                var rows = JsonConvert.DeserializeObject<RoundRow[]>(body);
                return rows is { Length: > 0 } ? rows[0] : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Supabase GET exception for {Path}", path);
                return null;
            }
        }
    }
}
