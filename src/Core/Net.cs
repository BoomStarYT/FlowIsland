using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Island.Core;

/// <summary>
/// Internet nur für freiwillige Extras (Lyrics, Wetter). Kein Konto, keine Tracking-Daten:
/// es wird nur gesendet, was für die Antwort nötig ist (Songtitel/Künstler bzw. Ort).
/// </summary>
internal static class Net
{
    private static readonly HttpClient Http = Create();

    private static HttpClient Create()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("FlowIsland/1.6 (+https://github.com/BoomStarYT/FlowIsland)");
        return c;
    }

    /// <summary>Holt JSON. null bei Fehler oder 404.</summary>
    public static async Task<JsonDocument?> GetJsonAsync(string url)
    {
        try
        {
            using var response = await Http.GetAsync(url).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonDocument.Parse(text);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }
}
