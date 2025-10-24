IF "%~1"=="" goto :exit

if "%~1" == "" (
	echo "Building must include CPU platform as second argument"
	goto :exit
)
echo "Building Publish-AOT-Trimmed-Self-Contained GuardianFirewallService.exe"
msbuild /t:restore ^
	/t:Publish ^
	/p:SelfContained=true ^
	/p:_IsPortable=false ^
	/p:PublishSingleFile=true ^
	/p:PublishAot=true ^
	/p:PublishReadyToRun=false ^
	/p:PublishTrimmed=true ^
	/p:IncludeNativeLibrariesForSelfExtract=false ^
	/p:IncludeAllContentForSelfExtract=false ^
	/p:Configuration=Release ^
	/p:Platform=%1 ^
	/p:RuntimeIdentifier=win-%1 GuardianFirewallService\GuardianFirewallService.csproj

:exit
