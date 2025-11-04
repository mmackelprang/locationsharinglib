using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LocationSharingLib.Exceptions;
using LocationSharingLib.Models;
using Microsoft.Extensions.Logging;

namespace LocationSharingLib;

public sealed class Service
{
    private static readonly HashSet<string> ValidCookieNames = new(StringComparer.Ordinal)
    {"__Secure-1PSID","__Secure-3PSID"};
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private const string Url = "https://www.google.com/maps/rpc/locationsharing/read";

    private readonly HttpClient _httpClient;
    private readonly string _email;
    private DateTime _lastFetchUtc = DateTime.MinValue;
    private JsonElement _cachedRoot; // default empty
    private readonly ILogger<Service> _logger;
    private readonly int _maxRetries;

    private readonly bool _disableCache;

    public Service(string cookiesFilePath,
                   string authenticatingAccount = "unknown@gmail.com",
                   HttpClient? httpClient = null,
                   int maxRetries = 3,
                   ILogger<Service>? logger = null,
                   bool disableCache = false)
    {
        _email = authenticatingAccount;
        _httpClient = httpClient ?? BuildHttpClientFromCookieFile(cookiesFilePath);
        _maxRetries = maxRetries < 1 ? 1 : maxRetries;
        _logger = logger ?? LoggingConfig.CreateLoggerFactory().CreateLogger<Service>();
        _disableCache = disableCache;
        _logger.LogInformation("Service initialization starting for {Email}", _email);
        ValidateSessionAsync().GetAwaiter().GetResult(); // sync init
        _logger.LogInformation("Service initialization completed for {Email}", _email);
    }

    private static HttpClient BuildHttpClientFromCookieFile(string path)
    {
        if (!File.Exists(path)) throw new InvalidCookieFileException("Cookie file not found or unreadable.");
        var content = File.ReadAllText(path);
        var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
        var cookies = ParseCookieFile(content).ToList();
        if (!cookies.Any(c => ValidCookieNames.Contains(c.Name)))
            throw new InvalidCookiesException($"Missing required cookies: {string.Join(",", ValidCookieNames)}");
        foreach (var cookie in cookies)
        {
            try { handler.CookieContainer.Add(new Uri("https://google.com"), cookie.ToNetCookie()); } catch { /* ignore invalid */ }
        }
        return new HttpClient(handler, disposeHandler: true);
    }

