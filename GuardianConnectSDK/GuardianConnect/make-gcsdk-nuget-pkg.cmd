#nuget pack GuardianConnect.nuspec
#nuget add GuardianConnect.0.9.48.317.nupkg -source %LOCALAPPDATA%\GuardianConnectSDK\NugetPackages

#dotnet pack GuardianConnect.nuspec
dotnet add GuardianConnect.0.9.48.317.nupkg -source GuardianConnectSDK_NugetPackages