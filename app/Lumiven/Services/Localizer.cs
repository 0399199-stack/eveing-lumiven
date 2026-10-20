using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lumiven.Services;

public static class Localizer
{
    private static readonly Dictionary<string, string> English = LoadEnglish();
    private static readonly Dictionary<string, string> Chinese = English.GroupBy(pair => pair.Value, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);
    private static readonly List<TranslationPattern> Patterns = English
        .Where(pair => Regex.IsMatch(pair.Key, @"\{\d+(?:[^{}]*)\}") && HasLiteral(pair.Key) && HasLiteral(pair.Value))
        .OrderByDescending(pair => pair.Key.Length)
        .Select(pair => new TranslationPattern(pair.Key, pair.Value, Pattern(pair.Key), Pattern(pair.Value))).ToList();

    public static string CurrentLanguage { get; private set; } = "zh-CN";
    public static event EventHandler? LanguageChanged;

    public static void SetLanguage(string? language)
    {
        string next = language == "en-US" ? "en-US" : "zh-CN";
        var culture = CultureInfo.GetCultureInfo(next);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        if (CurrentLanguage == next) return;
        CurrentLanguage = next;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string T(string source, params object[] args)
    {
        string text = CurrentLanguage == "en-US" && English.TryGetValue(source, out var translation) ? translation : source;
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }

    // Only use for application-owned status/error messages, never video titles, authors or release notes.
    public static string TranslateExisting(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (English.ContainsKey(text)) return T(text);
        if (Chinese.TryGetValue(text, out var source)) return T(source);
        foreach (var pattern in Patterns)
        {
            var match = pattern.SourcePattern.Match(text);
            if (!match.Success) match = pattern.EnglishPattern.Match(text);
            if (!match.Success) continue;
            var arguments = Enumerable.Range(0, pattern.ArgumentCount).Select(index =>
            {
                string value = match.Groups["p" + index].Value;
                return (object)(Chinese.TryGetValue(value, out var key) ? T(key) : T(value));
            }).ToArray();
            return T(pattern.Source, arguments);
        }
        return text;
    }

    private static Dictionary<string, string> LoadEnglish()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var assembly = typeof(Localizer).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.EndsWith(".en.json", StringComparison.Ordinal)).Order())
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            foreach (var pair in JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!) values[pair.Key] = pair.Value;
        }
        return values;
    }

    private static Regex Pattern(string template)
    {
        var expression = new System.Text.StringBuilder("\\A");
        int offset = 0;
        foreach (Match token in Regex.Matches(template, @"\{(?<index>\d+)(?:[^{}]*)\}"))
        {
            expression.Append(Regex.Escape(template[offset..token.Index]));
            expression.Append("(?<p").Append(token.Groups["index"].Value).Append(">.*?)");
            offset = token.Index + token.Length;
        }
        expression.Append(Regex.Escape(template[offset..])).Append("\\z");
        return new Regex(expression.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    private static bool HasLiteral(string template) => Regex.Replace(template, @"\{\d+(?:[^{}]*)\}", "").Any(char.IsLetterOrDigit);

    private sealed record TranslationPattern(string Source, string English, Regex SourcePattern, Regex EnglishPattern)
    {
        public int ArgumentCount { get; } = Regex.Matches(Source, @"\{(\d+)(?:[^{}]*)\}")
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).DefaultIfEmpty(-1).Max() + 1;
    }
}
