using System;
using System.IO;

namespace MoonBooks.Services;

public sealed class AppPaths
{
    public AppPaths()
        : this(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MoonBooks"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MoonBooks"))
    {
    }

    public AppPaths(string appDataDir, string defaultLibraryPath)
    {
        AppDataDir = appDataDir;
        DefaultLibraryPath = defaultLibraryPath;
        DatabasePath = Path.Combine(appDataDir, "moonbooks.db");
        SettingsPath = Path.Combine(appDataDir, "settings.json");
        SecretsDir = Path.Combine(appDataDir, "secrets");
        CoversDir = Path.Combine(appDataDir, "covers");
        CacheDir = Path.Combine(appDataDir, "cache");
        LogsDir = Path.Combine(appDataDir, "logs");
        Directory.CreateDirectory(AppDataDir);
        Directory.CreateDirectory(SecretsDir);
        Directory.CreateDirectory(CoversDir);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(LogsDir);
    }

    public string AppDataDir { get; }
    public string DatabasePath { get; }
    public string SettingsPath { get; }
    public string SecretsDir { get; }
    public string CoversDir { get; }
    public string CacheDir { get; }
    public string LogsDir { get; }
    public string DefaultLibraryPath { get; }
}
