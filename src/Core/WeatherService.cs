using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Island.Core;

public sealed record HourForecast(DateTime Time, double Temp, int Code, bool IsDay);
public sealed record DayForecast(DateTime Date, double Max, double Min, int Code, int RainChance);

public sealed class WeatherData
{
    public double Temp { get; init; }
    public double Feels { get; init; }
    public int Code { get; init; }
    public bool IsDay { get; init; }
    public double Wind { get; init; }
    public List<HourForecast> Hours { get; init; } = new();
    public List<DayForecast> Days { get; init; } = new();
    public DateTime Fetched { get; init; }
}

/// <summary>
/// Wetter von open-meteo.com (frei, ohne Konto, ohne Schlüssel). Gesendet wird nur der Ort bzw. seine Koordinaten.
/// Aktualisiert alle 30 Minuten, nur wenn das Wetter-Widget benutzt wird.
/// </summary>
public static class WeatherService
{
    public static WeatherData? Current { get; private set; }
    public static string? Error { get; private set; }
    public static event Action? Updated;

    private static DispatcherTimer? _timer;
    private static Settings? _settings;
    private static bool _busy;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static bool Configured(Settings s) => s.WetterLat != null || !string.IsNullOrWhiteSpace(s.WetterOrt);

    /// <summary>Startet die regelmässige Aktualisierung (mehrfach aufrufen ist ok).</summary>
    public static void Start(Settings settings)
    {
        _settings = settings;
        if (_timer == null)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
            _timer.Tick += (_, _) => _ = RefreshAsync();
            _timer.Start();
        }
        if (Current == null || DateTime.Now - Current.Fetched > TimeSpan.FromMinutes(25)) _ = RefreshAsync();
    }

    public static async Task RefreshAsync()
    {
        var s = _settings;
        if (s == null || _busy || !Configured(s)) return;
        _busy = true;
        try
        {
            if (s.WetterLat == null || s.WetterLon == null)
            {
                var place = await GeocodeAsync(s.WetterOrt);
                if (place == null) { Error = "Ort nicht gefunden"; Updated?.Invoke(); return; }
                s.WetterLat = place.Value.Lat;
                s.WetterLon = place.Value.Lon;
                s.WetterOrtName = place.Value.Name;
                s.SaveSoon();
            }

            var url = "https://api.open-meteo.com/v1/forecast?latitude=" + s.WetterLat.Value.ToString("0.###", Inv) +
                      "&longitude=" + s.WetterLon!.Value.ToString("0.###", Inv) +
                      "&current=temperature_2m,apparent_temperature,weather_code,is_day,wind_speed_10m" +
                      "&hourly=temperature_2m,weather_code,is_day" +
                      "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max" +
                      "&timezone=auto&forecast_days=4";
            using var doc = await Net.GetJsonAsync(url);
            if (doc == null) { Error = "Keine Verbindung"; Updated?.Invoke(); return; }

            Current = Parse(doc.RootElement);
            Error = null;
            Updated?.Invoke();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            Error = "Wetter nicht verfügbar";
            Updated?.Invoke();
        }
        finally { _busy = false; }
    }

    private static WeatherData Parse(JsonElement root)
    {
        var cur = root.GetProperty("current");
        var hourly = root.GetProperty("hourly");
        var daily = root.GetProperty("daily");

        var hours = new List<HourForecast>();
        var times = hourly.GetProperty("time").EnumerateArray().Select(t => DateTime.Parse(t.GetString()!, Inv)).ToList();
        var temps = hourly.GetProperty("temperature_2m").EnumerateArray().Select(Num).ToList();
        var codes = hourly.GetProperty("weather_code").EnumerateArray().Select(e => (int)Num(e)).ToList();
        var days = hourly.GetProperty("is_day").EnumerateArray().Select(e => Num(e) > 0).ToList();
        var nowHour = DateTime.Parse(cur.GetProperty("time").GetString()!, Inv);
        for (int i = 0; i < times.Count; i++)
            if (times[i] > nowHour) hours.Add(new HourForecast(times[i], temps[i], codes[i], days[i]));

        var dayList = new List<DayForecast>();
        var dTimes = daily.GetProperty("time").EnumerateArray().Select(t => DateTime.Parse(t.GetString()!, Inv)).ToList();
        var dMax = daily.GetProperty("temperature_2m_max").EnumerateArray().Select(Num).ToList();
        var dMin = daily.GetProperty("temperature_2m_min").EnumerateArray().Select(Num).ToList();
        var dCode = daily.GetProperty("weather_code").EnumerateArray().Select(e => (int)Num(e)).ToList();
        var dRain = daily.GetProperty("precipitation_probability_max").EnumerateArray().Select(e => (int)Num(e)).ToList();
        for (int i = 0; i < dTimes.Count; i++) dayList.Add(new DayForecast(dTimes[i], dMax[i], dMin[i], dCode[i], dRain[i]));

        return new WeatherData
        {
            Temp = Num(cur.GetProperty("temperature_2m")),
            Feels = Num(cur.GetProperty("apparent_temperature")),
            Code = (int)Num(cur.GetProperty("weather_code")),
            IsDay = Num(cur.GetProperty("is_day")) > 0,
            Wind = Num(cur.GetProperty("wind_speed_10m")),
            Hours = hours.Take(12).ToList(),
            Days = dayList,
            Fetched = DateTime.Now,
        };
    }

    private static double Num(JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : 0;

    /// <summary>Ort → Koordinaten (open-meteo Geocoding).</summary>
    public static async Task<(double Lat, double Lon, string Name)?> GeocodeAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        using var doc = await Net.GetJsonAsync("https://geocoding-api.open-meteo.com/v1/search?count=1&language=de&format=json&name="
                                               + Uri.EscapeDataString(query.Trim()));
        if (doc == null || !doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0) return null;
        var r = results[0];
        var name = r.GetProperty("name").GetString() ?? query;
        if (r.TryGetProperty("country", out var country)) name += ", " + country.GetString();
        return (r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(), name);
    }

    /// <summary>Ort neu setzen (aus den Einstellungen) und gleich laden.</summary>
    public static async Task<bool> SetPlaceAsync(Settings s, string query)
    {
        var place = await GeocodeAsync(query);
        if (place == null) return false;
        s.WetterOrt = query.Trim();
        s.WetterLat = place.Value.Lat;
        s.WetterLon = place.Value.Lon;
        s.WetterOrtName = place.Value.Name;
        s.NotifyChanged();
        _settings = s;
        Current = null;
        await RefreshAsync();
        return true;
    }

    public static string Describe(int code) => code switch
    {
        0 => "Klar",
        1 => "Meist klar",
        2 => "Teils bewölkt",
        3 => "Bedeckt",
        45 or 48 => "Nebel",
        51 or 53 or 55 => "Nieselregen",
        56 or 57 => "Gefrierender Niesel",
        61 or 63 => "Regen",
        65 => "Starker Regen",
        66 or 67 => "Eisregen",
        71 or 73 => "Schnee",
        75 => "Starker Schnee",
        77 => "Schneegriesel",
        80 or 81 => "Regenschauer",
        82 => "Heftige Schauer",
        85 or 86 => "Schneeschauer",
        95 => "Gewitter",
        96 or 99 => "Gewitter mit Hagel",
        _ => "Wetter",
    };
}
