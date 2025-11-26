using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using GuardianConnect.API.Model;

namespace GuardianConnect.API
{
    /// <summary>
    /// This class is responsible for caching geographical information
    /// It is instantiated as a pair which after initial load, has one ACTIVE and
    /// the second is periodically filled and a checksum comparison determines if
    /// newer and atomically a toggle points to the newer and switch is instant
    /// for the consumer. This completely runs in background task and consumer's
    /// calls reference the static active sister.
    /// </summary>
    public class GeoInfoCache
    {
        internal List<string> contentstrings = new();
        internal byte[] Sha = Array.Empty<byte>(); 

        internal List<string> RegionKeys = new();
        internal Dictionary<string, string> RegionKeysByDisplay = new();
        internal Dictionary<string, GRDRegion> regionLookup = new();

        internal Dictionary<string, List<string>> timezonesLookup = new();
        internal Dictionary<string, List<RegionalHostRecord>> _hostLookup = new();

        internal GeoInfoCache()
        {
            timezonesLookup = new();
            regionLookup = new();
            _hostLookup = new();
            RegionKeys = new();
            RegionKeysByDisplay = new();
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
            foreach (var b in Sha)
            {
                crc += b;
            }

            return crc;
        }
    }
}
