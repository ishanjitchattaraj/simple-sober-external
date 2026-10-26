# Sober C# / Dear ImGui overlay

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

Press **Insert** while Sober is focused to open or close the menu. The mouse can interact with the menu while open; closing it restores click-through. Losing focus closes the menu and disables aim input. The menu uses compact black panels, fine gray borders, and muted violet accents, with **Aimbot**, **ESP**, **Triggerbot**, **World**, **Players**, **Experimental**, **Console**, and **Game Explorer** tabs. Mouse-wheel scrolling works in the tabs and explorer panes. Settings save to `settings.json` on menu close or application exit.

ESP draws screen and corner boxes, approximate 3D bounds, sampled-part skeleton lines, snaplines, head dots, offscreen arrows, motion trails, an XZ radar, predicted points, target highlight, names, distances, and equipped Tool names. Colors, alpha, thickness, dark outlines, distance gradients, maximum render distance, and distance fading are configurable. The ESP tab includes a synthetic live preview that responds to these settings. Workspace models with a direct Humanoid child that are not linked through Players.Character are classified as NPC candidates; discovery skips branches over 128 children, visits at most 512 nodes per scan, and refreshes its cache every three seconds. Bounds use fixed approximate dimensions; skeletons connect standard R6/R15-named parts when present. Health is sampled from the supported build’s Humanoid getter and displayed on a fixed 100-point scale; facing, visible/occluded state, and team colors still need verified fields.

Aim assistance is disabled by default; hold **right mouse** to activate it when the menu is closed. Choose **Mouse** or **Camera** mode. Camera mode uses the bounded camera-rotation operation, and the game's camera controller may overwrite it between samples. Aim controls include crosshair-centered FOV, dynamic FOV, sensitivity, smoothness curves, distance smoothing, deadzone, maximum distance, head/torso point, target priorities, and lock. The cursor position is not used for ranking. LocalPlayer is excluded. Target motion is estimated from successive root positions. Horizontal and vertical prediction can be enabled separately. Manual projectile speed and gravity produce a clamped flight-time lead; airborne/grounded state is unknown, so sampled vertical velocity is used directly. Prediction feeds both mouse and camera aim paths. A fixed internal 40-pixel-per-frame stability limit remains; it is not configurable.

The **World** tab offers overlay-only crosshair and FOV circle. The **Players** tab has search, a persisted name whitelist, target HUD, and a shortcut to the read-only object explorer. The **Console** shows connection state, tracked count, sample time, and viewport size. Aim ranking selects projected players within the FOV and distance limit. Closest to player uses the local character root when available. Lock pauses if its target leaves the eligible set; release, menu/focus changes, disabling aim, or changing priority resets it. Mouse mode uses XTest relative motion. Native Wayland is unsupported.

The **Game Explorer** starts at the game DataModel. Expand an instance to load its children; select a row to inspect its name, class, raw RTTI type, address, parent, and path. Supported part types also show their sampled position. Large branches load in pages of 128. Refresh the whole hierarchy or the selected branch to see changes. The explorer is read-only and runs its memory reads on a background worker, with bounded page sizes and a cache limit. Switching tabs or closing the menu cancels further loads; changing scenes clears the cached hierarchy. It shows the known fields for this build rather than a full reflection/property database.

Triggerbot activation, spectating, game-world edits (freecam, lighting, fog, camera FOV), and the requested speed, infinite jump, fly, and noclip mods are unavailable. Team/visibility filters, facing, team colors, and visible/occluded colors remain disabled. Humanoid property getters in the supported binary are invoked through a callback that adjusts the object argument by +0xa0; runtime samples across players confirmed Health at Humanoid Instance+0x170, MaxHealth at +0x188, and WalkSpeed at +0x1b0. Health changed over repeated samples while +0x168 stayed at 100. The displayed/filter denominator is fixed at 100 as requested, and values are clamped to [0, 1]. Health/dead target filtering and health bars are active. The configured supported binary hash is `852151db150ec26604a4e79a0c15d30c9724af302e635c182d35a0f67a4df`. When root starts the bridge without `SUDO_UID`, it infers the owner only if all discovered Sober Main processes belong to one UID. If multiple Sober user sessions exist, start it with `--owner-uid <uid>`. The socket remains mode 0600 and peer credentials must match the selected owner.

The overlay follows Sober's X11 window, hides when unfocused, and shows connection status while waiting for a scene. Player references refresh every 250 ms; memory sampling targets 60 Hz. NPC candidates are scanned on a separate bounded three-second schedule. Player characters are resolved through Players, including models inside Workspace folders. Invalid root references are re-resolved and retried with fresh memory pages. Missing heads fall back to the current body position. Boxes use projected bounds and viewport clipping, so a degenerate head/feet projection or an oversized close-up box does not discard a valid aim target. Positions are not held over from old samples when a character is unavailable.

## Build-specific memory layout

All build-specific offsets, fingerprints, and the inspected binary hash are in `Offsets.cs`. Supported SHA-256: `852151db150ec26604a4e79a0c15d30c9724af302e635c182d35d82a0f67a4df`. Discovery checks instruction fingerprints before using the layout. These checks are not a complete hash verification of the running process. A different client build requires a separately verified layout.

The helper authenticates the connecting user, targets that user's Sober Main process, and accepts maps and bounded reads over a private Unix socket. With `--allow-camera-writes`, it also accepts `aim_camera`: it checks the PID, build fingerprints, Workspace, Camera, remote player character, and writable mapping, then computes and writes exactly nine rotation floats. It accepts no arbitrary write address or byte payload. Temporary scene or target loss pauses camera aiming and appears in the overlay/menu; it resumes when the target is available again. Other camera validation or communication errors stop aim assistance.

## Checks and dependencies

```sh
./run.sh --self-test
./run.sh --render-test
./run.sh --snapshot
```

Snapshot requires a manual helper on the default socket, or `--socket PATH` for a managed session. It waits up to 30 seconds for a live sample. The renderer smoke test traverses all eight tabs and checks the transparent framebuffer and OpenGL error state, with a preview hierarchy for the explorer.

Requires .NET 10, Bash 5.1+, Flatpak, sudo, Xwayland, X11/Xfixes, XTest (`libXtst.so.6`), and OpenGL 3.3. NuGet dependencies: ImGui.NET 1.91.6.1 and OpenTK 4.9.4. The OpenGL renderer is adapted from NogginBops/ImGui.NET_OpenTK_Sample; its MIT license is in `Rendering/LICENSE.txt`.
