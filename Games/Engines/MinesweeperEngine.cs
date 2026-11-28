// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
namespace Jotanator;

public enum MineResult
{
    /// <summary>Nothing happened (flagged or already revealed cell).</summary>
    Nothing,
    Safe,
    Boom,
    Win,
}

/// <summary>Minesweeper rules. Mines are placed on the first reveal so the first click is always safe. No Discord code in here.</summary>
public sealed class MineBoard
{
    private readonly bool[] _mine;
    private readonly bool[] _revealed;
    private readonly bool[] _flagged;
    private readonly int[] _adjacent;
    private readonly Random _rng;
    private bool _placed;

    public int Width { get; }
    public int Height { get; }
    public int Mines { get; }
    public bool Exploded { get; private set; }
    public int ExplodedIndex { get; private set; } = -1;

    public MineBoard(int width, int height, int mines, Random? rng = null)
    {
        Width = width;
        Height = height;
        Mines = Math.Min(mines, width * height - 1);
        _rng = rng ?? Random.Shared;
        var n = width * height;
        _mine = new bool[n];
        _revealed = new bool[n];
        _flagged = new bool[n];
        _adjacent = new int[n];
    }

    public int Index(int x, int y) => y * Width + x;

    public bool IsMine(int x, int y) => _mine[Index(x, y)];
    public bool IsRevealed(int x, int y) => _revealed[Index(x, y)];
    public bool IsFlagged(int x, int y) => _flagged[Index(x, y)];
    public int Adjacent(int x, int y) => _adjacent[Index(x, y)];

    public int FlagsLeft => Mines - _flagged.Count(f => f);

    public bool Won => !Exploded && _placed && _revealed.Count(r => r) == Width * Height - Mines;

    public void ToggleFlag(int x, int y)
    {
        var i = Index(x, y);
        if (!_revealed[i]) _flagged[i] = !_flagged[i];
    }

    public MineResult Reveal(int x, int y)
    {
        var i = Index(x, y);
        if (_flagged[i] || _revealed[i]) return MineResult.Nothing;

        if (!_placed) Place(x, y);

        if (_mine[i])
        {
            Exploded = true;
            ExplodedIndex = i;
            return MineResult.Boom;
        }

        Flood(x, y);
        return Won ? MineResult.Win : MineResult.Safe;
    }

    private void Place(int safeX, int safeY)
    {
        // keep the clicked cell and its neighbours clear when there is room, so the first click opens up
        var keepClear = new HashSet<int>();
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var nx = safeX + dx;
                var ny = safeY + dy;
                if (nx >= 0 && nx < Width && ny >= 0 && ny < Height) keepClear.Add(Index(nx, ny));
            }

        if (Width * Height - keepClear.Count < Mines)
        {
            keepClear.Clear();
            keepClear.Add(Index(safeX, safeY));
        }

        var spots = Enumerable.Range(0, Width * Height).Where(i => !keepClear.Contains(i)).OrderBy(_ => _rng.Next()).Take(Mines);
        foreach (var i in spots) _mine[i] = true;

        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
                _adjacent[Index(x, y)] = Neighbours(x, y).Count(n => _mine[n]);

        _placed = true;
    }

    private IEnumerable<int> Neighbours(int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var nx = x + dx;
                var ny = y + dy;
                if (nx >= 0 && nx < Width && ny >= 0 && ny < Height) yield return Index(nx, ny);
            }
    }

    private void Flood(int x, int y)
    {
        var stack = new Stack<int>();
        stack.Push(Index(x, y));

        while (stack.Count > 0)
        {
            var i = stack.Pop();
            if (_revealed[i] || _flagged[i] || _mine[i]) continue;
            _revealed[i] = true;
            if (_adjacent[i] != 0) continue;

            foreach (var n in Neighbours(i % Width, i / Width))
                if (!_revealed[n]) stack.Push(n);
        }
    }
}
