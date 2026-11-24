using System.Diagnostics;
using System.Text;

namespace LiveChat;

/// <summary>
/// Journal minimaliste : un fichier par jour dans %APPDATA%\LiveChat\logs, conservé 7 jours.
/// Tant que <see cref="Initialize"/> n'a pas été appelé, les messages ne vont que dans la sortie de débogage.
/// </summary>
public static class Log
{
    private const int RetentionDays = 7;
    private static readonly Lock Gate = new();
    private static string? _file;

    public static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            _file = Path.Combine(AppPaths.LogDirectory, $"livechat-{DateTime.Now:yyyyMMdd}.log");
            PurgeOldFiles();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Journal indisponible : {ex}");
        }
    }

    public static void Info(string message) => Write("INFO ", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WARN ", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" [").Append(level).Append("] ")
            .Append(message);
        if (exception is not null)
        {
            line.AppendLine().Append(exception);
        }

        var text = line.ToString();
        Debug.WriteLine(text);

        if (_file is null)
        {
            return;
        }

        lock (Gate)
        {
            try
            {
                File.AppendAllText(_file, text + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Le journal ne doit jamais faire planter l'application.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void PurgeOldFiles()
    {
        var limit = DateTime.Now.AddDays(-RetentionDays);
        foreach (var file in Directory.EnumerateFiles(AppPaths.LogDirectory, "livechat-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < limit)
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
