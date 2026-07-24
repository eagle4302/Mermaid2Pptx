#!/usr/bin/env bash
#
# Stop the Mermaid2PPTX Web UI on macOS or Linux.
# POSIX/bash counterpart of Stop-WebUI.bat (Windows).
set -uo pipefail

PORT="5088"

echo "Stopping Mermaid2PPTX Web UI on port ${PORT}..."
echo

if ! command -v lsof >/dev/null 2>&1 && ! command -v fuser >/dev/null 2>&1; then
  echo "Neither lsof nor fuser is available; cannot locate the server process." >&2
  echo "Stop the server from the terminal running it (Ctrl+C), or install lsof." >&2
  exit 1
fi

# Print the PIDs currently listening on $PORT, one per line, sorted and unique.
# Prefers lsof (works on both Linux and macOS); falls back to fuser (Linux).
listening_pids() {
  if command -v lsof >/dev/null 2>&1; then
    lsof -ti "tcp:${PORT}" -sTCP:LISTEN 2>/dev/null | sort -u
  else
    fuser "${PORT}/tcp" 2>/dev/null | tr -s ' ' '\n' | grep -E '^[0-9]+$' | sort -u
  fi
}

# Collapse newline-separated PIDs into a trimmed, space-separated list.
pids="$(listening_pids | tr '\n' ' ' | xargs 2>/dev/null || true)"

if [ -z "$pids" ]; then
  echo "No server is listening on port ${PORT}."
  exit 0
fi

failed=0
for pid in $pids; do
  name="$(ps -p "$pid" -o comm= 2>/dev/null | xargs 2>/dev/null || true)"
  echo "Stopping PID ${pid} (${name:-unknown})..."
  kill "$pid" 2>/dev/null || failed=1
done

# Allow a graceful shutdown, then force-kill anything still listening.
sleep 1
remaining="$(listening_pids | tr '\n' ' ' | xargs 2>/dev/null || true)"
if [ -n "$remaining" ]; then
  for pid in $remaining; do
    kill -9 "$pid" 2>/dev/null || failed=1
  done
  sleep 1
  remaining="$(listening_pids | tr '\n' ' ' | xargs 2>/dev/null || true)"
fi

if [ -n "$remaining" ]; then
  echo "Port ${PORT} is still in use." >&2
  exit 1
fi

if [ "$failed" -ne 0 ]; then
  echo "Some processes could not be signaled cleanly." >&2
  exit 1
fi

echo "Server stopped."
