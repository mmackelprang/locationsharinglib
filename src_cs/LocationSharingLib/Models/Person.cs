using System;
using System.Text.Json;

namespace LocationSharingLib.Models;

public sealed class Person
{
    public string Id { get; private set; } = string.Empty;
    public string? PictureUrl { get; }
    public string? FullName { get; }
    public string? Nickname { get; }
    public double? Latitude { get; }
    public double? Longitude { get; }
    public long? Timestamp { get; }
    public long? Accuracy { get; }
    public string? Address { get; }
    public string? CountryCode { get; }
    public bool? Charging { get; }
    public int? BatteryLevel { get; }

    public DateTimeOffset? DateTimeUtc => Timestamp.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(Timestamp.Value) : null;

    public Person(JsonElement raw)
    {
        // Mirrors Python indexing logic. Defensive access; if shape changes fields become null instead of throwing.
        try
        {
            // data[6][0..3]
            if (raw.GetArrayLength() > 6)
            {
                var six = raw[6];
                if (six.ValueKind == JsonValueKind.Array)
                {
                    Id = six.GetArrayLength() > 0 ? six[0].GetString() ?? string.Empty : string.Empty;
                    PictureUrl = six.GetArrayLength() > 1 ? six[1].GetString() : null;
                    FullName = six.GetArrayLength() > 2 ? six[2].GetString() : null;
                    Nickname = six.GetArrayLength() > 3 ? six[3].GetString() : null;
                }
            }
            Id ??= FullName ?? string.Empty;
            // data[1][1][2] etc
            if (raw.GetArrayLength() > 1)
            {
                var one = raw[1];
                if (one.ValueKind == JsonValueKind.Array && one.GetArrayLength() > 1)
                {
                    var oneOne = one[1];
                    if (oneOne.ValueKind == JsonValueKind.Array && oneOne.GetArrayLength() > 2)
                    {
                        Longitude = oneOne.GetArrayLength() > 1 ? TryGetDouble(oneOne[1]) : null;
                        Latitude = oneOne.GetArrayLength() > 2 ? TryGetDouble(oneOne[2]) : null;
                    }
                    Timestamp = one.GetArrayLength() > 2 ? TryGetLong(one[2]) : null;
                    Accuracy = one.GetArrayLength() > 3 ? TryGetLong(one[3]) : null;
                    Address = one.GetArrayLength() > 4 ? one[4].GetString() : null;
                    CountryCode = one.GetArrayLength() > 6 ? one[6].GetString() : null;
                }
            }
            if (raw.GetArrayLength() > 13)
            {
                var thirteen = raw[13];
                if (thirteen.ValueKind == JsonValueKind.Array)
                {
                    Charging = thirteen.GetArrayLength() > 0 ? TryGetBool(thirteen[0]) : null;
                    BatteryLevel = thirteen.GetArrayLength() > 1 ? TryGetInt(thirteen[1]) : null;
                }
            }
        }
        catch
        {
            // swallow; invalid entries become mostly null
        }
        if (string.IsNullOrEmpty(Id))
        {
            Id = FullName ?? Guid.NewGuid().ToString();
        }
    }

    private static double? TryGetDouble(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Number => el.TryGetDouble(out var d) ? d : null,
        JsonValueKind.String => double.TryParse(el.GetString(), out var d) ? d : null,
        _ => null
    };
    private static long? TryGetLong(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : null,
        JsonValueKind.String => long.TryParse(el.GetString(), out var l) ? l : null,
        _ => null
    };
    private static int? TryGetInt(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Number => el.TryGetInt32(out var i) ? i : null,
        JsonValueKind.String => int.TryParse(el.GetString(), out var i) ? i : null,
        _ => null
    };
    private static bool? TryGetBool(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => bool.TryParse(el.GetString(), out var b) ? b : null,
        _ => null
    };
}
