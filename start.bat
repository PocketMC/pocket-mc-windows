@echo off
setlocal
echo Building PocketMC Desktop...
dotnet build PocketMC.Desktop.sln --configuration Debug
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Build FAILED. Cannot start application.
    pause
    exit /b %ERRORLEVEL%
)
echo.
echo Starting PocketMC Desktop...
dotnet run --project PocketMC.Desktop\PocketMC.Desktop.csproj --configuration Debug
endlocal
