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

        using var response = await Shared.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength > maxBytes)
        {
            throw new InvalidDataException("Fichier trop volumineux.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new InvalidDataException("Fichier trop volumineux.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
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
