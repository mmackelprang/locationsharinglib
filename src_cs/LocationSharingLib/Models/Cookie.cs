using System.Net;

namespace LocationSharingLib.Models;

public sealed class CookieModel
{
    public string Domain { get; }
    public bool Flag { get; }
    public string Path { get; }
    public bool Secure { get; }
    public long Expiry { get; }
    public string Name { get; }
    public string Value { get; }

    public CookieModel(string domain, bool flag, string path, bool secure, long expiry, string name, string value)
    {
        Domain = domain; Flag = flag; Path = path; Secure = secure; Expiry = expiry; Name = name; Value = value;
    }

    public Cookie ToNetCookie()
    {
        var domain = Domain.StartsWith('.') ? Domain[1..] : Domain; // CookieContainer expects domain without leading dot.
        return new Cookie(Name, Value, Path, domain);
    }
}
