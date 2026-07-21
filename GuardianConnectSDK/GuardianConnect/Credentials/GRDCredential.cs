using System.Text.Json.Serialization;
using GuardianConnect.Abstractions;
using GuardianConnect.Shared;

namespace GuardianConnect.Credentials;

public class GRDCredential
{
    [JsonConstructor]
    public GRDCredential()
    {
    }

    public GRDCredential(Dictionary<string, object> credDict)
    {
        foreach (var key in credDict.Keys)
            switch (key)
            {
                case "Name":
                    Name = (string)credDict[key];
                    break;
                case "Identifier":
                    Identifer = (string)credDict[key];
                    break;
                case "MainCredential":
                    MainCredential = (bool)credDict[key];
                    break;
                case "HostnameDisplayValue":
                    HostnameDisplayValue = (string)credDict[key];
                    break;
                case "ClientId":
                    ClientId = (string)credDict[key];
                    break;
                case "ApiAuthToken":
                    ApiAuthToken = (string)credDict[key];
                    break;
                case "UserName":
                    UserName = (string)credDict[key];
                    break;
                case "Password":
                    Password = (string)credDict[key];
                    break;
                case "PasswordRef":
                    PasswordRef = (byte[])credDict[key];
                    break;
                case "DevicePrivateKey":
                    DevicePrivateKey = (string)credDict[key];
                    break;
                case "DevicePublicKey":
                    DevicePublicKey = (string)credDict[key];
                    break;
                case "ServerPublicKey":
                    ServerPublicKey = (string)credDict[key];
                    break;
                case "IPv4Address":
                    IPv4Address = (string)credDict[key];
                    break;
                case "IPv6Address":
                    IPv6Address = (string)credDict[key];
                    break;
            }
    }

    public GRDCredential(Dictionary<string, object> credDict, string hostname, DateTime expirationDate)
    {
        var self = new GRDCredential(credDict);
        self.TransportProtocol = GRDTransportProtocol.TransportProtocol.TransportIKEv2;
        HostName = hostname;
        ExpirationDate = expirationDate;
        _checkedExpiration = false;
        _expired = false;
    }

