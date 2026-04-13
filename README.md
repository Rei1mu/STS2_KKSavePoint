# KKSavePoint 

`KKSavePoint` SavePoint mod for recording and loading checkpoints.

It includes:
- Linux/WSL build/install scripts (`bash`)
- conda environment file (`environment.yml`)
- an included decompile reference folder (curated subset)
- 5 tutorial examples behind main-menu toggles

## Prerequisites

Install these first:

1. Slay the Spire 2 installed via Steam.
2. WSL2 (Ubuntu) if you are working from Windows.
3. Git.
4. Miniconda (recommended).
5. Godot 4.5.1 CLI (optional if you use `./scripts/bash/setup_godot_cli.sh`).

Official guides:
- WSL install: [Microsoft WSL install guide](https://learn.microsoft.com/windows/wsl/install)
- Git install: [Git install guide](https://git-scm.com/book/en/v2/Getting-Started-Installing-Git)
- Miniconda install: [Miniconda install guide](https://www.anaconda.com/docs/getting-started/miniconda/install)
- .NET 9 SDK (only if not using conda-managed dotnet): [.NET on Linux](https://learn.microsoft.com/dotnet/core/install/linux)
- Godot 4.5.1 release downloads (only if not using setup godot script): [Godot 4.5.1](https://github.com/godotengine/godot/releases/tag/4.5.1-stable)

Notes:
- This repo’s conda environment installs Python + dotnet for you.
- `./scripts/bash/setup_godot_cli.sh` downloads a local Godot CLI binary into `.tools/`.
- You must set `STS2_INSTALL_DIR` in a local `.env` file (see below).

## Tutorial examples in this mod

All gameplay examples are disabled by default and controlled from a main-menu button (`Example Mod`).

1. Add a `Gamble` button to the top bar (with wager selection)
2. Change `Burning Blood` to heal 10 after combat (instead of 6)
3. Force Ironclad `Strike` base damage to 10
4. Give Ironclad +1 Strength at the start of each combat
5. Add a main-menu UI for toggling all features on/off

## Clone location

You can clone this repository anywhere.

Example:

```bash
mkdir -p ~/dev/sts2
cd ~/dev/sts2
git clone https://github.com/customjack/sts2_ExampleMod ExampleMod
cd ExampleMod
```

## .env setup (required)

You must create a local `.env` from `.env.example` before running scripts.

Do this once:

```bash
cp .env.example .env
```

Then edit `.env` and set:

```bash
STS2_INSTALL_DIR='C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2'
```

Notes:
- `.env` is local-only and gitignored by default.
- You can use either a Windows path (`C:\...`) or a WSL path (`/mnt/c/...`).
- Bash scripts in `scripts/bash/` load `.env` automatically.

## Environment setup

### Option A: Conda (recommended)

#### Linux/WSL

```bash
cd /path/to/ExampleMod
cp .env.example .env   # once, then edit STS2_INSTALL_DIR
./scripts/bash/setup_env.sh
source ./scripts/bash/activate_env.sh
```

### Option B: System tools

Use the guides in the `Prerequisites` section, then ensure tools are on `PATH`.

## Build / pack / install

### Linux/WSL

```bash
./scripts/bash/build_and_stage.sh
./scripts/bash/setup_godot_cli.sh
./scripts/bash/make_pck.sh
./scripts/bash/install_to_game.sh
```

Installed mod output goes to:
- `${STS2_INSTALL_DIR}/mods/ExampleMod/ExampleMod.dll`
- `${STS2_INSTALL_DIR}/mods/ExampleMod/ExampleMod.pck`

## Using the toggle menu

1. Launch STS2 with mods enabled.
2. In main menu, click `Example Mod`.
3. Toggle feature checkboxes.
4. Start a run (or next combat) to observe enabled examples.

Notes:
- Toggles are persisted to `user://example_mod_settings.json`.
- Existing in-run objects may keep old values until the next relevant rebuild/combat.

## Decompile folder in this repo

`decompile/` includes a curated subset of decompiled game files needed by these examples.

To refresh from your local full decompile dump:

### Linux/WSL

```bash
./scripts/bash/sync_decompile_subset.sh
```

## Mod manifest and preview image

Edit `mod_manifest.json` to change how your mod appears:

```json
{
  "pck_name": "ExampleMod",
  "name": "ExampleMod",
  "author": "local-dev",
  "description": "Tutorial mod with toggleable gameplay examples.",
  "version": "0.1.0"
}
```

Notes:
- `pck_name` should match your mod package/base name (and usually your DLL/PCK names).
- `name`, `author`, `description`, and `version` are display metadata.

Preview image:
- Use `mod_image.png` at the project root.
- This repo already includes a working `mod_image.png` preview image.
- Replace it with your own PNG to change the mod card image.

After changing manifest or image, rebuild the PCK:

```bash
./scripts/bash/make_pck.sh
./scripts/bash/install_to_game.sh
```

## View logs without Godot

Run the included web log viewer:

```bash
cd ./scripts/tools/log_viewer
./run.sh
```

Then open:

```text
http://127.0.0.1:8765
```

Default log path:
- auto-detected from `/mnt/c/Users/*/AppData/Roaming/SlayTheSpire2/logs/godot.log`

You can override with:

```bash
LOG_VIEWER_LOG_PATH="/mnt/c/Users/<you>/AppData/Roaming/SlayTheSpire2/logs/godot.log" ./run.sh
```

## Optional debugging

Remote debug launch option:

```text
--remote-debug tcp://127.0.0.1:6007
```

Then connect from Godot editor Output/Remote inspector.
