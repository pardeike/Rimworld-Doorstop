# RimWorld Doorstop

Companion implementation to [Unity Doorstop](https://github.com/NeighTools/UnityDoorstop). This provides a `Doorstop.dll` that Doorstop loads first, enables the Unity/Mono debug server, and adds a small hot‑reload mechanism for methods/ctors marked `[Reloadable]` in your mod assemblies.

> **Tested with Unity Doorstop v4.4.0** (Windows, Linux, macOS). See the latest release notes: https://github.com/NeighTools/UnityDoorstop/releases

---

## Why use it

- Unity player builds do not expose the Mono debugger by default. Doorstop can enable it at launch so you can **attach a C# debugger** (dnSpyEx / Rider / Visual Studio) to the shipping player.
- This repo’s loader also supports **method‑level hot reload**: when a DLL under `Mods/` changes, patched copies are swapped in **only for members annotated `[Reloadable]`**.

Define the attribute once in your mod (include it in the built DLL):
```csharp
using System;

[AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method)]
public class ReloadableAttribute : Attribute { }
```

---

## Requirements

- **Unity Doorstop v4.4.0** (multi‑platform; includes Linux + macOS builds)  
  ↳ https://github.com/NeighTools/UnityDoorstop/releases  
- **Harmony 2.4+** (optional but common; arm64‑ready for Apple Silicon)  
  ↳ https://github.com/pardeike/Harmony/releases  
- **Debugger** (pick one):  
  - **dnSpyEx** (Windows): https://github.com/dnSpyEx/dnSpy  
  - **JetBrains Rider** (Win/macOS): https://www.jetbrains.com/help/rider/Debugging_Unity_Applications.html  
  - **Visual Studio** (Windows): https://learn.microsoft.com/visualstudio/gamedev/unity/get-started/using-visual-studio-tools-for-unity

---

## Quick start — Windows (Steam)

1. **Locate the RimWorld folder** (default):  
   `C:\Program Files (x86)\Steam\steamapps\common\RimWorld`

2. **Install Doorstop**  
   Download the latest Unity Doorstop release and copy **`winhttp.dll`** and **`doorstop_config.ini`** next to `RimWorldWin64.exe`.

3. **Add this repository’s loader**  
   Build or download this repo’s release and place **`Doorstop.dll`** into the same folder.

4. **Configure Doorstop** (`doorstop_config.ini`):
   ```ini
   [General]
   enabled=true
   target_assembly=Doorstop.dll
   redirect_output_log=false

   [UnityMono]
   # If you need to override Mono's search path, set this (see Doorstop v4.2+ notes)
   dll_search_path_override=

   # Enable the Mono debug server and choose an address/port (default is 127.0.0.1:10000)
   debug_enabled=true
   debug_address=127.0.0.1:56000
   debug_suspend=false
   ```

5. **Build your mod in `Debug`** so the `.pdb` sits next to your `ModName.dll` in your mod’s `Assemblies/` folder.

6. **Run the game** (via Steam as usual).

Your RimWorld folder should now contain (abridged):

```
Data/
Mods/
MonoBleedingEdge/
RimWorldWin64_Data/
Doorstop.dll           <-- added (this repo)
doorstop_config.ini    <-- added (Unity Doorstop)
winhttp.dll            <-- added (Unity Doorstop)
RimWorldWin64.exe
...
```

---

## Quick start — macOS (Apple Silicon)

> Harmony is **arm64‑ready**; no Rosetta needed.

You’ll launch RimWorld via a tiny script so Doorstop preloads first.

1. **Place files** (next to the app bundle):  
   - `libdoorstop.dylib` (from Unity Doorstop release)  
   - `Doorstop.dll` (from this repo)  
   - `run.sh` (launcher script below)  

2. **Create `run.sh`** (works for both `RimWorldMac.app` and `RimWorld.app`):
   ```bash
   #!/usr/bin/env bash
   set -euo pipefail

   DIR="$(cd "$(dirname "$0")" && pwd)"
   APP="$DIR/RimWorldMac.app"
   [[ -d "$APP" ]] || APP="$DIR/RimWorld.app"
   BIN="$APP/Contents/MacOS/$(basename "$APP" .app)"

   # Inject Doorstop
   export DYLD_INSERT_LIBRARIES="$DIR/libdoorstop.dylib"

   # Launch with Doorstop CLI options
   exec "$BIN" \
     --doorstop-enabled true \
     --doorstop-target-assembly "$DIR/Doorstop.dll" \
     --doorstop-mono-debug-enabled true \
     --doorstop-mono-debug-address 127.0.0.1:56000 \
     --doorstop-mono-debug-suspend false
   ```

   Make it executable:
   ```bash
   chmod +x ./run.sh
   ```

3. **(If macOS blocks files)** remove quarantine:
   ```bash
   xattr -dr com.apple.quarantine ./libdoorstop.dylib ./run.sh ./RimWorldMac.app || true
   xattr -dr com.apple.quarantine ./RimWorld.app || true
   ```

4. **Run**
   ```bash
   ./run.sh
   ```

**Where to find game assemblies on macOS:**  
`~/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Contents/Resources/Data/Managed/Assembly-CSharp.dll`

---

## Quick start — Linux (with Rider)

> These steps mirror the current Linux section in the upstream README, updated for Doorstop v4.4.0.

1. **Download the Linux Doorstop build** from the Unity Doorstop release. Use a **numbered release**, not a CI build. Extract `x64/libdoorstop.so` and `x64/run.sh` into the RimWorld **game root**.

   Common Steam paths (pick what matches your install):
   - `~/.local/share/Steam/steamapps/common/RimWorld/` *(typical default)*
   - `~/.steam/debian-installation/steamapps/common/RimWorld/` *(Debian/Ubuntu installer path)*

2. **Add this repository’s loader**  
   Build or download this repo’s release and copy **`Doorstop.dll`** into the same game root.

3. **Edit `run.sh`** — set these lines to match (ports are your choice):
   ```bash
   executable_name="RimWorldLinux"
   debug_enable="1"
   debug_address="127.0.0.1:50000"
   debug_suspend="1"
   ```

   - `debug_suspend="1"` makes the game wait until your debugger attaches (so it’s normal if nothing appears yet).
   - If your `run.sh` doesn’t have these variables, use the CLI style instead (see **Alternative `run.sh`** below).

4. **Launch & attach in Rider**  
   Run `./run.sh`, then use Rider’s **“Attach to Unity Process”** / **Unity** configuration with Host `127.0.0.1` and Port `50000`.

5. **Breakpoints**  
   You can set source breakpoints in **your mod** (needs PDBs). Breakpoints in **RimWorld** code require IL‑level tools (e.g., dnSpyEx).

**Alternative `run.sh`** (works on any distro; uses CLI flags directly):
```bash
#!/usr/bin/env bash
set -euo pipefail

DIR="$(cd "$(dirname "$0")" && pwd)"
BIN="$DIR/RimWorldLinux"   # adjust if necessary

export LD_PRELOAD="$DIR/libdoorstop.so"

exec "$BIN" \
  --doorstop-enabled true \
  --doorstop-target-assembly "$DIR/Doorstop.dll" \
  --doorstop-mono-debug-enabled true \
  --doorstop-mono-debug-address 127.0.0.1:50000 \
  --doorstop-mono-debug-suspend true
```

---

## Debugging

- **dnSpyEx (Windows):** `Debug → Start Debugging → Debug engine: Unity`, target `127.0.0.1:<port>` (e.g., `56000` on Windows example).  
- **Rider (Win/macOS/Linux):** `Run → Attach to Unity Process` or create a Unity attach config and set the host/port you chose.  
- **Visual Studio (Windows):** `Debug → Attach to Process…` and connect to the Mono port.

> Doorstop’s **default** Mono debug server is `127.0.0.1:10000`. Using `50000`/`56000` is fine—just be consistent between config and your IDE.

Build the mod **in Debug** so symbols (`.pdb`) are present next to your DLL.

---

## Typical paths

**Windows (Steam default library):**
- Game root: `C:\Program Files (x86)\Steam\steamapps\common\RimWorld`
- Managed code: `RimWorldWin64_Data\Managed\Assembly-CSharp.dll`

**macOS (Steam):**
- Game root: `~/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app`
- Managed code: `RimWorldMac.app/Contents/Resources/Data/Managed/Assembly-CSharp.dll`

**Linux (Steam):**
- Game root (typical): `~/.local/share/Steam/steamapps/common/RimWorld/`  
- Game root (Debian installer): `~/.steam/debian-installation/steamapps/common/RimWorld/`  
- Managed code: `<GameRoot>/RimWorldLinux_Data/Managed/Assembly-CSharp.dll`

> On macOS, right‑click the app → **Show Package Contents** to browse `Contents/...`

---

## Hot‑reload details

- When a DLL inside `Mods/` changes, the loader copies and patches it **only** for members marked `[Reloadable]`.
- Ensure the assembly that **defines** `[Reloadable]` is deployed with your mod **before** starting the game (so the attribute type exists when the game boots).

---

## Troubleshooting

- **Can’t attach the debugger**  
  Check that debugging is enabled (Windows: `debug_enabled=true`; macOS/Linux: `--doorstop-mono-debug-enabled true`), verify the port, and allow local loopback in your firewall.

- **macOS: “Permission denied” or app/dylib blocked**  
  `chmod +x run.sh` and clear quarantine with `xattr -dr com.apple.quarantine …` (see steps above).

- **Doorstop not loading on Windows**  
  `winhttp.dll` **and** `doorstop_config.ini` must sit beside `RimWorldWin64.exe`. Launchers that spawn the game in a different working directory can bypass `winhttp.dll`—start from Steam or the actual exe.

- **Breakpoints never hit (your mod)**  
  Ensure your mod build is `Debug` and the `.pdb` is next to the DLL.

- **Mono search path (Doorstop v4.2+ change)**  
  If a needed dependency isn’t found early, set `dll_search_path_override=` in `doorstop_config.ini` to include the directory that holds it.

---

## Notes (advanced)

- **CLI flags on all platforms**: Doorstop v4 uses `--doorstop-*` flags; see the upstream README for the full list.  
- **Default debug port**: `127.0.0.1:10000`. Change via `debug_address` (config) or `--doorstop-mono-debug-address` (CLI).  
- **Entrypoint**: v4 expects `static void Doorstop.Entrypoint.Start()`; this repo already provides it.

---

## Credits

- [Unity Doorstop](https://github.com/NeighTools/UnityDoorstop)  
- [Harmony](https://github.com/pardeike/Harmony)  
- [dnSpyEx](https://github.com/dnSpyEx/dnSpy), [JetBrains Rider](https://www.jetbrains.com/help/rider/Debugging_Unity_Applications.html), [Visual Studio](https://learn.microsoft.com/visualstudio/gamedev/unity/get-started/using-visual-studio-tools-for-unity)
