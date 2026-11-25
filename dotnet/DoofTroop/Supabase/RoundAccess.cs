using System;

namespace DoofTroop.Supabase
{
    /// <summary>
    /// Shared round policy: channel allow-list and test-money gating.
    /// </summary>
    public static class RoundAccess
    {
        public static string ExpectedChannelId =>
            Environment.GetEnvironmentVariable("DOOF_CHANNEL_ID")
            ?? Constants.DefaultChannelId;

        public static string ValidateChannel(RoundRow round)
        {
            if (round == null) return "Round not found.";
            if (string.IsNullOrEmpty(round.ChannelId)) return "Round channel is missing.";
            if (!string.Equals(round.ChannelId, ExpectedChannelId, StringComparison.OrdinalIgnoreCase))
            {
                return "Round channel is not allowed for this game.";
            }

            return null;
        }

        /// <summary>
        /// test_money_only rounds must not accept real-money (non-coins) sessions.
        /// </summary>
        public static string ValidateTestMoney(RoundRow round, bool sessionIsCoins)
        {
            if (round?.TestMoneyOnly == true && !sessionIsCoins)
            {
                return "This round is test-money only.";
            }

            return null;
        }
    }
}
