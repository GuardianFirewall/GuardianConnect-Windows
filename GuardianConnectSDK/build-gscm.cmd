msbuild /t:restore /t:Build InstallationHelpers\GuardianServiceSCM /p:Platform=%1 /p:Configuration=Release /p:RuntimeIdentifier=win-%1 
