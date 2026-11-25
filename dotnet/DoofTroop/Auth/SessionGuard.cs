using System;
using Fireball.Fireworks.Models;

namespace DoofTroop.Auth
{
    /// <summary>
    /// Binds launch session fingerprint into GameState and rejects mismatches
    /// on subsequent wallet / betting mutations.
    /// </summary>
    public static class SessionGuard
    {
        public static void ApplyBindFromSession(GameState state, BaseMessage message)
        {
            if (state == null || message == null) return;

            state.BoundPlayerId = FirstNonEmpty(message.PlayerId, state.BoundPlayerId);
            state.BoundGameId = FirstNonEmpty(message.GameId, state.BoundGameId);
            state.BoundEnvironment = FirstNonEmpty(message.Environment, state.BoundEnvironment);
            state.BoundGameMode = FirstNonEmpty(message.GameMode, state.BoundGameMode);
            state.BoundCurrency = FirstNonEmpty(message.Currency, state.BoundCurrency);

            state.GameMode = state.BoundGameMode ?? state.GameMode;
            state.Currency = state.BoundCurrency ?? state.Currency;
        }

        /// <summary>
        /// Ensures message matches bound session from auth. Unbound states must
        /// re-authenticate — never hydrate binds from client wallet messages.
        /// Overwrites message GameMode/Currency from binds so stake conversion
        /// cannot be spoofed.
        /// </summary>
        public static string ValidateAndApply(GameState state, BaseMessage message)
        {
            if (state == null) return "Missing game state.";
            if (message == null) return "Missing message.";

            if (!HasBind(state))
            {
                return "Session not bound; re-authenticate.";
            }

            if (Mismatch(state.BoundPlayerId, message.PlayerId))
            {
                return "Session player mismatch.";
            }

            if (Mismatch(state.BoundGameId, message.GameId))
            {
                return "Session game mismatch.";
            }

            if (Mismatch(state.BoundEnvironment, message.Environment))
            {
                return "Session environment mismatch.";
            }

            if (Mismatch(state.BoundGameMode, message.GameMode))
            {
                return "Session game mode mismatch.";
            }

            if (Mismatch(state.BoundCurrency, message.Currency))
            {
                return "Session currency mismatch.";
            }

            // Force conversion helpers to use bound values, not client spoof.
            if (!string.IsNullOrEmpty(state.BoundGameMode))
            {
                message.GameMode = state.BoundGameMode;
                state.GameMode = state.BoundGameMode;
            }

            if (!string.IsNullOrEmpty(state.BoundCurrency))
            {
                message.Currency = state.BoundCurrency;
                state.Currency = state.BoundCurrency;
            }

            return null;
        }

        public static bool HasBind(GameState state) =>
            state != null
            && (!string.IsNullOrEmpty(state.BoundPlayerId)
                || !string.IsNullOrEmpty(state.BoundGameId)
                || !string.IsNullOrEmpty(state.BoundEnvironment));

        private static bool Mismatch(string bound, string actual)
        {
            // Missing actual is tolerated (some platform callbacks omit fields);
            // when present it must match the auth bind.
            if (string.IsNullOrEmpty(bound) || string.IsNullOrEmpty(actual)) return false;
            return !string.Equals(bound, actual, StringComparison.OrdinalIgnoreCase);
        }

        private static string FirstNonEmpty(string primary, string fallback) =>
            !string.IsNullOrEmpty(primary) ? primary : fallback;
    }
}
