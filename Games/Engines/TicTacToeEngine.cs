// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
namespace Jotanator;

/// <summary>Tic tac toe rules and AI. X moves first. No Discord code in here.</summary>
public sealed class TttBoard
{
    public const char X = 'X';
    public const char O = 'O';

    private static readonly int[][] Lines =
    [
        [0, 1, 2], [3, 4, 5], [6, 7, 8],   // rows
        [0, 3, 6], [1, 4, 7], [2, 5, 8],   // columns
        [0, 4, 8], [2, 4, 6],              // diagonals
    ];

    public char[] Cells { get; } = Enumerable.Repeat(' ', 9).ToArray();

    public bool IsFull => !Cells.Contains(' ');

    public List<int> Empty() => Enumerable.Range(0, 9).Where(i => Cells[i] == ' ').ToList();

    public (char Who, int[]? Line) Winner() => Winner(Cells);

    private static (char Who, int[]? Line) Winner(char[] b)
    {
        foreach (var l in Lines)
            if (b[l[0]] != ' ' && b[l[0]] == b[l[1]] && b[l[1]] == b[l[2]])
                return (b[l[0]], l);
        return (' ', null);
    }

    /// <summary>A cell that completes a line for `who` (two in a row + one empty), if any.</summary>
    public int? LineMove(char who)
    {
        foreach (var l in Lines)
        {
            var mine = l.Count(i => Cells[i] == who);
            var free = l.Where(i => Cells[i] == ' ').ToList();
            if (mine == 2 && free.Count == 1) return free[0];
        }
        return null;
    }

    /// <summary>The computer's move for `me`: easy = random, medium = win/block/random, hard = unbeatable.</summary>
    public int PickMove(char me, string difficulty, Random? rng = null)
    {
        rng ??= Random.Shared;
        var empty = Empty();
        var other = me == X ? O : X;

        switch (difficulty)
        {
            case "hard":
                return BestMove(me, empty, rng);
            case "medium":
                var move = LineMove(me) ?? LineMove(other);
                if (move != null) return move.Value;
                break;
        }

        return empty[rng.Next(empty.Count)];
    }

    /// <summary>Minimax: picks randomly among the moves with the best score.</summary>
    private int BestMove(char me, List<int> empty, Random rng)
    {
        var other = me == X ? O : X;
        var best = int.MinValue;
        var candidates = new List<int>();

        foreach (var i in empty)
        {
            Cells[i] = me;
            var score = Score(Cells, me, other, myTurn: false, depth: 1);
            Cells[i] = ' ';

            if (score > best) { best = score; candidates.Clear(); }
            if (score == best) candidates.Add(i);
        }

        return candidates[rng.Next(candidates.Count)];
    }

    private static int Score(char[] b, char me, char other, bool myTurn, int depth)
    {
        var (winner, _) = Winner(b);
        if (winner == me) return 10 - depth;
        if (winner == other) return depth - 10;
        if (!b.Contains(' ')) return 0;

        var best = myTurn ? int.MinValue : int.MaxValue;
        for (var i = 0; i < 9; i++)
        {
            if (b[i] != ' ') continue;
            b[i] = myTurn ? me : other;
            var score = Score(b, me, other, !myTurn, depth + 1);
            b[i] = ' ';
            best = myTurn ? Math.Max(best, score) : Math.Min(best, score);
        }
        return best;
    }
}
