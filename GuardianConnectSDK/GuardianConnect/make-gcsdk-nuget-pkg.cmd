#nuget pack GuardianConnect.nuspec
#nuget add GuardianConnect.0.14.5.356.nupkg -source %LOCALAPPDATA%\GuardianConnectSDK\NugetPackages

#dotnet pack GuardianConnect.nuspec
dotnet add GuardianConnect.0.14.5.356.nupkg -source GuardianConnectSDK_NugetPackages