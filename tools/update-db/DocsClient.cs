using System.Net;

namespace Shiftwall.UpdateDb;

/// <summary>
/// Polite HTTP client restricted to official Fortinet hosts. Sends a generic user agent (no personal data)
/// and waits between requests.
/// </summary>
internal sealed class DocsClient : IDisposable
{
    private static readonly string[] AllowedHosts = ["docs.fortinet.com", "www.fortinet.com", "community.fortinet.com", "fortinetweb.s3.amazonaws.com"];
    private readonly HttpClient _http;
    private readonly TimeSpan _delay;
    private DateTime _last = DateTime.MinValue;

    public DocsClient(TimeSpan? delay = null)
    {
        _http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All, AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; Shiftwall-update-db)");
        _delay = delay ?? TimeSpan.FromMilliseconds(700);
    }

    public sealed record Page(Uri FinalUrl, string Html);

    public async Task<Page> GetAsync(string url, CancellationToken ct = default)
    {
        var uri = new Uri(url);
        if (uri.Scheme != Uri.UriSchemeHttps || !AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to fetch non-official URL: {url}");
        }

        for (var attempt = 1; ; attempt++)
        {
            var wait = _last + _delay - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _last = DateTime.UtcNow;
            try
            {
                using var resp = await _http.GetAsync(uri, ct);
                resp.EnsureSuccessStatusCode();
                return new Page(resp.RequestMessage?.RequestUri ?? uri, await resp.Content.ReadAsStringAsync(ct));
            }
            catch (HttpRequestException) when (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(3 * attempt), ct);
            }
        }
    }

    public async Task<int> StatusAsync(string url, CancellationToken ct = default)
    {
        var wait = _last + _delay - DateTime.UtcNow;
        if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
        _last = DateTime.UtcNow;
        try
        {
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            return (int)resp.StatusCode;
        }
        catch (HttpRequestException)
        {
            return 0;
        }
    }

    public void Dispose() => _http.Dispose();
}
