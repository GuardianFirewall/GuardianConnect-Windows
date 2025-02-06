#nuget pack GuardianConnect.nuspec
#nuget add GuardianConnect.0.10.1.52.nupkg -source %LOCALAPPDATA%\GuardianConnectSDK\NugetPackages

#dotnet pack GuardianConnect.nuspec
dotnet add GuardianConnect.0.10.1.52.nupkg -source GuardianConnectSDK_NugetPackages