#!/bin/sh
set -eu
dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
if [ ! -f "$dir/overlay.pid" ]; then echo "Overlay is not running."; exit 0; fi
pid=$(cat "$dir/overlay.pid")
case "$pid" in ''|*[!0-9]*) echo "Invalid overlay PID."; exit 1;; esac
if [ -r "/proc/$pid/cmdline" ] && tr '\000' '\n' < "/proc/$pid/cmdline" | /usr/bin/grep -Fxq "$dir/bin/Release/net10.0/SoberImGui.dll"; then
 kill -TERM "$pid"
 echo "Overlay stopped."
else echo "Overlay is not running."; fi
