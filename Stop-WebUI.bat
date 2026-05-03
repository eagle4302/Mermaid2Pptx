@echo off
setlocal

cd /d "%~dp0"
set "PORT=5088"

echo Stopping Mermaid2PPTX Web UI on port %PORT%...
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command "$port = %PORT%; $connections = @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue); if ($connections.Count -eq 0) { Write-Host ('No server is listening on port {0}.' -f $port); exit 0 }; $processIds = @($connections | Select-Object -ExpandProperty OwningProcess -Unique); $failed = $false; foreach ($processId in $processIds) { try { $process = Get-Process -Id $processId -ErrorAction Stop; Write-Host ('Stopping PID {0} ({1})...' -f $processId, $process.ProcessName); Stop-Process -Id $processId -Force -ErrorAction Stop } catch { Write-Warning $_.Exception.Message; $failed = $true } }; Start-Sleep -Milliseconds 500; $remaining = @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue); if ($remaining.Count -gt 0) { Write-Warning ('Port {0} is still in use.' -f $port); exit 1 }; if ($failed) { exit 1 }; Write-Host 'Server stopped.'; exit 0"

if errorlevel 1 (
  echo.
  echo Failed to stop the server. Try closing the server window manually, or run this file as administrator.
  pause
  exit /b 1
)

echo.
pause
