# neumann-orbital

3D space visualization client for [Von Neumann Game](https://neumann-probe.net).
Displays the probe, mannies, and sectors (planets, asteroids) in a live 3D scene driven by the game API.

**Stack:** Godot 4.6+ (.NET edition) · C# · .NET 8

---

## Prerequisites

### System packages (Arch / CachyOS)

```bash
sudo pacman -S godot-mono dotnet-sdk-8.0
```

> `godot-mono` is mandatory — the standard `godot` package does not include C# support.

### mise (version manager)

If you use [mise](https://mise.jdx.dev/), the `.mise.toml` pins the .NET SDK version:

```bash
mise install      # installs dotnet 8.0.421
```

### Verify

```bash
godot-mono --version   # 4.6.x.stable.mono
dotnet --version       # 8.0.x
```

---

## Configuration

Copy the example config and fill in your API key:

```bash
mkdir -p ~/.config/neumann-orbital
cp config.example.toml ~/.config/neumann-orbital/config.toml
$EDITOR ~/.config/neumann-orbital/config.toml
```

`~/.config/neumann-orbital/config.toml`:

```toml
base_url = "https://neumann-probe.net"
api_key  = "vng_your_api_key_here"
```

---

## Build

```bash
# C# build only (no editor needed)
dotnet build neumann-orbital.csproj

# Full build via Godot (headless)
godot-mono --headless --build-solutions --quit
```

---

## Run

```bash
godot-mono --path .
```

Or open the project in the Godot editor:

```bash
godot-mono --editor --path .
```

---

## VS Code setup

Install the [Godot Tools](https://marketplace.visualstudio.com/items?itemName=geequlim.godot-tools) extension:

```bash
code --install-extension geequlim.godot-tools
code --install-extension ms-dotnettools.csdevkit
```

Then in the Godot editor: **Editor → Editor Settings → Mono → External Editor → VS Code**.

---

## Project structure

```
neumann-orbital/
├── scenes/              # .tscn scene files
│   ├── Main.tscn        # entry point
│   ├── Space.tscn       # 3D scene (star systems, camera)
│   └── Hud.tscn         # 2D overlay (gauges, sector panels, map)
├── src/
│   ├── Api/             # ApiClient + JSON models (Probe, Manny, Sector)
│   ├── State/           # AppState autoload singleton
│   └── UI/              # SpaceScene, Hud, CameraRig, MapPanel, WarpOverlay
├── resources/           # assets used by the game (planet textures)
├── assets/              # raw Kenney source packs — not committed
├── project.godot
└── neumann-orbital.csproj
```

**Data flow:**
```
ApiClient (async polling, exponential backoff)
    → AppState (signals: ProbeUpdated, ManniesUpdated, SectorDiscovered, SectorUpdated)
        → SpaceScene  — renders the current star system in 3D
        → Hud         — gauges, sector info, neighbour list, map panel
```

Config is read from `~/.config/neumann/config.toml` (falls back to `~/.config/neumann-cockpit/config.toml`).  
Scan history is persisted to `~/.config/neumann/scan_history.json` and shared with neumann-cockpit.

---

## License

GPL-3.0 — see [LICENSE](LICENSE).  
Assets: Kenney Space Kit (CC0), NASA textures (public domain), JetBrains Mono (OFL-1.1).
