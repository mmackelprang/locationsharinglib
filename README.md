# LocationSharingLib (C# Port)

Modern C# port of the Python `locationsharinglib` for programmatic retrieval of Google Maps Location Sharing data (where users have granted sharing). 

> DISCLAIMER: This is not an official Google API. Endpoints or payloads may change or be rate limited / blocked without notice. Use responsibly, respect user privacy, and comply with Google Terms of Service.

---
## Table of Contents
1. Features & Goals
2. Differences From Python Version
3. Creating `cookies.txt` (Required Authentication)
4. Quick Start
5. Advanced Usage (Logging, Retry, Cancellation, Disable Cache)
6. Public API Overview
7. Edge Cases & Error Handling
8. Building, Testing, Packing (NuGet)
9. Roadmap / TODO
10. License

---
## 1. Features & Goals
- Async-first API via `Service` class.
- Parses shared people plus heuristic authenticated account representation.
- Exposes helper lookups (nickname / full name, coordinates, timestamp).
- Defensive parsing of partially missing JSON fields.
- Logging (Serilog) with file rotation.
- Retry with exponential backoff + jitter for transient failures.
- Cancellation token support.

## 2. Differences From Python Version
| Aspect | Python | C# Port |
|--------|--------|---------|
| Caching | `cachetools.TTLCache` | In-memory timestamp + optional disable flag |
| Sync vs Async | Mostly synchronous | All network APIs are async |
| JSON handling | Raw index access & exceptions | `System.Text.Json` with safe fallbacks |
| Logging | Standard logging module | `ILogger` + Serilog (file sink) |
| Retries | None built-in | Transient HTTP + network retries (configurable) |
| Auth Person | Similar heuristic | Same, constructed array skeleton |

## 3. Creating `cookies.txt`
You must supply a Netscape-format cookie file containing at least one of: `__Secure-1PSID` or `__Secure-3PSID` for the Google account that has access to location sharing info.

### 3.1 Steps (Chrome / Chromium based)
1. Log into https://maps.google.com with the target account (ensure Location Sharing is enabled and people are visible).
2. Open DevTools (F12) → Application tab → Storage → Cookies → `https://www.google.com`.
3. Locate cookie rows named `__Secure-1PSID` and/or `__Secure-3PSID` (copy both if present).
4. Create a text file `cookies.txt` with Netscape format lines. Example line structure:
   ```
   .google.com	TRUE	/	TRUE	0	__Secure-1PSID	<your_cookie_value_here>
   .google.com	TRUE	/	TRUE	0	__Secure-3PSID	<your_cookie_value_here>
   ```
   Fields (tab or space separated):
   1. Domain (prefix with dot)  
   2. Include subdomains flag (TRUE/FALSE)  
   3. Path  
   4. Secure (TRUE/FALSE)  
   5. Expiry (UNIX time or 0)  
   6. Name  
   7. Value
5. Save the file somewhere not world-readable (contains session secrets!).
6. Pass the file path to the `Service` constructor.

### 3.2 Security Tips
- Treat this file like a password—store securely.
- Rotate/regenerate cookies periodically (log out / log back in and re-export).
- NEVER commit it to version control.

## 4. Quick Start
```csharp
using LocationSharingLib;

var service = new Service(@"C:\secrets\cookies.txt", "user@example.com");
var people = await service.GetAllPeopleAsync();
foreach (var p in people)
{
    Console.WriteLine($"{p.FullName} => {p.Latitude},{p.Longitude} (battery {p.BatteryLevel}% charging={p.Charging})");
}
```

## 5. Advanced Usage
### 5.1 Logging
Serilog is configured automatically (rolling files under `logs/`). Default template:
```
[timestamp][LVL][LocationSharingLib.Service][message][exception]
```
To customize or integrate into a larger host, provide your own `ILogger<Service>`:
```csharp
using Microsoft.Extensions.Logging;
using LocationSharingLib;

var factory = LoggingConfig.CreateLoggerFactory();
var logger = factory.CreateLogger<Service>();
var svc = new Service(@"C:\cookies.txt", "user@example.com", logger: logger);
```

### 5.2 Retry & Backoff
```csharp
var svc = new Service(@"C:\cookies.txt", "user@example.com", maxRetries:5);
```
Transient HTTP codes (408, 500, 502, 503, 504) + network errors are retried with exponential backoff (base 500ms, capped ~10s, jitter added).

### 5.3 Cancellation
```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
var shared = await svc.GetSharedPeopleAsync(cts.Token);
```

### 5.4 Disabling Cache
```csharp
var noCacheSvc = new Service(@"C:\cookies.txt", "user@example.com", disableCache: true);
```

### 5.5 Selecting Coordinates Quickly
```csharp
var (lat, lng) = await svc.GetCoordinatesByNicknameAsync("Johnny");
```

## 6. Public API Overview (selected)
| Method | Description |
|--------|-------------|
| `GetSharedPeopleAsync()` | Returns list of people sharing their location with the authenticated account. |
| `GetAuthenticatedPersonAsync()` | Heuristic representation of the authenticated account. |
| `GetAllPeopleAsync()` | Combined enumerable of shared + authenticated person. |
| `GetPersonByNicknameAsync(string)` | Case-insensitive nickname lookup. |
| `GetCoordinatesByFullNameAsync(string)` | Returns `(double? Latitude, double? Longitude)` tuple. |
| ... | Additional helpers mirror original Python library. |

## 7. Edge Cases & Error Handling
| Scenario | Exception |
|----------|-----------|
| Cookie file missing/unreadable | `InvalidCookieFileException` |
| Required secure cookies absent | `InvalidCookiesException` |
| Session deemed unauthenticated (heuristic) | `InvalidCookiesException` |
| Malformed / unexpected payload | `InvalidDataException` |
| Transient HTTP/network | Automatically retried (then `InvalidDataException`) |

## 8. Building, Testing, Packing
From repository root (Windows PowerShell):
```powershell
dotnet build .\src_cs\LocationSharingLib\LocationSharingLib.csproj
dotnet test  .\src_cs\LocationSharingLib.Tests\LocationSharingLib.Tests.csproj
dotnet pack  .\src_cs\LocationSharingLib\LocationSharingLib.csproj -c Release -o .\src_cs\nupkg
```
The `.nupkg` will appear in `src_cs/nupkg`. You can then `dotnet add package LocationSharingLib -s src_cs/nupkg` in another project (local source).

## 9. Roadmap / TODO
Short-term:
- [ ] Stronger typed JSON model (schema-based / record types)
- [ ] Option to supply raw cookie values (in-memory) for ephemeral sessions
- [ ] Configurable logging minimum level via constructor parameter
- [ ] Pluggable persistence for cache (e.g., MemoryCache, Redis)

Long-term:
- [ ] Telemetry abstraction & metrics (success/failure counts, latency)
- [ ] Resilience policies via Polly integration
- [ ] Signed & versioned NuGet publishing workflow (CI)
- [ ] Encrypted cookie secret manager integration

## 10. License
MIT (inherits original). See root `LICENSE`.

---
Generated version: `VersionInfo.Version` reads root `.VERSION` (fallback embedded). 

