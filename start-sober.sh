#!/usr/bin/env bash
set -euo pipefail
# Starts this project's bridge, X11 Sober launch, and overlay as one managed session.
dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
if (( EUID == 0 )); then
 echo 'Run this launcher as your normal user; it requests sudo only for the bridge.' >&2
 exit 1
fi
if [[ ! -f "$dir/bin/Release/net10.0/SoberImGui.dll" ]]; then
 dotnet build "$dir/sober-imgui.csproj" -c Release
fi
sudo -v
session=$(mktemp -d "$dir/.session.XXXXXX")
sober_pid= bridge_pid= overlay_pid=
cleanup() {
 trap - EXIT INT TERM HUP
 [[ -z "$overlay_pid" ]] || kill -TERM "$overlay_pid" 2>/dev/null || true
 [[ -z "$sober_pid" ]] || kill -TERM "$sober_pid" 2>/dev/null || true
 [[ -z "$overlay_pid" ]] || wait "$overlay_pid" 2>/dev/null || true
 [[ -z "$sober_pid" ]] || wait "$sober_pid" 2>/dev/null || true
 # The bridge observes Sober's process exit, cancels Accept, and removes its socket.
 [[ -z "$bridge_pid" ]] || wait "$bridge_pid" 2>/dev/null || true
 rm -f "$session/memory.sock"
 rmdir "$session" 2>/dev/null || true
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM HUP
# An already running instance could hand this launch off and exit immediately.
if flatpak ps --columns=application | grep -Fxq org.vinegarhq.Sober; then
 echo 'Close the existing Sober instance before starting this session.' >&2
 exit 1
fi
flatpak run --nosocket=wayland --socket=x11 --env=GDK_BACKEND=x11 org.vinegarhq.Sober "$@" &
sober_pid=$!
sudo -n "$dir/run-bridge.sh" --allow-camera-writes --socket "$session/memory.sock" --watch-pid "$sober_pid" &
bridge_pid=$!
ready=false
for ((attempt=0; attempt<100; attempt++)); do
 if [[ -S "$session/memory.sock" ]]; then ready=true; break; fi
 if ! kill -0 "$bridge_pid" 2>/dev/null || ! kill -0 "$sober_pid" 2>/dev/null; then break; fi
 sleep .1
done
if [[ "$ready" != true ]]; then echo 'Sober or the memory bridge failed to start.' >&2; exit 1; fi
"$dir/run.sh" --socket "$session/memory.sock" &
overlay_pid=$!
# Any component ending tears down this session, including Sober closing normally.
set +e
wait -n "$sober_pid" "$bridge_pid" "$overlay_pid"
result=$?
exit "$result"
