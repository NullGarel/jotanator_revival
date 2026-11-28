// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
namespace Jotanator;

/// <summary>Hangman rules. Words are lowercase a-z only (no accents). No Discord code in here.</summary>
public sealed class HangmanGame
{
    public const int MaxWrong = 6;

    private readonly HashSet<char> _guessed = new();

    public string Word { get; }
    public int Wrong { get; private set; }

    public HangmanGame(string word) => Word = word.ToLowerInvariant();

    public bool Has(char letter) => _guessed.Contains(letter);

    public IEnumerable<char> WrongLetters => _guessed.Where(c => !Word.Contains(c)).OrderBy(c => c);

    public bool Won => Word.All(_guessed.Contains);
    public bool Lost => Wrong >= MaxWrong;
    public bool Over => Won || Lost;

    /// <summary>Guesses a letter. Returns true if it is in the word. Repeated guesses change nothing.</summary>
    public bool Guess(char letter)
    {
        letter = char.ToLowerInvariant(letter);
        if (letter is < 'a' or > 'z' || !_guessed.Add(letter)) return Word.Contains(letter);

        if (Word.Contains(letter)) return true;
        Wrong++;
        return false;
    }

    /// <summary>The word with unguessed letters hidden, e.g. "_ a n g _ a n". Shows everything when `reveal`.</summary>
    public string Mask(bool reveal = false) =>
        string.Join(" ", Word.Select(c => reveal || _guessed.Contains(c) ? char.ToUpperInvariant(c).ToString() : "_"));

    private static readonly string[] Stages =
    [
        "  +---+\n  |   |\n      |\n      |\n      |\n      |\n=========",
        "  +---+\n  |   |\n  O   |\n      |\n      |\n      |\n=========",
        "  +---+\n  |   |\n  O   |\n  |   |\n      |\n      |\n=========",
        "  +---+\n  |   |\n  O   |\n /|   |\n      |\n      |\n=========",
        "  +---+\n  |   |\n  O   |\n /|\\  |\n      |\n      |\n=========",
        "  +---+\n  |   |\n  O   |\n /|\\  |\n /    |\n      |\n=========",
        "  +---+\n  |   |\n  O   |\n /|\\  |\n / \\  |\n      |\n=========",
    ];

    public string Gallows() => Stages[Math.Clamp(Wrong, 0, MaxWrong)];

    // ------------------------------------------------------------------ words

    public static string RandomWord(string language, Random? rng = null)
    {
        var list = language == "pt" ? Portuguese : English;
        return list[(rng ?? Random.Shared).Next(list.Length)];
    }

    public static readonly string[] English =
    [
        "anchor", "balcony", "bicycle", "blanket", "blizzard", "bridge", "butterfly", "cactus", "camera", "candle",
        "captain", "carpet", "castle", "chimney", "circus", "compass", "cookie", "crystal", "curtain", "dolphin",
        "dragon", "eclipse", "elephant", "engine", "factory", "feather", "fireplace", "forest", "fountain", "galaxy",
        "garden", "giraffe", "glacier", "guitar", "hammer", "harvest", "helmet", "horizon", "island", "jacket",
        "jungle", "kangaroo", "kitchen", "ladder", "lantern", "library", "lighthouse", "magnet", "marble", "meadow",
        "mirror", "monkey", "mountain", "mushroom", "napkin", "notebook", "ocean", "orange", "orchestra", "oxygen",
        "paddle", "parrot", "peanut", "penguin", "pillow", "pirate", "planet", "pocket", "pumpkin", "puzzle",
        "rainbow", "rocket", "saddle", "sandwich", "scissors", "shadow", "silver", "skeleton", "snowman", "spider",
        "squirrel", "station", "storm", "sunflower", "teacher", "thunder", "tiger", "tomato", "tractor", "treasure",
        "trumpet", "tunnel", "turtle", "umbrella", "unicorn", "valley", "vampire", "violin", "volcano", "waffle",
        "whistle", "window", "winter", "wizard", "wolf", "yogurt", "zebra", "zipper", "keyboard", "internet",
    ];

    public static readonly string[] Portuguese =
    [
        "abacaxi", "aranha", "armario", "balao", "banana", "barco", "biblioteca", "bicicleta", "borboleta", "cachorro",
        "cadeira", "camisa", "caminhao", "caneta", "carteira", "castelo", "cavalo", "cenoura", "chocolate", "cobertor",
        "coelho", "computador", "coracao", "cozinha", "crocodilo", "dinossauro", "elefante", "escada", "escola", "espelho",
        "estrela", "fantasma", "farinha", "floresta", "foguete", "fogueira", "formiga", "garrafa", "girafa", "guitarra",
        "helicoptero", "ilha", "janela", "jardim", "jornal", "laranja", "lanterna", "leao", "livro", "macaco",
        "martelo", "montanha", "morango", "motocicleta", "navio", "nuvem", "oceano", "onibus", "oculos", "padaria",
        "panela", "papagaio", "pelicano", "pinguim", "piscina", "planeta", "porta", "praia", "pulseira", "queijo",
        "quadrado", "relogio", "robo", "sanduiche", "sapato", "serpente", "sorvete", "sombrinha", "tartaruga", "telefone",
        "tesoura", "tesouro", "tigre", "tomate", "trator", "trovao", "unicornio", "vassoura", "vulcao", "violao",
        "zebra", "abelha", "arvore", "bruxa", "cachoeira", "caderno", "cadeado", "campeonato", "dragao", "esquilo",
        "feijoada", "futebol", "gaiola", "jacare", "lagarto", "mochila", "pipoca", "tapete", "teclado", "universo",
    ];
}
