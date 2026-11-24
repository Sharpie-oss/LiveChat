using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace LiveChat.Services;

public sealed record UpdateInfo(Version Version, string TagName, string ReleaseNotes, string ReleasePageUrl, Uri AssetUrl, Uri? ChecksumUrl);

/// <summary>
/// Mise à jour via les « Releases » GitHub : télécharge l'archive LiveChat-win-x64.zip publiée par la CI,
/// vérifie sa somme SHA-256, remplace les fichiers puis relance l'application.
/// </summary>
public sealed class UpdateService
{
    public const string Owner = "Sharpie-oss";
    public const string Repository = "LiveChat";
    public const string AssetName = "LiveChat-win-x64.zip";
    public const string ExecutableName = "LiveChat.exe";
    private const string BackupExtension = ".old";

    public static Version CurrentVersion { get; } = Normalize(typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0));

    public static string ReleasesPageUrl => $"https://github.com/{Owner}/{Repository}/releases";

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await HttpClients.Shared.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // Aucune release publiée.
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ParseRelease(document.RootElement, CurrentVersion);
    }

    /// <summary>Lit la réponse de l'API GitHub ; renvoie null si la release n'est pas plus récente ou pas exploitable.</summary>
    internal static UpdateInfo? ParseRelease(JsonElement release, Version currentVersion)
    {
        if (IsTrue(release, "draft") || IsTrue(release, "prerelease"))
        {
            return null;
        }

        var tag = release.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
        if (!TryParseVersion(tag, out var version) || version <= currentVersion)
        {
            return null;
        }

        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        Uri? assetUrl = null;
        Uri? checksumUrl = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            if (name is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                continue;
            }

            if (name.Equals(AssetName, StringComparison.OrdinalIgnoreCase))
            {
                assetUrl = uri;
            }
            else if (name.Equals(AssetName + ".sha256", StringComparison.OrdinalIgnoreCase))
            {
                checksumUrl = uri;
            }
        }

        if (assetUrl is null)
        {
            return null;
        }

        var notes = release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        var page = release.TryGetProperty("html_url", out var html) ? html.GetString() ?? ReleasesPageUrl : ReleasesPageUrl;
        return new UpdateInfo(version, tag!, notes, page, assetUrl, checksumUrl);
    }

    /// <summary>Accepte « v2.1.0 », « 2.1 », « 2.1.0-beta »... (le suffixe est ignoré).</summary>
    internal static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var text = tag.Trim().TrimStart('v', 'V');
        var suffix = text.IndexOfAny(['-', '+', ' ']);
        if (suffix >= 0)
        {
            text = text[..suffix];
        }

        if (!Version.TryParse(text.Contains('.') ? text : text + ".0", out var parsed))
        {
            return false;
        }

        version = Normalize(parsed);
        return true;
    }

    /// <summary>Extrait le hash d'un fichier « &lt;sha256&gt;  &lt;nom&gt; » (format sha256sum).</summary>
    internal static string? ParseChecksum(string content)
    {
        var token = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return token is { Length: 64 } && token.All(Uri.IsHexDigit) ? token.ToLowerInvariant() : null;
    }

    public async Task InstallAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        var workDirectory = Path.Combine(Path.GetTempPath(), "LiveChat-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);
        try
        {
            var archive = Path.Combine(workDirectory, AssetName);
            await DownloadAsync(update.AssetUrl, archive, progress, cancellationToken);

            if (update.ChecksumUrl is not null)
            {
                var expected = ParseChecksum(await HttpClients.Shared.GetStringAsync(update.ChecksumUrl, cancellationToken))
                    ?? throw new InvalidDataException("Le fichier de somme de contrôle de la mise à jour est invalide.");
                await using var file = File.OpenRead(archive);
                var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken));
                if (actual != expected)
                {
                    throw new InvalidDataException("La somme de contrôle de la mise à jour ne correspond pas : installation annulée.");
                }
            }
            else
            {
                Log.Warn("Aucune somme de contrôle publiée pour cette release : intégrité non vérifiée.");
            }

            var extracted = Path.Combine(workDirectory, "files");
            ZipFile.ExtractToDirectory(archive, extracted);
            var payload = FindPayloadRoot(extracted)
                ?? throw new InvalidDataException($"L'archive ne contient pas {ExecutableName}.");

            ReplaceFiles(payload, AppPaths.AppDirectory);
            Log.Info($"Mise à jour {update.TagName} installée, redémarrage.");

            Process.Start(new ProcessStartInfo(AppPaths.ExecutablePath, "--updated")
            {
                UseShellExecute = false,
                WorkingDirectory = AppPaths.AppDirectory,
            });
        }
        finally
        {
            TryDeleteDirectory(workDirectory);
        }
    }

    /// <summary>Supprime les fichiers .old laissés par la mise à jour précédente.</summary>
    public static void CleanupPreviousUpdate(string? directory = null)
    {
        directory ??= AppPaths.AppDirectory;
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*" + BackupExtension, SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Encore verrouillé : on réessaiera au prochain démarrage.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Nettoyage de la mise à jour précédente impossible.", ex);
        }
    }

    /// <summary>
    /// Remplace les fichiers de <paramref name="targetDirectory"/>. Windows autorise à renommer un exécutable
    /// en cours d'exécution : l'ancien fichier est renommé en .old puis le nouveau est copié.
    /// En cas d'erreur, tout est remis en place.
    /// </summary>
    internal static void ReplaceFiles(string sourceDirectory, string targetDirectory)
    {
        var backups = new List<(string Target, string Backup)>();
        var created = new List<string>();
        try
        {
            foreach (var source in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(targetDirectory, Path.GetRelativePath(sourceDirectory, source));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                if (File.Exists(target))
                {
                    var backup = target + BackupExtension;
                    if (File.Exists(backup))
                    {
                        File.Delete(backup);
                    }

                    File.Move(target, backup);
                    backups.Add((target, backup));
                }

                File.Copy(source, target);
                created.Add(target);
            }
        }
        catch
        {
            foreach (var file in created)
            {
                TryDelete(file);
            }

            foreach (var (target, backup) in Enumerable.Reverse(backups))
            {
                try
                {
                    File.Move(backup, target, overwrite: true);
                }
                catch (Exception ex)
                {
                    Log.Error($"Restauration impossible de {target}.", ex);
                }
            }

            throw;
        }
    }

    private static async Task DownloadAsync(Uri url, string destination, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var response = await HttpClients.Shared.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destination);
        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            if (total > 0)
            {
                progress?.Report((double)received / total.Value);
            }
        }

        progress?.Report(1);
    }

    /// <summary>Dossier qui contient l'exécutable : racine de l'archive ou unique sous-dossier.</summary>
    private static string? FindPayloadRoot(string directory)
    {
        if (File.Exists(Path.Combine(directory, ExecutableName)))
        {
            return directory;
        }

        var subdirectories = Directory.GetDirectories(directory);
        return subdirectories.Length == 1 && File.Exists(Path.Combine(subdirectories[0], ExecutableName))
            ? subdirectories[0]
            : null;
    }

    private static bool IsTrue(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
