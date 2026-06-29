using System.Security.Cryptography;
using System.Text;
using GuardianConnect.API.Model;

namespace GuardianConnect.API;

/// <summary>
///     This class is responsible for caching geographical information
///     It is instantiated as a pair which after initial load, has one ACTIVE and
///     the second is periodically filled and a checksum comparison determines if
///     newer and atomically a toggle points to the newer and switch is instant
///     for the consumer. This completely runs in background task and consumer's
///     calls reference the static active sister.
/// </summary>
public class GRDRegionCache
{
    internal List<string> RegionKeys = new();
    internal Dictionary<string, string> RegionKeysByDisplay = new();
    internal byte[] Sha = Array.Empty<byte>();
    internal Dictionary<string, List<GRDSGWServer>> _hostLookup = new();
    internal List<string> contentstrings = new();
    internal Dictionary<string, GRDRegion> regionLookup = new();

    internal Dictionary<string, List<string>> timezonesLookup = new();

    internal GRDRegionCache()
    {
        timezonesLookup = new Dictionary<string, List<string>>();
        regionLookup = new Dictionary<string, GRDRegion>();
        _hostLookup = new Dictionary<string, List<GRDSGWServer>>();
        RegionKeys = new List<string>();
        RegionKeysByDisplay = new Dictionary<string, string>();
    }

    public void ComputeHash()
    {
        using var sha256 = SHA256.Create();
        // Concatenate all strings
        var combined = string.Concat(contentstrings);
        var bytes = Encoding.UTF8.GetBytes(combined);
        Sha = sha256.ComputeHash(bytes);
    }

    public uint Checksum()
    {
        uint crc = 0;
        foreach (var b in Sha) crc += b;

        return crc;
    }
}