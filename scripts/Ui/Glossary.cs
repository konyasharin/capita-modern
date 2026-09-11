using System.Text.Json;
using Godot;

/// <summary>Описание одной величины или термина.</summary>
public sealed record Article(string Title, string Text);

/// <summary>Что показывает панель и что значат слова в подсказках.</summary>
/// <remarks>
/// Тексты лежат в data/ui/glossary.json, а не в коде: их правят чаще, чем логику, и
/// править их должно быть можно без пересборки.
/// </remarks>
public sealed class Glossary
{
    private const string Path = "res://data/ui/glossary.json";

    private readonly Dictionary<string, Article> _metrics;
    private readonly Dictionary<string, Article> _terms;

    public static Glossary Load()
    {
        var json = Godot.FileAccess.GetFileAsString(Path);
        if (string.IsNullOrEmpty(json))
        {
            throw new InvalidDataException($"не читается {Path}: {Godot.FileAccess.GetOpenError()}");
        }

        using var doc = JsonDocument.Parse(json);

        return new Glossary(Read(doc.RootElement, "metrics"), Read(doc.RootElement, "terms"));
    }

    private Glossary(Dictionary<string, Article> metrics, Dictionary<string, Article> terms)
    {
        _metrics = metrics;
        _terms = terms;
    }

    public Article? Metric(string key) => _metrics.GetValueOrDefault(key);

    public Article? Term(string key) => _terms.GetValueOrDefault(key);

    /// <summary>Статья по ключу: сперва среди величин, потом среди терминов.</summary>
    public Article? Any(string key) => Metric(key) ?? Term(key);

    private static Dictionary<string, Article> Read(JsonElement root, string section)
    {
        var result = new Dictionary<string, Article>();

        foreach (var item in root.GetProperty(section).EnumerateObject())
        {
            result[item.Name] = new Article(
                item.Value.GetProperty("title").GetString() ?? item.Name,
                item.Value.GetProperty("text").GetString() ?? string.Empty);
        }

        return result;
    }
}
