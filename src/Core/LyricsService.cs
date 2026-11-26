using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Island.Core;

public sealed record LyricLine(TimeSpan Time, string Text);

/// <summary>Zeitgenaue Songtexte von lrclib.net (frei, ohne Konto). Gesendet werden nur Titel, Künstler, Album, Länge.</summary>
public static class LyricsService
{
    private static readonly Dictionary<string, List<LyricLine>?> Cache = new();
    private static readonly Queue<string> CacheOrder = new();
    private static readonly Regex LineRegex = new(@"^\[(\d+):(\d+(?:\.\d+)?)\](.*)$", RegexOptions.Compiled);
    private static readonly Regex Noise = new(@"\s*[\(\[](feat\.|ft\.|with |official|lyric|audio|video|remaster|live|explicit)[^\)\]]*[\)\]]|\s+-\s+.*remaster.*$",
                                              RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>null = keine zeitgenauen Lyrics gefunden.</summary>
    public static async Task<List<LyricLine>?> GetAsync(string title, string artist, string album, TimeSpan duration)
    {
        var key = $"{title}|{artist}".ToLowerInvariant();
        if (Cache.TryGetValue(key, out var cached)) return cached;

        // Browser/YouTube: "Künstler - Titel (Official Video)"
        if (string.IsNullOrWhiteSpace(artist) && title.Contains(" - "))
        {
            var parts = title.Split(" - ", 2);
            artist = parts[0];
            title = parts[1];
        }
        var cleanTitle = Noise.Replace(title, "").Trim();
        var cleanArtist = artist.Split(',', '&')[0].Trim();

        List<LyricLine>? result = null;
        if (cleanTitle.Length > 0)
        {
            var url = "https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(cleanTitle) +
                      "&artist_name=" + Uri.EscapeDataString(cleanArtist);
            if (!string.IsNullOrWhiteSpace(album)) url += "&album_name=" + Uri.EscapeDataString(album);
            if (duration > TimeSpan.Zero) url += "&duration=" + (int)Math.Round(duration.TotalSeconds);

            using (var doc = await Net.GetJsonAsync(url))
                result = Parse(Synced(doc?.RootElement));

            if (result == null)
            {
                var search = "https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(cleanTitle) +
                             (cleanArtist.Length > 0 ? "&artist_name=" + Uri.EscapeDataString(cleanArtist) : "");
                using var doc = await Net.GetJsonAsync(search);
                if (doc?.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        result = Parse(Synced(item));
                        if (result != null) break;
                    }
                }
            }
        }

        Cache[key] = result;
        CacheOrder.Enqueue(key);
        while (CacheOrder.Count > 30) Cache.Remove(CacheOrder.Dequeue());
        return result;
    }

    private static string? Synced(JsonElement? e)
    {
        if (e is not { ValueKind: JsonValueKind.Object } el) return null;
        return el.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
    }

    private static List<LyricLine>? Parse(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc)) return null;
        var lines = new List<LyricLine>();
        foreach (var raw in lrc.Split('\n'))
        {
            var m = LineRegex.Match(raw.Trim());
            if (!m.Success) continue;
            var time = TimeSpan.FromMinutes(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
                     + TimeSpan.FromSeconds(double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
            lines.Add(new LyricLine(time, m.Groups[3].Value.Trim()));
        }
        return lines.Count > 0 ? lines.OrderBy(l => l.Time).ToList() : null;
    }

    /// <summary>Index der Zeile, die gerade gesungen wird (-1 = noch vor der ersten).</summary>
    public static int IndexAt(List<LyricLine> lines, TimeSpan position)
    {
        int lo = 0, hi = lines.Count - 1, best = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (lines[mid].Time <= position) { best = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return best;
    }
}
