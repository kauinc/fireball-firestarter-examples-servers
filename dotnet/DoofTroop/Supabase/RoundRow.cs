using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DoofTroop.Supabase
{
    public class RoundRow
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("channel_id")]
        public string ChannelId { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("placements")]
        public JToken Placements { get; set; }

        [JsonProperty("finisher_times_ms")]
        public JToken FinisherTimesMs { get; set; }

        [JsonProperty("betting_deadline")]
        public DateTimeOffset? BettingDeadline { get; set; }

        [JsonProperty("podium_transition_started_at")]
        public DateTimeOffset? PodiumTransitionStartedAt { get; set; }

        [JsonProperty("podium_entered_at")]
        public DateTimeOffset? PodiumEnteredAt { get; set; }

        [JsonProperty("test_money_only")]
        public bool? TestMoneyOnly { get; set; }

        [JsonProperty("round_version")]
        public int? RoundVersion { get; set; }

        [JsonProperty("created_at")]
        public DateTimeOffset? CreatedAt { get; set; }

        [JsonProperty("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }

        public List<object> PlacementList()
        {
            if (Placements == null || Placements.Type == JTokenType.Null)
            {
                return null;
            }

            if (Placements is JArray arr)
            {
                return arr.ToObject<List<object>>();
            }

            return null;
        }

        public List<int> PlacementIndexes()
        {
            var list = PlacementList();
            if (list == null || list.Count < 3) return null;

            var result = new List<int>();
            foreach (var item in list)
            {
                if (item == null) return null;
                if (!int.TryParse(item.ToString(), out var index)) return null;
                result.Add(index);
            }

            return result.Count >= 3 ? result : null;
        }

        public bool IsBettingOpen(DateTimeOffset? now = null)
        {
            if (Status != Constants.RoundStatus.BettingOpen) return false;
            var clock = now ?? DateTimeOffset.UtcNow;
            if (BettingDeadline.HasValue && clock >= BettingDeadline.Value) return false;
            return true;
        }

        public bool IsBettingClosedOrLater()
        {
            return Status is Constants.RoundStatus.BettingClosed
                or Constants.RoundStatus.Countdown
                or Constants.RoundStatus.RaceRunning
                or Constants.RoundStatus.ResultsRecorded
                or Constants.RoundStatus.Finished
                or Constants.RoundStatus.Cancelled;
        }

        public bool HasPodiumSignal() =>
            PodiumTransitionStartedAt.HasValue || PodiumEnteredAt.HasValue;

        public bool IsCancelled() => Status == Constants.RoundStatus.Cancelled;
    }
}
