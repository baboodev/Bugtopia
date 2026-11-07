using System;
using HeartopiaMod;

internal static class Program
{
    private static int failures;

    private static void Expect(HomelandFarmAutoWaveAction actual, HomelandFarmAutoWaveAction expected, string name)
    {
        if (actual == expected)
        {
            Console.WriteLine("PASS " + name);
            return;
        }

        failures++;
        Console.WriteLine("FAIL " + name + " expected " + expected + " actual " + actual);
    }

    private static void Main()
    {
        Expect(HomelandFarmAutoWave.Decide(0, -1f, 3), HomelandFarmAutoWaveAction.Idle, "nothing ripe stays idle");
        Expect(HomelandFarmAutoWave.Decide(-2, 10f, 3), HomelandFarmAutoWaveAction.Idle, "negative ripe count stays idle");
        Expect(HomelandFarmAutoWave.Decide(12, 0f, 3), HomelandFarmAutoWaveAction.Hold, "first ripe tick holds");
        Expect(HomelandFarmAutoWave.Decide(40, 2.9f, 3), HomelandFarmAutoWaveAction.Hold, "burst still inside the pad holds");
        Expect(HomelandFarmAutoWave.Decide(40, 3f, 3), HomelandFarmAutoWaveAction.HarvestAndSow, "pad boundary runs the pass");
        Expect(HomelandFarmAutoWave.Decide(4, 9f, 5), HomelandFarmAutoWaveAction.HarvestAndSow, "elapsed pad runs the pass");
        Expect(HomelandFarmAutoWave.Decide(4, 2f, 0), HomelandFarmAutoWaveAction.Hold, "missing config uses 3s");
        Expect(HomelandFarmAutoWave.Decide(4, 3f, 0), HomelandFarmAutoWaveAction.HarvestAndSow, "missing config elapses at 3s");
        Expect(HomelandFarmAutoWave.Decide(4, 4f, 99), HomelandFarmAutoWaveAction.HarvestAndSow, "out-of-range pad uses 3s");

        if (HomelandFarmAutoWave.ClampSyncDelaySeconds(0) != 3
            || HomelandFarmAutoWave.ClampSyncDelaySeconds(1) != 1
            || HomelandFarmAutoWave.ClampSyncDelaySeconds(5) != 5
            || HomelandFarmAutoWave.ClampSyncDelaySeconds(3) != 3
            || HomelandFarmAutoWave.ClampSyncDelaySeconds(9) != 3)
        {
            failures++;
            Console.WriteLine("FAIL clamp");
        }
        else
        {
            Console.WriteLine("PASS clamp");
        }

        if (failures != 0)
        {
            throw new InvalidOperationException(failures + " failed");
        }

        Console.WriteLine("ALL PASS");
    }
}
