using GuardianConnect.Shared;

namespace GuardianConnect.Abstractions;

public interface ITransportProvider
{
    // TransportProtocol enum moved to GRDTransportProtocol.cs (still in
    // this Abstractions assembly) as part of the consolidation that put
    // the registry Get/Set helpers and the enum into one canonical
    // location. Property type below is now
    // GRDTransportProtocol.TransportProtocol; on-disk ordinal values are
    // unchanged so credential serialization stays wire-compatible.
    public GRDTransportProtocol.TransportProtocol ProtocolType { get; }
    public VPNProviderStatus VPNStatus { get; }
    public VPNConnectionError LastVPNError { get; }

    /*!
     * @property connectedDate
     * @discussion The date and time when the connection status changed to VPNStatusConnected.
     * This property is nil if the connection is not fully established.
     */
    public DateTime ConnectedDate { get; }

    public static bool IsConnected { get; private set; }

    #region CommonEnumerations

    public enum VPNProviderStatus
    {
        /*! @const NEVPNStatusInvalid The VPN is not configured. */
        VPNStatusInvalid = 0,

        /*! @const NEVPNStatusDisconnected The VPN is disconnected. */
        VPNStatusDisconnected = 1,

        /*! @const NEVPNStatusConnecting The VPN is connecting. */
        VPNStatusConnecting = 2,

        /*! @const NEVPNStatusConnected The VPN is connected. */
        VPNStatusConnected = 3,

        /*! @const NEVPNStatusReasserting The VPN is reconnecting following loss of underlying network connectivity. */
        VPNStatusReasserting = 4,

        /*! @const NEVPNStatusDisconnecting The VPN is disconnecting. */
        VPNStatusDisconnecting = 5
    }

    public enum VPNConnectionError
    {
        /*! @const NEVPNConnectionErrorOverslept The VPN connection was terminated because the system slept for an extended period of time. */
        VPNConnectionErrorOverslept = 1,

        /*! @const NEVPNConnectionErrorNoNetworkAvailable The VPN connection could not be established because the system is not connected to a network. */
        VPNConnectionErrorNoNetworkAvailable = 2,

        /*! @const NEVPNConnectionErrorUnrecoverableNetworkChange The VPN connection was terminated because the network conditions changed in such a
         * way that the VPN connection could not be maintained. */
        VPNConnectionErrorUnrecoverableNetworkChange = 3,

        /*! @const NEVPNConnectionErrorConfigurationFailed The VPN connection could not be established because the configuration is invalid. */
        VPNConnectionErrorConfigurationFailed = 4,

        /*! @const NEVPNConnectionErrorServerAddressResolutionFailed The address of the VPN server could not be determined. */
        VPNConnectionErrorServerAddressResolutionFailed = 5,

        /*! @const NEVPNConnectionErrorServerNotResponding Network communication with the VPN server has failed. */
        VPNConnectionErrorServerNotResponding = 6,

        /*! @const NEVPNConnectionErrorServerDead The VPN server is no longer functioning. */
        VPNConnectionErrorServerDead = 7,

        /*! @const NEVPNConnectionErrorAuthenticationFailed The user credentials were rejected by the VPN server. */
        VPNConnectionErrorAuthenticationFailed = 8,

        /*! @const NEVPNConnectionErrorClientCertificateInvalid The client certificate is invalid. */
        VPNConnectionErrorClientCertificateInvalid = 9,

        /*! @const NEVPNConnectionErrorClientCertificateNotYetValid The client certificate will not be valid until some future point in time. */
        VPNConnectionErrorClientCertificateNotYetValid = 10,

        /*! @const NEVPNConnectionErrorClientCertificateExpired The validity period of the client certificate has passed. */
        VPNConnectionErrorClientCertificateExpired = 11,

        /*! @const NEVPNConnectionErrorPluginFailed The VPN plugin died unexpectedly. */
        VPNConnectionErrorPluginFailed = 12,

        /*! @const NEVPNConnectionErrorConfigurationNotFound The VPN configuration could not be found . */
        VPNConnectionErrorConfigurationNotFound = 13,

        /*! @const NEVPNConnectionErrorPluginDisabled The VPN plugin could not be found or needed to be updated. */
        VPNConnectionErrorPluginDisabled = 14,

        /*! @const NEVPNConnectionErrorNegotiationFailed The VPN protocol negotiation failed. */
        VPNConnectionErrorNegotiationFailed = 15,

        /*! @const NEVPNConnectionErrorServerDisconnected The VPN server terminated the connection. */
        VPNConnectionErrorServerDisconnected = 16,

        /*! @const NEVPNConnectionErrorServerCertificateInvalid The server certificate is invalid. */
        VPNConnectionErrorServerCertificateInvalid = 17,

        /*! @const NEVPNConnectionErrorServerCertificateNotYetValid The server certificate will not be valid until some future point in time. */
        VPNConnectionErrorServerCertificateNotYetValid = 18,

        /*! @const NEVPNConnectionErrorServerCertificateExpired The validity period of the server certificate has passed. */
        VPNConnectionErrorServerCertificateExpired = 19
    }

    #endregion

    #region Methods

    /*!
     * @method startVPNTunnelAndReturnError:
     * @discussion This function is used to start the VPN tunnel using the current VPN configuration. The VPN tunnel connection process is started and this function returns immediately.
     * @param error If the VPN tunnel was started successfully, this parameter is set to nil. Otherwise this parameter is set to the error that occurred. Possible errors include:
     *    1. NEVPNErrorConfigurationInvalid
     *    2. NEVPNErrorConfigurationDisabled
     * @return YES if the VPN tunnel was started successfully, NO if an error occurred.
     */
    Task<(ErrorResponse, bool)> StartVPNTunnelAndReturnError();
    ErrorResponse DisconnectVPNTunnel();

    /*!
     * @method startVPNTunnelWithOptions:andReturnError:
     * @discussion This function is used to start the VPN tunnel using the current VPN configuration. The VPN tunnel connection process is started and this function returns immediately.
     * @param options A dictionary that will be passed to the tunnel provider during the process of starting the tunnel.
     *    If not nil, 'options' is an NSDictionary may contain the following keys
     *        NEVPNConnectionStartOptionUsername
     *        NEVPNConnectionStartOptionPassword
     * @param error If the VPN tunnel was started successfully, this parameter is set to nil. Otherwise this parameter is set to the error that occurred. Possible errors include:
     *    1. NEVPNErrorConfigurationInvalid
     *    2. NEVPNErrorConfigurationDisabled
     * @return YES if the VPN tunnel was started successfully, NO if an error occurred.
     */
    Task<ErrorResponse> StartVPNTunnelWithOptions(VPNCallParameters options);

    /*!
     * @method stopVPNTunnel:
     * @discussion This function is used to stop the VPN tunnel. The VPN tunnel disconnect process is started and this function returns immediately.
     */
    ErrorResponse StopVPNTunnel(bool wasDisconnectPlanned = true);

    /*!
     * @method fetchLastDisconnectErrorWithCompletionHandler:
     * @discussion Retrive the most recent error that caused the VPN to disconnect. If the error was generated by the VPN system (including the IPsec client) then the error will be in the NEVPNConnectionErrorDomain error domain. If the error was generated by a tunnel provider app extension then the error will be the ErrorResponse that the provider passed when disconnecting the tunnel.
     * @param handler A block which takes an optional ErrorResponse that will be called when the error is obtained.
     */
    ErrorResponse FetchLastDisonnectError();

    #endregion
}