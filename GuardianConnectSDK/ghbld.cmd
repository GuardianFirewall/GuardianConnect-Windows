IF "%~1"=="" goto :exit

if "%~1" == "sdk" (
	rem msbuild of GuardianConnect
	goto :exit
)

if "%~1" == "gfs" (
	 msbuild /t:restore /t:Build /p:Platform=%2 /p:Configuration=Release /p:RuntimeIdentifier=win-%2 GuardianWinService\GuardianWinService.csproj
	goto :exit
)

:exit