    private static IEnumerable<CookieModel> ParseCookieFile(string content)
    {
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#')) continue;
            var parts = trimmed.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 7) // Attempt space split fallback
                parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 7) continue; // malformed
            var domain = parts[0];
            var flag = parts[1] == "TRUE";
            var path = parts[2];
            var secure = parts[3] == "TRUE";
            _ = long.TryParse(parts[4], out var expiry);
            var name = parts[5];
            var value = parts[6];
            yield return new CookieModel(domain, flag, path, secure, expiry, name, value);
        }
    }

    private async Task<JsonElement> FetchAsync(CancellationToken ct = default)
    {
        if (!_disableCache && DateTime.UtcNow - _lastFetchUtc < CacheTtl && _cachedRoot.ValueKind != JsonValueKind.Undefined)
        {
            _logger.LogDebug("Using cached location data");
            return _cachedRoot;
        }
        var url = Url + "?authuser=2&hl=en&gl=us&pb=!1m7!8m6!1m3!1i14!2i8413!3i5385!2i6!3x4095!2m3!1e0!2sm!3i407105169!3m7!2sen!5e1105!12m4!1e68!2m2!1sset!2sRoadmap!4e1!5m4!1e4!8m2!1e0!1e1!6m9!1e12!2i2!26m1!4b1!30m1!1f1.3953487873077393!39b1!44e1!50e0!23i4111425";
        int attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                _logger.LogDebug("Fetching location data attempt {Attempt}", attempt);
                using var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
                var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    if (IsTransient(response.StatusCode) && attempt < _maxRetries)
                    {
                        _logger.LogWarning("Transient HTTP {Status} on attempt {Attempt}, will retry", (int)response.StatusCode, attempt);
                        await BackoffDelayAsync(attempt, ct).ConfigureAwait(false);
                        continue;
                    }
                    _logger.LogError("Non-success HTTP {Status}: {Body}", (int)response.StatusCode, Truncate(text));
                    throw new Exceptions.InvalidDataException($"Server returned {(int)response.StatusCode}: {text}");
                }
                var payload = ExtractJsonPayload(text);
                var root = JsonSerializer.Deserialize<JsonElement>(payload);
                _cachedRoot = root;
                _lastFetchUtc = DateTime.UtcNow;
                _logger.LogDebug("Successfully fetched and parsed location data");
                return root;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && attempt < _maxRetries)
            {
                // treat as transient timeout
                _logger.LogWarning("Operation canceled (timeout) attempt {Attempt}, retrying", attempt);
                await BackoffDelayAsync(attempt, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (attempt < _maxRetries)
            {
                _logger.LogWarning(ex, "HttpRequestException attempt {Attempt}, retrying", attempt);
                await BackoffDelayAsync(attempt, ct).ConfigureAwait(false);
            }
        }
    }

    private static bool IsTransient(HttpStatusCode status) => status switch
    {
        HttpStatusCode.RequestTimeout => true, // 408
        HttpStatusCode.InternalServerError => true, //500
        HttpStatusCode.BadGateway => true, //502
        HttpStatusCode.ServiceUnavailable => true, //503
        HttpStatusCode.GatewayTimeout => true, //504
        _ => false
    };

    private static async Task BackoffDelayAsync(int attempt, CancellationToken ct)
    {
        var baseDelay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));
        var delay = baseDelay + jitter;
        if (delay > TimeSpan.FromSeconds(10)) delay = TimeSpan.FromSeconds(10);
        await Task.Delay(delay, ct).ConfigureAwait(false);
    }

    private static string Truncate(string text, int max = 500)
        => text.Length <= max ? text : text[..max];

    private static string ExtractJsonPayload(string raw)
    {
        // Python logic: data.split("'",1)[1]
        var idx = raw.IndexOf('\'', StringComparison.Ordinal);
        if (idx >= 0 && idx + 1 < raw.Length)
            return raw[(idx + 1)..];
        return raw; // fallback
    }

    private async Task ValidateSessionAsync()
    {
        var root = await FetchAsync();
    if (root.ValueKind != JsonValueKind.Array) throw new Exceptions.InvalidDataException("Unexpected JSON structure.");
        if (root.GetArrayLength() > 6)
        {
            var authField = root[6];
            if (authField.ValueKind == JsonValueKind.String && authField.GetString() == "GgA=")
                throw new InvalidCookiesException("Session not authenticated (auth heuristic matched).");
        }
    }

    private async Task<JsonElement> GetDataAsync(CancellationToken ct = default) => await FetchAsync(ct);

    public async Task<IReadOnlyList<Person>> GetSharedPeopleAsync(CancellationToken ct = default)
    {
        var root = await GetDataAsync(ct);
        var list = new List<Person>();
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return list;
        var sharedEntries = root[0];
        if (sharedEntries.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in sharedEntries.EnumerateArray())
            {
                try { list.Add(new Person(entry)); }
                catch (Exception ex) { _logger.LogDebug(ex, "Skipping invalid person entry"); }
            }
        }
        return list;
    }

    public async Task<Person?> GetAuthenticatedPersonAsync(CancellationToken ct = default)
    {
        try
        {
            var root = await GetDataAsync(ct);
            // Build synthetic array similar to Python logic
            var fullName = _email;
            var avatar = root.GetArrayLength() > 9 && root[9].ValueKind == JsonValueKind.Array && root[9][1].ValueKind == JsonValueKind.String ? root[9][1].GetString() : null;
            var skeleton = new object?[]
            {
                _email, // placeholder
                new object?[]{null, new object?[]{null,null,null,null}, null, null, null, null, null}, // minimal structure for indexing [1]
                null,null,null,null,null,
                new object?[]{_email, avatar, fullName, fullName}, // index 6
                null,null,null,null,null,null,null
            };
            var json = JsonSerializer.Serialize(skeleton);
            var element = JsonSerializer.Deserialize<JsonElement>(json);
            return new Person(element);
        }
        catch
        {
            return null;
        }
    }

    public async Task<IEnumerable<Person>> GetAllPeopleAsync(CancellationToken ct = default)
    {
        var shared = await GetSharedPeopleAsync(ct);
        var auth = await GetAuthenticatedPersonAsync(ct);
        return auth is not null ? shared.Concat(new[]{auth}) : shared;
    }

    public async Task<Person?> GetPersonByNicknameAsync(string nickname, CancellationToken ct = default)
        => (await GetAllPeopleAsync(ct)).FirstOrDefault(p => string.Equals(p.Nickname, nickname, StringComparison.OrdinalIgnoreCase));

    public async Task<(double? Latitude,double? Longitude)> GetCoordinatesByNicknameAsync(string nickname, CancellationToken ct = default)
    {
        var p = await GetPersonByNicknameAsync(nickname, ct); return p is null ? (null,null) : (p.Latitude, p.Longitude);
    }

    public async Task<double?> GetLatitudeByNicknameAsync(string nickname, CancellationToken ct = default)
        => (await GetPersonByNicknameAsync(nickname, ct))?.Latitude;

    public async Task<double?> GetLongitudeByNicknameAsync(string nickname, CancellationToken ct = default)
        => (await GetPersonByNicknameAsync(nickname, ct))?.Longitude;

    public async Task<long?> GetTimestampByNicknameAsync(string nickname, CancellationToken ct = default)
        => (await GetPersonByNicknameAsync(nickname, ct))?.Timestamp;

    public async Task<Person?> GetPersonByFullNameAsync(string fullName, CancellationToken ct = default)
        => (await GetAllPeopleAsync(ct)).FirstOrDefault(p => string.Equals(p.FullName, fullName, StringComparison.OrdinalIgnoreCase));

    public async Task<(double? Latitude,double? Longitude)> GetCoordinatesByFullNameAsync(string fullName, CancellationToken ct = default)
    {
        var p = await GetPersonByFullNameAsync(fullName, ct); return p is null ? (null,null) : (p.Latitude, p.Longitude);
    }

    public async Task<double?> GetLatitudeByFullNameAsync(string fullName, CancellationToken ct = default)
        => (await GetPersonByFullNameAsync(fullName, ct))?.Latitude;

    public async Task<double?> GetLongitudeByFullNameAsync(string fullName, CancellationToken ct = default)
        => (await GetPersonByFullNameAsync(fullName, ct))?.Longitude;

    public async Task<long?> GetTimestampByFullNameAsync(string fullName, CancellationToken ct = default)
        => (await GetPersonByFullNameAsync(fullName, ct))?.Timestamp;
}
