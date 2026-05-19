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

    // public GRDCredential InitWithFullDictionary(Dictionary<string, object> credDict, int validForDays, bool isMain)
    // {
    //     var self = new GRDCredential(credDict);
    //     self.TransportProtocol = GRDTransportProtocol.TransportProtocol.TransportIKEv2;
    //     self.Identifer = isMain ? "main" : Guid.NewGuid().ToString();
    //     self.UserName = (string)credDict[IGRDKeychain.kKeychainStr_EapUsername];
    //     self.Password = (string)credDict[IGRDKeychain.kKeychainStr_EapPassword];
    //     self.ApiAuthToken = (string)credDict[IGRDKeychain.kKeychainStr_AuthToken];
    //     self.HostName = (string)credDict[Common.kGRDHostnameOverride];
    //     self.ExpirationDate = DateTime.Now.AddDays(validForDays);
    //     self.HostnameDisplayValue = (string)credDict[Common.kGRDVPNHostLocation];
    //     self.Name = (string)credDict[Common.kGRDVPNHostLocation];
    //
    //     _checkedExpiration = false;
    //     _checkedExpiration = false;
    //     _expired = false;
    //
    //     self.CheckExpiration();
    //     return self;
    // }
    //
    public GRDCredential InitWithTransportProtocol(GRDTransportProtocol.TransportProtocol protocol,
        Dictionary<string, object> credDict, int validForDays, bool areMainCreds)
    {
        var self = new GRDCredential(credDict);
        self.Name = (string)credDict[Common.kGRDVPNHostLocation];
        self.Identifer = areMainCreds ? "main" : Guid.NewGuid().ToString();
        self.MainCredential = areMainCreds;

        self.ApiAuthToken = (string)credDict[IGRDKeychain.kKeychainStr_AuthToken];
        self.HostName = (string)credDict[Common.kGRDHostnameOverride];
        self.ExpirationDate = DateTime.Now.AddDays(validForDays);
        self.HostnameDisplayValue = (string)credDict[Common.kGRDVPNHostLocation];

        _checkedExpiration = false;
        _expired = false;

        if (protocol == GRDTransportProtocol.TransportProtocol.TransportIKEv2)
        {
            self.UserName = (string)credDict[IGRDKeychain.kKeychainStr_EapUsername];
            self.Password = (string)credDict[IGRDKeychain.kKeychainStr_EapPassword];

            self.ClientId = self.UserName;
        }

        if (protocol == GRDTransportProtocol.TransportProtocol.TransportWireGuard)
        {
            self.DevicePublicKey = (string)credDict[Common.kGRDWGDevicePublicKey];
            self.DevicePrivateKey = (string)credDict[Common.kGRDWGDevicePrivateKey];
            self.ServerPublicKey = (string)credDict[Common.kGRDWGServerPublicKey];
            self.IPv4Address = (string)credDict[Common.kGRDWGIPv4Address];
            self.IPv6Address = (string)credDict[Common.kGRDWGIPv6Address];
            self.ClientId = (string)credDict[Common.kGRDClientId];

            // Per CJ - for backwards compatibility
            self.Password = @"wireguard-creds";
            self.UserName = (string)credDict[Common.kGRDClientId];
        }

        self.CheckExpiration();
        return self;
    }

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