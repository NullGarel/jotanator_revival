// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
namespace Jotanator;

/// <summary>A playing card. Rank 1 = ace ... 13 = king; suit 0-3. No Discord code in here.</summary>
public readonly record struct Card(int Rank, int Suit)
{
    private static readonly string[] Suits = ["♠", "♥", "♦", "♣"];

    public string RankText => Rank switch { 1 => "A", 11 => "J", 12 => "Q", 13 => "K", _ => Rank.ToString() };

    public override string ToString() => RankText + Suits[Suit];
}

public sealed class Deck
{
    private readonly Random _rng;
    private readonly Stack<Card> _cards = new();

    public Deck(Random? rng = null)
    {
        _rng = rng ?? Random.Shared;
        Refill();
    }

    private void Refill()
    {
        var all = new List<Card>();
        for (var suit = 0; suit < 4; suit++)
            for (var rank = 1; rank <= 13; rank++)
                all.Add(new Card(rank, suit));

        foreach (var card in all.OrderBy(_ => _rng.Next())) _cards.Push(card);
    }

    public Card Draw()
    {
        if (_cards.Count == 0) Refill();
        return _cards.Pop();
    }
}

/// <summary>Blackjack hand maths.</summary>
public static class Blackjack
{
    /// <summary>Best total (aces count 11 while that doesn't bust) and whether an ace is still counted as 11.</summary>
    public static (int Total, bool Soft) Value(IEnumerable<Card> cards)
    {
        var total = 0;
        var aces = 0;
        foreach (var c in cards)
        {
            if (c.Rank == 1) { aces++; total += 11; }
            else total += Math.Min(c.Rank, 10);
        }

        while (total > 21 && aces > 0)
        {
            total -= 10;
            aces--;
        }

        return (total, aces > 0);
    }

    public static bool IsBlackjack(IReadOnlyCollection<Card> cards) => cards.Count == 2 && Value(cards).Total == 21;

    public static bool IsBust(IEnumerable<Card> cards) => Value(cards).Total > 21;

    public static string Show(IEnumerable<Card> cards) => string.Join(" ", cards.Select(c => $"`{c}`"));
}
