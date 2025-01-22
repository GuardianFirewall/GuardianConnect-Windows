rem Build GuardianConnect
msbuild /t:restore /t:Build /p:Platform=x64 /p:Configuration=Release /p:RuntimeIdentifier=win-x64 GuardianConnect
      
rem Build GuardianFirewall Service
msbuild /t:restore /t:Build /p:Platform=x64 /p:Configuration=Release /p:RuntimeIdentifier=win-x64 GuardianWinService\GuardianWinService.csproj