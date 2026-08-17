namespace CapitaModern.Core.Loading;

public static class RepoPaths
{
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