    public DateTime LastUpdated { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Identifer { get; set; } = string.Empty;
    public bool MainCredential { get; set; }

    //[JsonIgnore]
    public GRDTransportProtocol.TransportProtocol TransportProtocol { get; set; }
    public string HostnameDisplayValue { get; set; } = string.Empty;
    public DateTime ExpirationDate { get; set; }
    public string HostName { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("api-auth-token")] public string ApiAuthToken { get; set; } = string.Empty;

    [JsonPropertyName("eap-username")] public string UserName { get; set; } = string.Empty;

    [JsonPropertyName("eap-password")] public string Password { get; set; } = string.Empty;

    [JsonIgnore] public byte[] PasswordRef { get; set; } = Array.Empty<byte>();
    public string DevicePublicKey { get; set; } = string.Empty;
    public string DevicePrivateKey { get; set; } = string.Empty;
    public string ServerPublicKey { get; set; } = string.Empty;
    public string IPv4Address { get; set; } = string.Empty;
    public string IPv6Address { get; set; } = string.Empty;

    /// <summary>
    /// The host's <c>POST /api/v1.4/device-credentials</c> reply, carried verbatim. This is
    /// the authoritative source for the server-provided fields; the flat fields
    /// above (UserName/Password/ServerPublicKey/IPv4Address/IPv6Address/ClientId/
    /// ApiAuthToken) are kept in sync for persistence back-compat and for the
    /// out-of-process consumer, but new code should pluck from <c>Device</c>.
    /// Null only on a legacy credential loaded from a pre-Device persisted blob;
    /// <see cref="EnsureDeviceFromLegacyFields"/> backfills it on load.
    /// </summary>
    public VPNDeviceResponse? Device { get; set; }

    public bool CanRevoke()
    {
        throw new NotImplementedException();
    }

    public string DefaultFileName()
    {
        throw new NotImplementedException();
    }

    public string GetAuthTokenIdentifer()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Single, protocol-parameterized credential factory. Carries the host's
    /// <paramref name="device"/> reply verbatim and fills the flat fields that
    /// the active protocol uses (kept in sync for persistence/consumer
    /// back-compat). For WireGuard the device keypair is generated client-side
    /// and passed in (it is NOT part of the host reply).
    ///
    /// No cross-protocol field-stuffing: a WireGuard credential leaves
    /// UserName/Password empty, so the IKEv2 predicate in
    /// <c>ActiveConnectionPossible</c> is false on it by construction. This
    /// replaces the per-protocol inline build sites and the former
    /// (dead, stuffing) InitWithTransportProtocol. Mirrors the Android SDK's
    /// <c>createGRDCredential</c>.
    /// </summary>
    public static GRDCredential CreateFromDeviceResponse(
        GRDTransportProtocol.TransportProtocol protocol,
        VPNDeviceResponse device,
        string hostName,
        string hostnameDisplayValue,
        bool mainCredential,
        int validForDays,
        string? devicePrivateKey = null,
        string? devicePublicKey = null)
    {
        var self = new GRDCredential
        {
            TransportProtocol    = protocol,
            Device               = device,
            Identifer            = mainCredential ? "main" : Guid.NewGuid().ToString(),
            MainCredential       = mainCredential,
            HostName             = hostName,
            HostnameDisplayValue = hostnameDisplayValue,
            Name                 = hostnameDisplayValue,
            ExpirationDate       = DateTime.UtcNow.AddDays(validForDays),
            ApiAuthToken         = device.ApiAuthToken ?? string.Empty,
        };

        switch (protocol)
        {
            case GRDTransportProtocol.TransportProtocol.TransportIKEv2:
                self.UserName = device.EapUsername ?? string.Empty;
                self.Password = device.EapPassword ?? string.Empty;
                self.ClientId = device.EapUsername ?? string.Empty; // IKEv2: clientId == EAP user
                break;

            case GRDTransportProtocol.TransportProtocol.TransportWireGuard:
                self.ServerPublicKey  = device.ServerPublicKey ?? string.Empty;
                self.IPv4Address      = device.MappedIPv4Address ?? string.Empty;
                self.IPv6Address      = device.MappedIPv6Address ?? string.Empty;
                self.ClientId         = device.ClientId ?? string.Empty;
                self.DevicePrivateKey = devicePrivateKey ?? string.Empty;
                self.DevicePublicKey  = devicePublicKey ?? string.Empty;
                // No UserName/Password stuffing — see factory remarks.
                break;
        }

        self.CheckExpiration();
        return self;
    }

    /// <summary>
    /// Back-compat for credentials loaded from a pre-<see cref="Device"/>
    /// persisted blob (only the flat fields are populated). Reconstructs the
    /// <see cref="Device"/> DTO from the flat fields relevant to this
    /// credential's <see cref="TransportProtocol"/>, so usage points can read
    /// from <c>Device</c> uniformly. No-op when <c>Device</c> is already set.
    ///
    /// Protocol-scoped on purpose: a legacy WireGuard credential has its
    /// UserName/Password stuffed ("wireguard-creds"/clientId) for old back-compat
    /// — we must NOT surface those as Device.EapUsername/EapPassword, or the
    /// IKEv2 predicate would wrongly pass on a WG cred.
    /// </summary>
    public void EnsureDeviceFromLegacyFields()
    {
        if (Device is not null) return;

        var d = new VPNDeviceResponse { ApiAuthToken = NullIfEmpty(ApiAuthToken) };
        switch (TransportProtocol)
        {
            case GRDTransportProtocol.TransportProtocol.TransportIKEv2:
                d.EapUsername = NullIfEmpty(UserName);
                d.EapPassword = NullIfEmpty(Password);
                d.ClientId    = NullIfEmpty(ClientId);
                break;
            case GRDTransportProtocol.TransportProtocol.TransportWireGuard:
                d.ServerPublicKey   = NullIfEmpty(ServerPublicKey);
                d.MappedIPv4Address = NullIfEmpty(IPv4Address);
                d.MappedIPv6Address = NullIfEmpty(IPv6Address);
                d.ClientId          = NullIfEmpty(ClientId);
                break;
        }

        Device = d;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private void CheckExpiration()
    {
        if (ExpirationDate < DateTime.Now)
        {
            _checkedExpiration = true;
            _expired = true;
        }
    }

    private int DaysLeft()
    {
        return (ExpirationDate - DateTime.Now).Days;
    }
#pragma warning disable CS0414
    [JsonIgnore] private bool _checkedExpiration;
    [JsonIgnore] private bool _expired;
#pragma warning restore CS0414
}