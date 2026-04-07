using GuardianConnect.Abstractions;
using GuardianConnect.Shared;

namespace GuardianFirewallService;

public class VpnTunnelManager : ITransportProvider
{
    public ITransportProvider.TransportProtocol ProtocolType { get; } =
        ITransportProvider.TransportProtocol.TransportIKEv2;

    public ITransportProvider.VPNProviderStatus VPNStatus { get; } =
        ITransportProvider.VPNProviderStatus.VPNStatusDisconnected;

    public ITransportProvider.VPNConnectionError LastVPNError { get; } = default;

    public DateTime ConnectedDate { get; } = DateTime.MinValue;

    public async Task<(ErrorResponse, bool)> StartVPNTunnelAndReturnError()
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

    public ErrorResponse StopVPNTunnel(bool wasDisconnectPlanned = true)
    {
        throw new NotImplementedException();
    }

    public ErrorResponse FetchLastDisonnectError()
    {
        throw new NotImplementedException();
    }

    public Task<ErrorResponse> DisconnectVPNTunnel(string entryName)
    {
        throw new NotImplementedException();
    }

    public ErrorResponse StopVPNTunnel(string entryName)
    {
        throw new NotImplementedException();
    }
}