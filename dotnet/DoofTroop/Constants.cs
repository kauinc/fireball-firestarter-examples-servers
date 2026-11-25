namespace DoofTroop
{
    public static class Constants
    {
        public static class Messages
        {
            public const string PlaceChip = "place-chip";
            public const string RemoveChip = "remove-chip";
            public const string ClearBets = "clear-bets";
            public const string DoubleBets = "double-bets";
            public const string RepeatLast = "repeat-last";
            public const string SetComboPick = "set-combo-pick";
            public const string SetCrazyComboPicks = "set-crazy-combo-picks";
            public const string CarryForward = "carry-forward";
            public const string UndoBets = "undo-bets";
            public const string LockBets = "lock-bets";
            public const string SettleRound = "settle-round";
            public const string CancelRound = "cancel-round";

            public const string BetsUpdated = "bets-updated";
            public const string BetsLocked = "bets-locked";
            public const string BetsRejected = "bets-rejected";
            public const string SettleResult = "settle-result";
            public const string RefundResult = "refund-result";
        }

        public static class RoundStatus
        {
            public const string Created = "CREATED";
            public const string BettingOpen = "BETTING_OPEN";
            public const string BettingClosed = "BETTING_CLOSED";
            public const string Countdown = "COUNTDOWN";
            public const string RaceRunning = "RACE_RUNNING";
            public const string ResultsRecorded = "RESULTS_RECORDED";
            public const string Finished = "FINISHED";
            public const string Cancelled = "CANCELLED";
        }

        public static class WalletPhase
        {
            public const string Idle = "Idle";
            public const string Pending = "Pending";
            public const string Locking = "Locking";
            public const string Locked = "Locked";
            public const string Paying = "Paying";
            public const string Settled = "Settled";
            public const string Refunding = "Refunding";
            public const string Refunded = "Refunded";
            public const string Rejected = "Rejected";
        }

        public static class ComboKinds
        {
            public const string Colors = "colors";
            public const string Patterns = "patterns";
            public const string Accessories = "accessories";

            public static readonly string[] All = { Colors, Patterns, Accessories };
        }

        public static class BetTargetTypes
        {
            public const string Doof = "doof";
            public const string Split = "split";
            public const string Pattern = "pattern";
            public const string Color = "color";
            public const string Accessory = "accessory";
            public const string Combo = "combo";
            public const string CrazyCombo = "crazyCombo";

            public static readonly string[] All =
            {
                Doof, Split, Pattern, Color, Accessory, Combo, CrazyCombo
            };
        }

        public static class Places
        {
            public const string First = "1st";
            public const string Second = "2nd";
            public const string Third = "3rd";

            public static readonly string[] All = { First, Second, Third };
        }

        public static readonly string[] DoofColors =
        {
            "Red", "Yellow", "Green", "Cyan", "Blue", "Pink"
        };

        public static readonly string[] DoofPatterns =
        {
            "Dots", "Solid", "Stripes"
        };

        public static readonly string[] DoofAccessories =
        {
            "Hats", "Glasses"
        };

        public static readonly decimal[] ChipValues =
        {
            0.2m, 0.4m, 0.6m, 0.8m, 1m, 2m, 5m, 10m, 20m, 50m
        };

        public static readonly string[] ChipStackOrder =
        {
            "bronze", "silver", "gold"
        };

        public const decimal MaxBetPerTarget = 100m;
        /// <summary>Max stake per target in coin units (coins mode).</summary>
        public const decimal MaxBetPerTargetCoins = 10000m;
        public const int MaxUndo = 40;
        public const string DefaultChannelId = "43fa047a-dfe0-45a3-a093-2cb11cc29b55";
        public const string DefaultSupabaseUrl = "https://xypxrqyecjeiftuqppeu.supabase.co";
    }
}
