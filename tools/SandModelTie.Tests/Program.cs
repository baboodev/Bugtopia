using System;
using HeartopiaMod;

internal static class Program
{
    private static int failures;
    private static readonly int[] Rarity = { 0, 1, 1, 2, 1, 1, 2, 3, 3, 4, 5, 4, 4, 4, 5 };

    private static void Expect(int actual, int expected, string name)
    {
        if (actual == expected)
        {
            Console.WriteLine("PASS " + name);
            return;
        }
        failures++;
        Console.WriteLine("FAIL " + name + " expected " + expected + " actual " + actual);
    }

    private static void ExpectText(string actual, string expected, string name)
    {
        if (actual == expected)
        {
            Console.WriteLine("PASS " + name);
            return;
        }
        failures++;
        Console.WriteLine("FAIL " + name + " expected [" + expected + "] actual [" + actual + "]");
    }

    private static int[] Collect(params int[] pairs)
    {
        int[] byId = new int[15];
        for (int i = 0; i < pairs.Length; i += 2)
        {
            byId[pairs[i]] = pairs[i + 1];
        }
        return byId;
    }

    private static bool[] Reads(params int[] ids)
    {
        bool[] ok = new bool[15];
        for (int i = 0; i < ids.Length; i++)
        {
            ok[ids[i]] = true;
        }
        return ok;
    }

    private static void Main()
    {
        string key;
        bool advance;
        int[] shipBear = { 25, 7, 8 };
        bool[] both = Reads(7, 8);

        int index = SandModelTie.Pick(shipBear, Collect(7, 2, 8, 2), both, Rarity, "", true, out key, out advance);
        Expect(index, 1, "2/2 first sight is ship");
        ExpectText(key, "7+8", "ship bear key");
        Expect(advance ? 1 : 0, 1, "first sight arms the cursor");

        index = SandModelTie.Pick(shipBear, Collect(7, 1, 8, 1), both, Rarity, "7+8=7", true, out key, out advance);
        Expect(shipBear[index], 8, "1/1 next is bear");
        index = SandModelTie.Pick(new[] { 8, 17, 7 }, Collect(7, 0, 8, 0), both, Rarity, "7+8=8", true, out key, out advance);
        Expect(index, 2, "0/0 wraps to ship regardless of option order");

        index = SandModelTie.Pick(shipBear, Collect(7, 1, 8, 2), both, Rarity, "7+8=7", true, out key, out advance);
        Expect(shipBear[index], 8, "higher collect is not a tie");
        Expect(advance ? 1 : 0, 0, "unequal collect does not arm");

        index = SandModelTie.Pick(new[] { 7, 8, 9 }, Collect(7, 0, 8, 0, 9, 0), Reads(7, 8, 9), Rarity, "7+8=8", true, out key, out advance);
        Expect(index, 2, "whale rarity leaves the ship bear cursor alone");
        Expect(advance ? 1 : 0, 0, "single top band does not arm");

        index = SandModelTie.Pick(new[] { 5, 2, 4, 1 }, Collect(1, 0, 2, 0, 4, 0, 5, 0), Reads(1, 2, 4, 5), Rarity, "", true, out key, out advance);
        ExpectText(key, "1+2+4+5", "rarity 1 set");
        Expect(index, 3, "four-way starts at car");
        index = SandModelTie.Pick(new[] { 5, 2, 4, 1 }, Collect(1, 0, 2, 0, 4, 0, 5, 0), Reads(1, 2, 4, 5), Rarity, "1+2+4+5=4", true, out key, out advance);
        Expect(shipBear[0] == 25 && new[] { 5, 2, 4, 1 }[index] == 5 ? 1 : 0, 1, "after duck comes rabbit");

        index = SandModelTie.Pick(new[] { 10, 14 }, Collect(10, 0, 14, 0), Reads(10, 14), Rarity, "10+14=10", true, out key, out advance);
        Expect(index, 1, "cat then cactus");

        index = SandModelTie.Pick(shipBear, Collect(7, 0, 8, 0), both, Rarity, "", false, out key, out advance);
        Expect(index, 1, "toggle off is smallest valid id");
        Expect(advance ? 1 : 0, 0, "toggle off does not arm");

        index = SandModelTie.Pick(new[] { 8 }, Collect(8, 0), Reads(8), Rarity, "7+8=7", true, out key, out advance);
        Expect(index, 0, "lone bear ignores the pair cursor");
        Expect(advance ? 1 : 0, 0, "lone bear does not arm");

        index = SandModelTie.Pick(new[] { 26, 25 }, Collect(), Reads(), Rarity, "", true, out key, out advance);
        Expect(index, 1, "no valid id keeps the smallest decoy index");
        Expect(advance ? 1 : 0, 0, "decoy fallback does not arm");

        index = SandModelTie.Pick(shipBear, Collect(7, 0, 8, 0), Reads(7), Rarity, "", true, out key, out advance);
        Expect(index, 1, "unread bear stays on ship");
        Expect(advance ? 1 : 0, 0, "unread band does not arm");

        ExpectText(SandModelTie.Remember("1+2+4=1", "7+8", 8), "1+2+4=1,7+8=8", "remember inserts in key order");
        ExpectText(SandModelTie.Remember("7+8=7,1+2+4=1", "7+8", 8), "1+2+4=1,7+8=8", "remember replaces one key");
        ExpectText(SandModelTie.Remember("7+8=7", "", 8), "7+8=7", "empty key is a no-op");
        ExpectText(SandModelTie.Remember("junk,7+8=7", "7+8", 8), "7+8=8", "malformed entry is dropped");

        if (failures != 0)
        {
            Console.WriteLine(failures + " failed");
            Environment.Exit(1);
        }
    }
}