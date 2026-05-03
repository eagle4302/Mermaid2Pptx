@echo off
setlocal

cd /d "%~dp0"
set "URL=http://127.0.0.1:5088"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET SDK was not found. Please install .NET 8 SDK first.
  pause
  exit /b 1
)

echo Starting Mermaid2PPTX Web UI...
echo URL: %URL%
echo.
echo Keep this window open while using the UI.
echo Press Ctrl+C to stop the server.
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $r = Invoke-WebRequest -UseBasicParsing -Uri '%URL%' -TimeoutSec 1; if ($r.StatusCode -ge 200) { exit 0 } else { exit 1 } } catch { exit 1 }" >nul 2>nul
if %ERRORLEVEL%==0 (
  echo Server is already running. Opening browser...
  start "" "%URL%"
  exit /b 0
)

start "" powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Sleep -Seconds 3; Start-Process '%URL%'"

dotnet run --project "src\Mermaid2Pptx.Web\Mermaid2Pptx.Web.csproj" --urls "%URL%"

echo.
echo Server stopped.
pause
