# GuardianConnect-Windows

This is a work-in-progress SDK for Windows applications to integrate with the Guardian Connect API and establish VPN connections to the Guardian Firewall infrastructure. All lower level components are exposed but the use of high level APIs in `GRDVPNHelper` are recommended. This framework includes everything to establish an IKEv2 VPN connection leveraging the IKEv2 VPN daemon included in Windows. We officially suppport Windows 10 & 11.
The SDK is built using native Windows C# technologies and supports .NET AOT ([ahead-of-time](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/?tabs=windows%2Cnet8)) complications.

For more information and a direct contact please visit https://guardianapp.com/company/partners

## Integration
The SDK is being published as a nuget package available through this repo as well as archived releases in this repo

## Building
The SDK is currently based on .NET version 9 though older versions may be compatible as well.  
For local builds please ensure that you have the .NET SDK installed which you can verify by entering `dotnet --version` into a PowerShell terminal.

To create a debug build open a local PowerShell terminal and move into the root of the repo's folder. The build can then be started by entering `dotnet build`. Upon completion the build artifacts will be available in <needs more info here>

