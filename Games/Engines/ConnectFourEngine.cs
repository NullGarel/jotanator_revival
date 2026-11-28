// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
namespace Jotanator;

/// <summary>Connect Four rules and AI. Players are 1 and 2; row 0 is the top. No Discord code in here.</summary>
public sealed class C4Board
{
    public const int Cols = 7;
    public const int Rows = 6;

    private static readonly int[] ColumnOrder = [3, 2, 4, 1, 5, 0, 6];   // centre first = better pruning
    private static readonly (int dr, int dc)[] Directions = [(0, 1), (1, 0), (1, 1), (1, -1)];

    /// <summary>Index = row * Cols + col.</summary>
    public int[] Cells { get; } = new int[Cols * Rows];

    public bool CanDrop(int col) => col is >= 0 and < Cols && Cells[col] == 0;

    public bool IsFull => Enumerable.Range(0, Cols).All(c => !CanDrop(c));

    public IEnumerable<int> ValidColumns => Enumerable.Range(0, Cols).Where(CanDrop);

    /// <summary>Drops a piece; returns the row it landed in, or -1 if the column is full.</summary>
    public int Drop(int col, int player)
    {
        if (!CanDrop(col)) return -1;
        for (var row = Rows - 1; row >= 0; row--)
        {
            if (Cells[row * Cols + col] != 0) continue;
            Cells[row * Cols + col] = player;
            return row;
        }
        return -1;
    }

    /// <summary>Removes the top piece of a column (for the AI's search).</summary>
    private void Undo(int col)
    {
        for (var row = 0; row < Rows; row++)
        {
            if (Cells[row * Cols + col] == 0) continue;
            Cells[row * Cols + col] = 0;
            return;
        }
    }

    /// <summary>The four winning cells through (row, col) if the piece there completes a line.</summary>
    public int[]? WinLineAt(int row, int col)
    {
        var player = Cells[row * Cols + col];
        if (player == 0) return null;

        foreach (var (dr, dc) in Directions)
        {
            var line = new List<int> { row * Cols + col };
            for (var sign = -1; sign <= 1; sign += 2)
            {
                var r = row + dr * sign;
                var c = col + dc * sign;
                while (r is >= 0 and < Rows && c is >= 0 and < Cols && Cells[r * Cols + c] == player)
                {
                    line.Add(r * Cols + c);
                    r += dr * sign;
                    c += dc * sign;
                }
            }
            if (line.Count >= 4) return line.ToArray();
        }
        return null;
    }

    // ------------------------------------------------------------------ AI

    /// <summary>The computer's column for `me`: easy = random, medium = win/block/random, hard = lookahead.</summary>
    public int PickMove(int me, string difficulty, Random? rng = null)
    {
        rng ??= Random.Shared;
        var valid = ValidColumns.ToList();
        var other = 3 - me;

        switch (difficulty)
        {
            case "hard":
                return BestMove(me, 5, rng);
            case "medium":
                foreach (var who in new[] { me, other })
                {
                    foreach (var col in valid)
                    {
                        var row = Drop(col, who);
                        var wins = WinLineAt(row, col) != null;
                        Undo(col);
                        if (wins) return col;
                    }
                }
                // otherwise lean towards the middle
                return valid.OrderBy(c => Math.Abs(c - 3) + rng.NextDouble() * 2.5).First();
        }

        return valid[rng.Next(valid.Count)];
    }

    private int BestMove(int me, int depth, Random rng)
    {
        var best = int.MinValue;
        var candidates = new List<int>();

        foreach (var col in ColumnOrder.Where(CanDrop))
        {
            var row = Drop(col, me);
            int score;
            if (WinLineAt(row, col) != null) score = 1_000_000;
            else score = -Negamax(depth - 1, -1_000_001, 1_000_001, 3 - me);
            Undo(col);

            if (score > best) { best = score; candidates.Clear(); }
            if (score == best) candidates.Add(col);
        }

        return candidates[rng.Next(candidates.Count)];
    }

    /// <summary>Score from the point of view of `player`, who is about to move.</summary>
    private int Negamax(int depth, int alpha, int beta, int player)
    {
        if (IsFull) return 0;
        if (depth == 0) return Evaluate(player);

        var best = int.MinValue + 1;
        foreach (var col in ColumnOrder)
        {
            if (!CanDrop(col)) continue;

            var row = Drop(col, player);
            var score = WinLineAt(row, col) != null
                ? 1_000_000 + depth
                : -Negamax(depth - 1, -beta, -alpha, 3 - player);
            Undo(col);

            if (score > best) best = score;
            if (best > alpha) alpha = best;
            if (alpha >= beta) break;
        }
        return best;
    }

    /// <summary>Heuristic: every window of four cells counts for whoever is closer to filling it.</summary>
    private int Evaluate(int me)
    {
        var other = 3 - me;
        var score = 0;

        for (var row = 0; row < Rows; row++)
            if (Cells[row * Cols + 3] == me) score += 3;

        for (var row = 0; row < Rows; row++)
        {
            for (var col = 0; col < Cols; col++)
            {
                foreach (var (dr, dc) in Directions)
                {
                    var endRow = row + dr * 3;
                    var endCol = col + dc * 3;
                    if (endRow is < 0 or >= Rows || endCol is < 0 or >= Cols) continue;

                    int mine = 0, theirs = 0;
                    for (var k = 0; k < 4; k++)
                    {
                        var v = Cells[(row + dr * k) * Cols + col + dc * k];
                        if (v == me) mine++;
                        else if (v == other) theirs++;
                    }

                    if (theirs == 0) score += mine switch { 3 => 50, 2 => 5, _ => 0 };
                    else if (mine == 0) score -= theirs switch { 3 => 60, 2 => 4, _ => 0 };
                }
            }
        }
        return score;
    }
}
