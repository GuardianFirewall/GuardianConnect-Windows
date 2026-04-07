rem msbuild /restore /t:GuardianWinService /p:PlatformTarget=ARM64 /p:Configuration=Release /p:RuntimeIdentifer=win-ARM64 guardianwindows.sln
rem msbuild /t:restore /t:Build /t:GuardianWinService /p:Platform=%1 /p:Configuration=Release /p:RuntimeIdentifier=win-%1 guardianwindows.sln
msbuild /t:restore /t:build /p:Platform=%1 /p:Configuration=Release /p:RuntimeIdentifer=win-%1 GuardianWinService\GuardianWinService.csproj
