echo "Building Publish-AOT-Trimmed-Self-Contained GuardianFirewallService.exe"
msbuild /t:restore /t:Publish /p:PublishReadyToRun=false /p:IncludeNativeLibrariesForSelfExtract=false /p:IncludeAllContentForSelfExtract=false /p:Configuration=Release /p:Platform=%1 /p:WarningLevel=0 /p:RuntimeIdentifier=win-%1 GuardianFirewallService\GuardianFirewallService.csproj

:exit
