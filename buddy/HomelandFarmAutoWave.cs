namespace HeartopiaMod
{
    internal enum HomelandFarmAutoWaveAction
    {
        Idle = 0,
        Hold = 1,
        HarvestAndSow = 2,
    }

    internal static class HomelandFarmAutoWave
    {
        public const int SyncDelayMinSeconds = 1;
        public const int SyncDelayMaxSeconds = 5;
        public const int SyncDelayDefaultSeconds = 3;

        public static int ClampSyncDelaySeconds(int seconds)
        {
            if (seconds < SyncDelayMinSeconds || seconds > SyncDelayMaxSeconds)
            {
                return SyncDelayDefaultSeconds;
            }

            return seconds;
        }

        // ripeCount: tracked crops with stage >= 4 this tick. Growing crops are not an input.
        // secondsSinceFirstRipe: realtime seconds since this burst first observed a ripe crop.
        // Negative when nothing ripe has been seen.
        // syncDelaySeconds: user pad. Out of 1..5 (including a missing config's 0) is the default.
        public static HomelandFarmAutoWaveAction Decide(
            int ripeCount,
            float secondsSinceFirstRipe,
            int syncDelaySeconds)
        {
            if (ripeCount <= 0)
            {
                return HomelandFarmAutoWaveAction.Idle;
            }

            int delay = ClampSyncDelaySeconds(syncDelaySeconds);
            if (secondsSinceFirstRipe < delay)
            {
                return HomelandFarmAutoWaveAction.Hold;
            }

            return HomelandFarmAutoWaveAction.HarvestAndSow;
        }
    }
}
