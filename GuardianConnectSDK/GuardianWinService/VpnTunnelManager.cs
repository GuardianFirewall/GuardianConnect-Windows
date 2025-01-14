using System;
using GuardianConnect;
using GuardianConnect.Shared;

namespace GuardianWinService;

public class VpnTunnelManager : ITransportProvider
{
    private ITransportProvider.TransportProtocol _protocolType;
    private ITransportProvider.VPNProviderStatus _vpnStatus;
    private ITransportProvider.VPNConnectionError _lastVpnError;
    private DateTime _connectedDate;

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

    public async Task<ErrorResponse> DisconnectVPNTunnel()
    {
        throw new NotImplementedException();
    }

    public async Task<ErrorResponse> StartVPNTunnelWithOptions(Dictionary<string, object> options)
    {
        throw new NotImplementedException();
    }

    public void StopVPNTunnel(string entryName)
    {
        throw new NotImplementedException();
    }

    public void StopVPNTunnel()
    {
        throw new NotImplementedException();
    }

    public ErrorResponse FetchLastDisonnectError()
    {
        throw new NotImplementedException();
    }
}