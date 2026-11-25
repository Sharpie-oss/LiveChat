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
    private static string? _directory;
    private static string? _file;
    private static DateTime _fileDate;

    /// <summary>Horloge (remplaçable par les tests).</summary>
    internal static Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    public static void Initialize() => Initialize(AppPaths.LogDirectory);

    internal static void Initialize(string? directory)
    {
        lock (Gate)
        {
            _directory = null;
            _file = null;
            if (directory is null)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(directory);
                _directory = directory;
                OpenFileFor(Clock());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Journal indisponible : {ex}");
            }
        }
    }

    public static void Info(string message) => Write("INFO ", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WARN ", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        var now = Clock();
        var line = new StringBuilder()
            .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" [").Append(level).Append("] ")
            .Append(message);
        if (exception is not null)
        {
            line.AppendLine().Append(exception);
        }

        var text = line.ToString();
        Debug.WriteLine(text);

        lock (Gate)
        {
            if (_directory is null)
            {
                return;
            }

            try
            {
                // LiveChat reste souvent ouvert plusieurs jours : on change de fichier à minuit
                // (avant, tout s'écrivait dans le fichier du jour de lancement, sans jamais être purgé).
                if (now.Date != _fileDate)
                {
                    OpenFileFor(now);
                }

                File.AppendAllText(_file!, text + Environment.NewLine, Encoding.UTF8);
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

    private static void OpenFileFor(DateTime now)
    {
        _fileDate = now.Date;
        _file = Path.Combine(_directory!, $"livechat-{now:yyyyMMdd}.log");
        PurgeOldFiles(now);
    }

    private static void PurgeOldFiles(DateTime now)
    {
        var limit = now.AddDays(-RetentionDays);
        foreach (var file in Directory.EnumerateFiles(_directory!, "livechat-*.log"))
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
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
