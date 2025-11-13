# GuardianConnect-Windows

This is a work-in-progress SDK for Windows applications to integrate with the Guardian Connect API and establish VPN connections to the Guardian Firewall infrastructure. All lower level components are exposed but the use of high level APIs in `GRDVPNHelper` are recommended. This framework includes everything to establish an IKEv2 VPN connection leveraging the IKEv2 VPN daemon included in Windows. We officially suppport Windows 10 & 11.
The SDK is built using native Windows C# technologies and supports .NET AOT ([ahead-of-time](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/?tabs=windows%2Cnet8)) complications.

For more information and a direct contact please visit https://guardianapp.com/company/partners

## Integration
GuardianConnect for Windows is published as a NuGet [package through Github](https://github.com/orgs/GuardianFirewall/packages?repo_name=GuardianConnect-Windows) as well as archived releases in this repo.

The SDK provides all of the abstractions to integrate the ability to establish a VPN connection to the global Guardian Firewall infrastructure by integrating the abstractions into a Windows service as well as into the main application a user or other system may interact with. Communcation between the SDK components in the main app & service are handled by the SDK.  
The benefit of the separation is that the service can maintain a reliable VPN connection. The service is required to be executed with SYSTEM authority for necessary Windows OS operations.

## Manual Builds
The SDK is currently based on .NET version 9, though older versions may be compatible as well. If a local build is preferred for the integration it can be achieved by following the information below.

#### Main SDK
The GuardianConnect for Windows repo contains six core projects that need to be built for inclusion into your solution.  
They are:
```
- GuardianConnect.Shared
- Win32Calls
- Win32Calls.WFP
- GuardianConnect.Abstractions
- GuardianConnect
- GuardianConnect.Services
```
The first five are necessary to build the GuardianConnect nuget package, of which GuardianConnect is the main project. The build dependencies and build sequence are as follows:
```
- GuardianConnect.Shared
- Win32Calls.WFP
- GuardianConnect.Abstractions
- Win32Calls
- GuardianConnect
```

#### Services Components
The project `GuardianConnect.Services` is built and offered also as a nuget package for your Service portion of your solution. The build dependencies and build sequence are as follows:
```
- GuardianConnect.Shared
- Win32Calls.WFP
- GuardianConnect.Abstractions
- Win32Calls
- GuardianConnect
- GuardianConnect.Services
```

The `GuardianConnect.Abstractions` are built and packaged separately as `GuardianConnect_Services_SDK_{cpu-arch}.nupkg` to assist developers with interfaces into the GuardianConnect.Services implementation.  
The CI configuartion files issues the following `msbuild` command lines can be used directly or as configured in your IDE respective of your choices for verbosity and target platform (included below are for both `x64` and `arm64`)
```
    - name: Build GuardianConnect for x64 CPU
      run: msbuild /t:restore /t:Build /p:Platform=x64 /p:Configuration=Release /p:RuntimeIdentifier=win-x64 GuardianConnect\GuardianConnect.csproj

    - name: Build GuardianConnect for ARM64 CPU
      run: msbuild /t:restore /t:Build /p:Platform=arm64 /p:Configuration=Release /p:RuntimeIdentifier=win-arm64 GuardianConnect\GuardianConnect.csproj
    
    - name: Build GuardianConnect.Services for x64 CPU
      run: msbuild /t:restore /t:Build /p:Platform=x64 /p:Configuration=Release /p:RuntimeIdentifier=win-x64 GuardianConnect.Services\GuardianConnect.Services.csproj
      
    - name: Build GuardianConnect.Services for ARM64 CPU
      run: msbuild /t:restore /t:Build /p:Platform=arm64 /p:Configuration=Release /p:RuntimeIdentifier=win-arm64 GuardianConnect.Services\GuardianConnect.Services.csproj
      
    - name: Build GuardianConnectSDK.Services.Abstractions for any CPU
      run: msbuild /t:restore /t:Build /p:Configuration=Release GuardianConnect.Abstractions\GuardianConnect.Abstractions.csproj
```
