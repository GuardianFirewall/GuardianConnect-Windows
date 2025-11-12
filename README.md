# GuardianConnect-Windows

This is a work-in-progress SDK for Windows applications to integrate with the Guardian Connect API and establish VPN connections to the Guardian Firewall infrastructure. All lower level components are exposed but the use of high level APIs in `GRDVPNHelper` are recommended. This framework includes everything to establish an IKEv2 VPN connection leveraging the IKEv2 VPN daemon included in Windows. We officially suppport Windows 10 & 11.
The SDK is built using native Windows C# technologies and supports .NET AOT ([ahead-of-time](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/?tabs=windows%2Cnet8)) complications.

For more information and a direct contact please visit https://guardianapp.com/company/partners

## Integration
GuardianConnect for Windows is published as a NuGet [package through Github](https://github.com/orgs/GuardianFirewall/packages?repo_name=GuardianConnect-Windows) as well as archived releases in this repo.

The SDK provides all of the abstractions to integrate the ability to establish a VPN connection to the global Guardian Firewall infrastructure by integrating the abstractions into a Windows service as well as into the main application a user or other system may interact with. Communcation between the SDK components in the main app & service are handled by the SDK.  
The benefit of the separation is that the service can maintain a reliable VPN connection. The service is required to be executed with SYSTEM authority for necessary Windows OS operations.

## Building
The SDK is currently based on .NET version 9, though older versions may be compatible as well.  

