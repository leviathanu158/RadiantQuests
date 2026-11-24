// Radiant Quests for Meitou. GPLv3 or later.
// The one difficulty scale every quest type is measured on. A quest type turns its challenge into a score (a fight: Catalog.Fight; later an
// item's rarity, a route's danger) and the fixed lines below turn the score into a rank, so "B" means the same kind of challenge whatever the
// quest is and whatever mods are loaded.
//
// The lines were set from fights known in play (2026-11-22): a goat or starving bandits are E; one naked beak thing or two swamp lurkers are C;
// one leviathan or one great beak thing is S. The scores come from the game data (Catalog: damage^1.5 x toughness^0.3 per fighter, squared,
// times how many you face), so the same numbers work for any load order.

namespace RadiantQuests;

public static class Difficulty
{
    /// <summary>The score where each rank starts: D, C, B, A, S and SS (below the first is E).</summary>
    public static readonly double[] Lines = [200, 1500, 4000, 15000, 40000, 140000];

    /// <summary>The rank (0 E .. 6 SS) of a challenge that scores <paramref name="score"/>.</summary>
    public static int RankOf(double score)
    {
        int rank = 0;
        while (rank < Lines.Length && score >= Lines[rank]) rank++;
        return rank;
    }

    /// <summary>How far into its rank's band <paramref name="score"/> sits, 0 (just made the rank) to 1 (nearly the next); log scale.</summary>
    public static double Within(double score)
    {
        int rank = RankOf(score);
        double lo = rank == 0 ? Lines[0] / 8 : Lines[rank - 1];
        double hi = rank < Lines.Length ? Lines[rank] : Lines[^1] * 4;
        if (score <= lo) return 0;
        if (score >= hi) return 1;
        return Math.Log(score / lo) / Math.Log(hi / lo);
    }
}
