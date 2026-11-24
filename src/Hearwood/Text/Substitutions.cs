using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Hearwood.Text;

/// <summary>"Say <see cref="From"/> as <see cref="To"/>": a word the voice gets wrong, and what to give it instead.</summary>
public sealed record SubstitutionRule(string From, string To, bool MatchCase = false);

/// <summary>
/// The user's word substitutions, applied to what is spoken (never to the text shown). Ported from another TTS app by
/// the same author, and stored in the ListenEasy file format, so lists move between the apps:
/// <code>{"format": "listeneasy-substitutions", "version": 1,
///  "substitutions": {"XAML": "zammel", "MTA": {"to": "em-ti-ey", "case": true}}}</code>
/// A plain string value matches whatever the case; <c>{"case": true}</c> matches only the exact case. A rule matches
/// whole words only where its own ends are letters or digits, so "ached" leaves "reached" alone while ".NET" still
/// matches in "ASP.NET". All rules apply in one pass, longest first, so a replacement is never substituted again.
/// </summary>
public sealed class Substitutions
{
    public const string Format = "listeneasy-substitutions";

    public static Substitutions Empty { get; } = new([]);

    /// <summary>%APPDATA%\Hearwood\substitutions.json.</summary>
    public static string PathOnDisk => Path.Combine(Config.Folder, "substitutions.json");

    static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Longest first; an exact-case rule before its caseless twin. Group <c>r{i}</c> of the pattern is rule i.</summary>
    readonly SubstitutionRule[] _ordered;
    readonly Regex? _pattern;

    public Substitutions(IEnumerable<SubstitutionRule> rules)
    {
        Rules = rules.Where(r => r.From.Length > 0).ToList();
        _ordered = [.. Rules.OrderByDescending(r => r.From.Length).ThenBy(r => r.MatchCase ? 0 : 1)];
        if (_ordered.Length == 0)
            return;
        var parts = _ordered.Select((r, i) =>
        {
            var body = Pattern(r.From);
            if (!r.MatchCase)
                body = $"(?i:{body})";
            var before = IsWordChar(r.From[0]) ? @"(?<!\w)" : "";
            var after = IsWordChar(r.From[^1]) ? @"(?!\w)" : "";
            return $"(?<r{i}>{before}{body}{after})";
        });
        _pattern = new Regex(string.Join('|', parts), RegexOptions.CultureInvariant);
    }

    /// <summary>The rules in the user's order.</summary>
    public IReadOnlyList<SubstitutionRule> Rules { get; }

    public string Apply(string text) => _pattern is null ? text : _pattern.Replace(text, m => Replace(m, text));

    string Replace(Match m, string text)
    {
        int i = 0;
        while (!m.Groups[$"r{i}"].Success)
            i++;
        var rule = _ordered[i];
        string to = rule.To, found = m.Value;
        // A caseless rule keeps a leading capital ("Hegemony" -> "Hedgemony"), but not all caps ("XAML").
        if (!rule.MatchCase && to.Length > 0 && char.IsUpper(found[0]) && !IsAllCaps(found) && char.IsLower(to[0]))
            to = char.ToUpperInvariant(to[0]) + to[1..];
        // A rule starting or ending in punctuation can sit against a word: "ASP.NET" -> "ASP dot net".
        if (to.Length > 0 && char.IsLetterOrDigit(to[0]) && m.Index > 0 && char.IsLetterOrDigit(text[m.Index - 1]))
            to = " " + to;
        int end = m.Index + m.Length;
        if (to.Length > 0 && char.IsLetterOrDigit(to[^1]) && end < text.Length && char.IsLetterOrDigit(text[end]))
            to += " ";
        return to;
    }

    /// <summary>The rule's text as a pattern in which a straight quote also matches a curly one, however the text was typed.</summary>
    static string Pattern(string from) => Regex.Escape(Straighten(from))
        .Replace("'", "['‘’]")
        .Replace("\"", "[\"“”]");

    static string Straighten(string s) => s.Replace('‘', '\'').Replace('’', '\'').Replace('“', '"').Replace('”', '"');

    static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    static bool IsAllCaps(string s) => s.Any(char.IsLetter) && !s.Any(char.IsLower);

    /// <summary>
    /// The word starting at <paramref name="start"/>, as a rule would name it: up to the next space, without the quotes
    /// or brackets around it or the punctuation after it ("“XAML,”" gives "XAML"; ".NET" and "ASP.NET" stay whole).
    /// </summary>
    public static string WordAt(string text, int start)
    {
        int end = start;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
            end++;
        return text[start..end].Trim(Enclosing).TrimEnd(Trailing).Trim(Enclosing);
    }

    static readonly char[] Enclosing = ['"', '\'', '“', '”', '‘', '’', '(', ')', '[', ']', '{', '}', '<', '>'];
    static readonly char[] Trailing = ['.', ',', ';', ':', '!', '?', '…'];

    /// <summary>The saved substitutions; none when there is no file. Throws when the file can't be read.</summary>
    public static Substitutions Load(string? path = null)
    {
        path ??= PathOnDisk;
        return File.Exists(path) ? new Substitutions(Read(path)) : Empty;
    }

    /// <summary>The rules in a ListenEasy substitutions file. Throws <see cref="InvalidDataException"/> for any other file.</summary>
    public static IReadOnlyList<SubstitutionRule> Read(string path)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(File.ReadAllText(path)); }
        catch (JsonException e) { throw new InvalidDataException("It isn't a substitutions file: " + e.Message, e); }

        if (root is not JsonObject obj
            || (obj["format"] is { } format && (format.GetValueKind() != JsonValueKind.String || format.GetValue<string>() != Format))
            || obj["substitutions"] is not JsonObject subs)
            throw new InvalidDataException("It isn't a substitutions file.");

        var rules = new List<SubstitutionRule>();
        foreach (var (word, value) in subs)
        {
            var rule = value switch
            {
                JsonValue v when v.GetValueKind() == JsonValueKind.String => new SubstitutionRule(word, v.GetValue<string>()),
                JsonObject o when o["to"] is JsonValue to && to.GetValueKind() == JsonValueKind.String =>
                    new SubstitutionRule(word, to.GetValue<string>(), o["case"] is JsonValue c && c.GetValueKind() == JsonValueKind.True),
                _ => throw new InvalidDataException($"The entry for \"{word}\" can't be read."),
            };
            if (word.Length > 0)
                rules.Add(rule);
        }
        return rules;
    }

    /// <summary>Writes <paramref name="rules"/> as a ListenEasy substitutions file, replacing the old file only once the new one is complete.</summary>
    public static void Write(string path, IEnumerable<SubstitutionRule> rules)
    {
        var subs = new JsonObject();
        foreach (var r in rules)
            subs[r.From] = r.MatchCase ? new JsonObject { ["to"] = r.To, ["case"] = true } : JsonValue.Create(r.To);
        var root = new JsonObject { ["format"] = Format, ["version"] = 1, ["substitutions"] = subs };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, root.ToJsonString(WriteOptions) + "\n");
        File.Move(temp, path, overwrite: true);
    }
}
