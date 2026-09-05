namespace CapitaModern.Core.Loading;

/// <summary>Ищет корень репозитория для консольных прогонов. Только для разработки:
/// в собранной игре данные лежат в .pck, и достаёт их Godot.</summary>
public static class RepoPaths
{
    /// <summary>Идёт вверх от папки с exe, пока не найдёт <c>data</c>: запускается всё
    /// из bin/Debug, а не из корня.</summary>
    public static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "data")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ??
               throw new DirectoryNotFoundException("папка data не найдена");
    }
}
