namespace ETLWorker.Infrastructure.Common;

/// <summary>Las rutas relativas del JSON se resuelven contra la carpeta del ejecutable (sin rutas C:\ fijas).</summary>
public static class PathResolver
{
    public static string Resolve(string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
}
