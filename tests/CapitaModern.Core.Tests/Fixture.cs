using CapitaModern.Core.Sim;

namespace CapitaModern.Core.Tests;

/// <summary>Мир из настоящих данных репо; собирается заново на каждый тест, чтобы тесты не мешали друг другу.</summary>
public static class Fixture
{
    private static readonly string Root = FindRoot();

    public static Game NewGame() =>
        Game.Load(path => File.ReadAllText(Path.Combine(Root, path)), path => File.ReadAllBytes(Path.Combine(Root, path)));

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "data", "map"))) dir = dir.Parent;

        return dir?.FullName ?? throw new DirectoryNotFoundException("не найдена папка data/map");
    }
}
