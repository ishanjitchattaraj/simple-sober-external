#!/bin/sh
set -eu
dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec /usr/bin/dotnet "$dir/bin/Release/net10.0/SoberImGui.dll" --home "$dir" "$@"
