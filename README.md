# Sober C# External

This .NET 10 application uses Dear ImGui and OpenTK/OpenGL to draw an external X11 overlay. The helper reads process memory. Mouse aim sends relative X11 mouse motion through XTest. Camera aim writes only the camera rotation matrix through a dedicated validated bridge operation; it leaves camera position unchanged. Neither mode injects code.

## Start and stop

Build once, then start the managed session as your normal user:

```sh
dotnet build -c Release
./start-sober.sh
```

The launcher requests sudo for the memory bridge, launches Sober with X11, waits for the bridge socket, and starts the overlay. Close an existing Sober instance first. Closing Sober, stopping the launcher, or a component exiting ends the session. The bridge watches the launched Sober process and removes its socket when that process exits. The overlay stays unprivileged. The managed launcher enables the camera-only write operation with `--allow-camera-writes`; manual helpers default to reads only. Launcher arguments are passed to Sober.

For manual diagnostics, `sudo ./run-bridge.sh` and `./run.sh` still work separately; use `sudo ./run-bridge.sh --allow-camera-writes` for camera mode. Manually started helpers have no automatic Sober lifetime unless `--watch-pid PID` is supplied. `./stop.sh` stops the overlay; in a managed session that also ends the session.

## Overlay settings and game explorer

Press **Insert** while Sober is focused to open or close the menu. The mouse can interact with the menu while open; closing it restores click-through. Losing focus closes the menu and disables aim input. The menu has **Aimbot**, **ESP**, and **Game Explorer** tabs. Mouse-wheel scrolling works in the tabs and explorer panes. Settings save to `settings.json` on menu close or application exit; there is also a Save button.

- Player boxes, names, distances, and status bar visibility.
- Aim method: **Mouse** (default) or **Camera**. Camera mode rotates the camera directly and uses the same target priorities, FOV, aim lock, sensitivity, and smoothness.
- Aim assistance, disabled by default; hold **right mouse** to activate when the menu is closed.
- FOV circle and target marker; the radius is in overlay pixels, centered on the game viewport.
- Sensitivity multiplier (0.1×–20×) and smoothness; higher smoothness slows the response.
- Maximum target distance in studs, and head or torso targeting.
- Target priority: closest to the centered mouse/crosshair aiming point, or closest to your player in world space. The captured desktop cursor is not used for ranking; it can remain offset while Sober controls the camera.
- Aim lock: retain the acquired character while holding right mouse, even when another player becomes closer.

Both priorities select only projected players within the crosshair-centered FOV and maximum distance limit. Closest to player uses your character root position, rather than the camera; it waits if your character position is unavailable. The maximum distance setting continues to measure distance from the camera, as used by the overlay labels. With aim lock enabled, leaving FOV/range or losing the target pauses aim without switching people. Release right mouse to acquire another target; opening the menu, losing focus, disabling aim, or changing priority also resets the lock. LocalPlayer is excluded. There is no team or wall/occlusion filter. Boxes and torso points approximate character bounds. Input gain depends on the game's mouse settings; adjust the multiplier for your local build. Mouse mode requires the game to accept XTest relative motion. Camera mode does not send mouse input, but the game camera controller may overwrite the rotation between samples. Native Wayland is unsupported by the overlay.

The **Game Explorer** starts at the game DataModel. Expand an instance to load its children; select a row to inspect its name, class, raw RTTI type, address, parent, and path. Supported part types also show their sampled position. Large branches load in pages of 128. Refresh the whole hierarchy or the selected branch to see changes. The explorer is read-only and runs its memory reads on a background worker, with bounded page sizes and a cache limit. Switching tabs or closing the menu cancels further loads; changing scenes clears the cached hierarchy. It shows the known fields for this build rather than a full reflection/property database.

The overlay follows Sober's X11 window, hides when unfocused, and shows connection status while waiting for a scene. Character references refresh every 250 ms; memory sampling targets 60 Hz. Characters are resolved through Players, including models inside Workspace folders. Invalid root references are re-resolved and retried with fresh memory pages. Missing heads fall back to the current body position. Boxes use projected bounds and viewport clipping, so a degenerate head/feet projection or an oversized close-up box does not discard a valid aim target. Positions are not held over from old samples when a character is unavailable.

## Build-specific memory layout

All build-specific offsets, fingerprints, and the inspected binary hash are in `Offsets.cs`. Supported SHA-256: `852151db150ec26604a4e79a0c15d30c9724af302e635c182d35d82a0f67a4df`. Discovery checks instruction fingerprints before using the layout. These checks are not a complete hash verification of the running process. A different client build requires a separately verified layout.

The helper authenticates the connecting user, targets that user's Sober Main process, and accepts maps and bounded reads over a private Unix socket. With `--allow-camera-writes`, it also accepts `aim_camera`: it checks the PID, build fingerprints, Workspace, Camera, remote player character, and writable mapping, then computes and writes exactly nine rotation floats. It accepts no arbitrary write address or byte payload. Temporary scene or target loss pauses camera aiming and appears in the overlay/menu; it resumes when the target is available again. Other camera validation or communication errors stop aim assistance.

## Checks and dependencies

```sh
./run.sh --self-test
./run.sh --render-test
./run.sh --snapshot
```

Snapshot requires a manual helper on the default socket, or `--socket PATH` for a managed session. It waits up to 30 seconds for a live sample. The renderer smoke test exercises all three tabs, with a preview hierarchy for the explorer.

Requires .NET 10, Bash 5.1+, Flatpak, sudo, Xwayland, X11/Xfixes, XTest (`libXtst.so.6`), and OpenGL 3.3. NuGet dependencies: ImGui.NET 1.91.6.1 and OpenTK 4.9.4. The OpenGL renderer is adapted from NogginBops/ImGui.NET_OpenTK_Sample; its MIT license is in `Rendering/LICENSE.txt`.
