// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
namespace Jotanator;

/// <summary>Minimal argument reader for prefix commands (words, "quoted words", rest-of-line).</summary>
internal sealed class StringView
{
    private readonly string _s;
    private int _i;

    public StringView(string s) => _s = s;

    /// <summary>Current read position (can be rewound).</summary>
    public int Position { get => _i; set => _i = value; }

    public void SkipWhitespace()
    {
        while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
    }

    public string? ReadWord()
    {
        SkipWhitespace();
        if (_i >= _s.Length) return null;

        if (_s[_i] == '"')
        {
            var end = _s.IndexOf('"', _i + 1);
            if (end > 0)
            {
                var quoted = _s.Substring(_i + 1, end - _i - 1);
                _i = end + 1;
                return quoted;
            }
        }

        var start = _i;
        while (_i < _s.Length && !char.IsWhiteSpace(_s[_i])) _i++;
        return _s.Substring(start, _i - start);
    }

    public string ReadRest()
    {
        SkipWhitespace();
        var rest = _s.Substring(_i);
        _i = _s.Length;
        return rest.Trim();
    }
}
