using System.Net;
using System.Net.Http.Headers;

namespace LiveChat.Services;

/// <summary>Client HTTP partagé (une seule instance pour toute l'application).</summary>
public static class HttpClients
{
    public static HttpClient Shared { get; } = Create();

    /// <summary>Télécharge un fichier en mémoire en refusant ceux qui dépassent <paramref name="maxBytes"/>.</summary>
    public static async Task<byte[]> GetBytesAsync(string url, long maxBytes, CancellationToken cancellationToken)
    {
        var uri = new Uri(url, UriKind.Absolute);

        if (uri.IsFile)
        {
            var info = new FileInfo(uri.LocalPath);
            if (info.Length > maxBytes)
            {
                throw new InvalidDataException($"Fichier trop volumineux ({info.Length / 1024 / 1024} Mo).");
            }

            return await File.ReadAllBytesAsync(uri.LocalPath, cancellationToken);
        }

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            throw new NotSupportedException($"Schéma d'URL non pris en charge : {uri.Scheme}");
        }

        using var buffer = new MemoryStream();
        await DownloadAsync(uri, buffer, maxBytes, progress: null, cancellationToken);
        return buffer.ToArray();
    }

    /// <summary>
    /// Télécharge un fichier sur disque, avec progression (0 à 1) et taille maximale.
    /// En cas d'erreur ou d'annulation, le fichier partiel est supprimé.
    /// </summary>
    public static async Task DownloadToFileAsync(string url, string destination, long maxBytes, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var uri = new Uri(url, UriKind.Absolute);
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            throw new NotSupportedException($"Schéma d'URL non pris en charge : {uri.Scheme}");
        }

        try
        {
            await using var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await DownloadAsync(uri, file, maxBytes, progress, cancellationToken);
        }
        catch
        {
            try
            {
                File.Delete(destination);
            }
            catch (IOException)
            {
            }

            throw;
        }
    }

    private static async Task DownloadAsync(Uri uri, Stream destination, long maxBytes, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var response = await Shared.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        if (total > maxBytes)
        {
            throw new InvalidDataException($"Fichier trop volumineux ({total / 1024 / 1024} Mo).");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var chunk = new byte[81920];
        long received = 0;
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            received += read;
            if (received > maxBytes)
            {
                throw new InvalidDataException("Fichier trop volumineux.");
            }

            await destination.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            if (total > 0)
            {
                progress?.Report((double)received / total.Value);
            }
        }
    }

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        var version = typeof(HttpClients).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LiveChat", version));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/Sharpie-oss/LiveChat)"));
        return client;
    }
}
