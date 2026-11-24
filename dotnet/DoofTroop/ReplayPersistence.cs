using System.Threading.Tasks;
using Fireball.Fireworks;

namespace DoofTroop
{
    /// <summary>
    /// Persist live session state, and when a ReplayId is known also write the
    /// snapshot into RGS replay storage (Aphrodite / Bonbonella pattern).
    /// </summary>
    public static class ReplayPersistence
    {
        public static async Task SaveAsync(
            IFireworks fireworks,
            string gameSession,
            GameState state,
            string replayIdHint = null)
        {
            if (!string.IsNullOrEmpty(replayIdHint))
            {
                state.LastReplayId = replayIdHint;
            }

            var replayId = state.LastReplayId;
            if (string.IsNullOrEmpty(replayId))
            {
                await fireworks.SaveGameState(gameSession, state);
                return;
            }

            await fireworks.SaveGameStateForReplay(gameSession, state, replayId);
        }
    }
}
