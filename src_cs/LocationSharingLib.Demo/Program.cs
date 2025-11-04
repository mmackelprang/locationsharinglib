using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LocationSharingLib;

// Demo application for LocationSharingLib
// Usage: dotnet run -- <path-to-cookies.txt> [email@example.com]

if (args.Length == 0)
{
    Console.WriteLine("Usage: LocationSharingLib.Demo <cookies.txt> [authEmail]");
    return;
}
var cookieFile = args[0];
var email = args.Length > 1 ? args[1] : "unknown@gmail.com";

Console.WriteLine("Initializing service...");
var service = new Service(cookieFile, email, maxRetries:5);

Console.WriteLine("Fetching people (shared + self)...");
try
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var people = await service.GetAllPeopleAsync(cts.Token);
    foreach (var p in people)
    {
        Console.WriteLine($"{p.FullName} | {p.Latitude},{p.Longitude} | Battery={p.BatteryLevel}% Charging={p.Charging} Address={p.Address}");
    }

    var targetNickname = "Johnny"; // example
    var coords = await service.GetCoordinatesByNicknameAsync(targetNickname);
    if (coords.Latitude.HasValue)
    {
        Console.WriteLine($"Nickname '{targetNickname}' => {coords.Latitude},{coords.Longitude}");
    }
    else
    {
        Console.WriteLine($"Nickname '{targetNickname}' not found.");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.GetType().Name} - {ex.Message}");
}

Console.WriteLine("Done.");
