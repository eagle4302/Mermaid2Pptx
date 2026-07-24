#!/usr/bin/env bash
#
# Start the Mermaid2PPTX Web UI on macOS or Linux.
# POSIX/bash counterpart of Start-WebUI.bat (Windows).
set -euo pipefail

# Move to the repository root (the directory this script lives in), resolving
# symlinks so it works no matter where it is invoked from.
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" >/dev/null 2>&1 && pwd -P)"
cd "$SCRIPT_DIR"

URL="http://127.0.0.1:5088"

if ! command -v dotnet >/dev/null 2>&1; then
  echo ".NET SDK was not found. Please install the .NET 8 SDK first." >&2
  exit 1
fi

# Open the default browser at $URL using whichever opener the platform provides.
open_browser() {
  if command -v xdg-open >/dev/null 2>&1; then      # Linux
    xdg-open "$URL" >/dev/null 2>&1 || true
  elif command -v open >/dev/null 2>&1; then        # macOS
    open "$URL" >/dev/null 2>&1 || true
  fi
}

# If a server already answers on the URL, just open the browser and exit.
if command -v curl >/dev/null 2>&1 &&
   curl -fsS --max-time 1 -o /dev/null "$URL" 2>/dev/null; then
  echo "Server is already running. Opening browser..."
  open_browser
  exit 0
fi

echo "Starting Mermaid2PPTX Web UI..."
echo "URL: $URL"
echo
echo "Keep this terminal open while using the UI."
echo "Press Ctrl+C to stop the server."
echo

# Open the browser a few seconds after the server begins listening.
( sleep 3; open_browser ) &

# Run the server in the foreground. Ctrl+C stops it and returns here.
dotnet run --project "src/Mermaid2Pptx.Web/Mermaid2Pptx.Web.csproj" --urls "$URL" || true

echo
echo "Server stopped."
