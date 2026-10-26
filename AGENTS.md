# Sober ImGui project guidance

## Project scope

This is a .NET 10 C# application that draws an external Dear ImGui/OpenTK overlay for Sober on Linux/X11. It reads process memory through a separate Unix-socket bridge; it does not inject code. Keep the overlay unprivileged and preserve the separation between UI/tracking and the privileged bridge.

## Architecture and safety

- `Program.cs` owns startup modes and process lifetime. `Tracker.cs` coordinates scene discovery, sampling, and targeting. `Overlay.cs` owns the UI and settings.
- `BridgeServer.cs` and `Memory.cs` implement the local bridge. Preserve peer validation, bounded reads, request validation, and explicit errors.
- Camera mode is an opt-in, narrowly validated bridge operation. Do not add arbitrary process writes, code injection, anti-detection, or security-bypass behavior.
- Build-specific memory layouts and fingerprints live in `Offsets.cs`. Verify any layout change against the intended client build; do not treat a fingerprint match as complete binary verification.
- Keep the explorer read-only and retain cancellation, page bounds, and cache limits. Preserve the existing X11-only behavior; native Wayland is unsupported.
- Follow the existing C# style and nullable/unsafe settings in `sober-imgui.csproj`. Avoid unrelated refactors and unnecessary dependencies.

## Build and checks

Run from the repository root:

```sh
dotnet build sober-imgui.csproj -c Release
./run.sh --self-test
```

The self-test covers projection, aim selection, camera logic, explorer reads, and character tracking. `./run.sh` runs the already-built Release output.

For rendering changes, run `./run.sh --render-test` in an X11/OpenGL environment. For live bridge or snapshot checks, use a running Sober session as described in `README.md`; `--snapshot` requires a live bridge and scene.

## Project references

- `README.md` documents launch/stop behavior, settings, bridge operation, runtime requirements, and supported limitations.
- `Offsets.cs` contains build-specific offsets and fingerprints.
- `Rendering/LICENSE.txt` contains the renderer dependency's license; retain it with the adapted renderer.
