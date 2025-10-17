IF "%~1"=="" goto :exit

if "%~1" == "sdk" (
	if "%~2" == "pack" (
		nuget pack GuardianConnectSDK.nuspec
		goto :exit
	)
	if "%~2" == "" (
		echo "Building must include CPU platform as second argument"
		goto :exit
	)

	msbuild /t:restore /t:Build /p:Platform=%2 /p:Configuration=Release /p:RuntimeIdentifier=win-%2 GuardianConnect\GuardianConnect.csproj

	goto :exit
)

if "%~1" == "gfs" (
	if "%~2" == "pack" (
		nuget pack GuardianFirewallService.nuspec
		goto :exit
	)
	if "%~2" == "" (
		echo "Building must include CPU platform as second argument"
		goto :exit
	)
	if "%~3" == "aot" {
		echo "Building Publish-AOT-Trimmed-Self-Contained GuardianFirewallService.exe"
		msbuild /t:restore /t:Publish /p:Platform=%2 /p:Configuration=Release /p:RuntimeIdentifier=win-%2 GuardianFirewallService\GuardianFirewallService.csproj /p:PublishSingleFile=true /p:SelfContained=true /p:PublishTrimmed=true
		goto :exit
	}

	if "%~3" == "dbg" {
		msbuild /t:restore /t:Build /p:Platform=%2 /p:Configuration=Debug /p:RuntimeIdentifier=win-%2 GuardianFirewallService\GuardianFirewallService.csproj
		goto :exit
	}
	msbuild /t:restore /t:Build /p:Platform=%2 /p:Configuration=Release /p:RuntimeIdentifier=win-%2 GuardianFirewallService\GuardianFirewallService.csproj
	goto :exit
)

:exit
