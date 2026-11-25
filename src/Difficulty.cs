// Radiant Quests for Meitou. GPLv3 or later.
// The one difficulty scale every quest type is measured on. A quest type turns its challenge into a score (a fight: Catalog.Fight; later an
// item's rarity, a route's danger) and the fixed lines below turn the score into a rank, so "B" means the same kind of challenge whatever the
// quest is and whatever mods are loaded.
//
// The lines were set from fights known in play (2026-11-22): a goat or starving bandits are E; one naked beak thing or two swamp lurkers are C;
// one leviathan or one great beak thing is S. The scores come from the game data (Catalog: damage^1.5 x toughness^0.3 per fighter, squared,
// times how many you face, numbers weighing a little more than one each: Catalog.CountWeight), so the same numbers work for any load order.
//
// Within a rank (his idea, 2026-11-23): the band is split in thirds, shown as Easy, Medium and Hard, and the pay follows where in the band a
// quest sits (a hard D pays near the top of D's range).

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
    public static double Within(double score) => Within(score, RankOf(score));

    /// <summary>How far into rank <paramref name="rank"/>'s band <paramref name="score"/> sits (0 below it, 1 above it).</summary>
    public static double Within(double score, int rank)
    {
        rank = Math.Clamp(rank, 0, Lines.Length);
        double lo = rank == 0 ? Lines[0] / 8 : Lines[rank - 1];
        double hi = rank < Lines.Length ? Lines[rank] : Lines[^1] * 4;
        if (score <= lo) return 0;
        if (score >= hi) return 1;
        return Math.Log(score / lo) / Math.Log(hi / lo);
    }

    /// <summary>
    /// How hard a quest is within its rank, in words (his choice, 2026-11-23: no numbers): the band in fifths, Very Easy, Easy, Medium, Hard,
    /// Very Hard. Empty when the score is unknown (0: a quest from an older version).
    /// </summary>
    public static string Band(double score, int rank)
    {
        if (score <= 0) return "";
        double w = Within(score, rank);
        return w < 0.2 ? "Very Easy" : w < 0.4 ? "Easy" : w < 0.6 ? "Medium" : w < 0.8 ? "Hard" : "Very Hard";
    }

    /// <summary>
    /// The cats a quest of <paramref name="rank"/> scoring <paramref name="score"/> pays before the spread: the rank's range, low end for the
    /// easiest, high end for the hardest. A score past the band keeps rising with the next rank's range (a group bigger than planned pays more).
    /// </summary>
    public static double CatsAt(double score, int rank)
    {
        if (score <= 0) return Ranks.Cats(rank).Min * 0.5 + Ranks.Cats(rank).Max * 0.5;
        int r = Math.Max(rank, RankOf(score));
        var (lo, hi) = Ranks.Cats(r);
        return lo + Within(score, r) * (hi - lo);
    }

    /// <summary>
    /// The guild rep a quest pays before the spread: the rank's rep, from 85% (very easy) to 115% (very hard), so ranks never overlap (the
    /// easiest D gives more than the hardest E); past the band, as above.
    /// </summary>
    public static double RepAt(double score, int rank)
    {
        if (score <= 0) return Ranks.Rep(rank);
        int r = Math.Max(rank, RankOf(score));
        return Ranks.Rep(r) * (0.85 + 0.3 * Within(score, r));
    }

    /// <summary>
    /// The score whose pay is <paramref name="cats"/> (the inverse of <see cref="CatsAt"/>): item quests are ranked by what their goods are
    /// worth (his rule, 2026-11-24). A value between two ranks' ranges takes the lower rank's top; below E's, E's bottom.
    /// </summary>
    public static double ScoreForCats(double cats)
    {
        for (int r = 0; r <= Ranks.Top; r++)
        {
            var (lo, hi) = Ranks.Cats(r);
            if (cats > hi && r < Ranks.Top && cats >= Ranks.Cats(r + 1).Min) continue;
            double w = Math.Clamp((cats - lo) / Math.Max(1.0, hi - lo), 0, 0.999);
            double blo = r == 0 ? Lines[0] / 8 : Lines[r - 1];
            double bhi = r < Lines.Length ? Lines[r] : Lines[^1] * 4;
            return Math.Exp(Math.Log(blo) + w * (Math.Log(bhi) - Math.Log(blo)));
        }
        return Lines[^1] * 4 * 0.999;
    }

    /// <summary>The board's pay for an offer: by its difficulty, with a small random spread that never leaves the rank's own range.</summary>
    public static (long Cats, int Rep) Pay(double score, int rank, double spreadA, double spreadB)
    {
        var (lo, hi) = Ranks.Cats(rank);
        double cats = Math.Clamp(CatsAt(score, rank) * (0.93 + 0.14 * spreadA), lo, hi);
        double rep = Math.Clamp(RepAt(score, rank) * (0.95 + 0.1 * spreadB), Ranks.Rep(rank) * 0.85, Ranks.Rep(rank) * 1.15);
        return ((long)Math.Round(cats / 10) * 10, (int)Math.Round(rep));
    }
}
