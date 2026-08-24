namespace CapitaModern.Core.Loading;

/// <summary>
/// Поиск корня репозитория для консольных прогонов.
/// </summary>
/// <remarks>
/// Только для разработки. В собранной игре папки <c>data</c> на диске нет — файлы
/// упакованы в .pck, и достаёт их Godot через <c>res://</c>.
/// </remarks>
public static class RepoPaths
{
    /// <summary>
    /// Поднимается вверх от папки с exe, пока не найдёт <c>data</c>: рабочий каталог
    /// у консольного проекта — bin/Debug/net8.0, а не корень репозитория.
    /// </summary>
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
