using System;
using GuardianConnect;
using GuardianConnect.Abstractions;
using GuardianConnect.Shared;

namespace GuardianFirewallService;

public class VpnTunnelManager : ITransportProvider
{
    private ITransportProvider.TransportProtocol _protocolType = ITransportProvider.TransportProtocol.TransportIKEv2;
    private ITransportProvider.VPNProviderStatus _vpnStatus = ITransportProvider.VPNProviderStatus.VPNStatusDisconnected;
    private ITransportProvider.VPNConnectionError _lastVpnError = default;
    private DateTime _connectedDate = DateTime.MinValue;

    public ITransportProvider.TransportProtocol ProtocolType => _protocolType;

    public ITransportProvider.VPNProviderStatus VPNStatus => _vpnStatus;

    public ITransportProvider.VPNConnectionError LastVPNError => _lastVpnError;

    public DateTime ConnectedDate => _connectedDate;

    public async Task<(ErrorResponse, bool)> StartVPNTunnelAndReturnError()
    {
        throw new NotImplementedException();
    }

    public Task<ErrorResponse> DisconnectVPNTunnel(string entryName)
    {
        throw new NotImplementedException();
    }

    public ErrorResponse DisconnectVPNTunnel()
    {
        throw new NotImplementedException();
    }

    public async Task<ErrorResponse> StartVPNTunnelWithOptions(VPNCallParameters protocolRequest)
    {
        throw new NotImplementedException();
    }

    public ErrorResponse StopVPNTunnel(string entryName)
    {
        throw new NotImplementedException();
    }

    public ErrorResponse StopVPNTunnel(bool wasDisconnectPlanned = true)
    {
        throw new NotImplementedException();
    }

    public ErrorResponse FetchLastDisonnectError()
    {
        throw new NotImplementedException();
    }
}