# FullAuto

Hold the fire button to shoot any gun full-auto in Sons of the Forest.

Works with the pistol, revolver, shotgun, rifle and other guns. The fire rate is
adjustable and your settings are saved between sessions.

## Multiplayer

Client side only. Nothing gets installed on a dedicated server, and other
players do not need the mod.

## Installation

### Step 1: Install RedLoader

[RedLoader](https://github.com/ToniMacaroni/RedLoader/releases/latest) is the mod
loader for Sons of the Forest. The game cannot load any mod without it, so
install it first. You only have to do this once.

1. Download `RedLoader.zip` from the
   [latest RedLoader release](https://github.com/ToniMacaroni/RedLoader/releases/latest)
2. Extract it into your Sons of the Forest folder, the one containing
   `SonsOfTheForest.exe`, usually
   `C:\Program Files (x86)\Steam\steamapps\common\Sons Of The Forest`
3. Launch the game once and wait until you reach the main menu. The first launch
   takes a few minutes while RedLoader processes the game files
4. Check that `MODS` appears on the main menu, then quit

### Step 2: Install FullAuto

1. Download `FullAuto.zip` from the
   [latest release](https://github.com/JustinOros/sotf-fullauto/releases/latest)
2. Extract it into the `Mods` folder inside your game folder
3. You should end up with:

```
Mods\FullAuto.dll
Mods\FullAuto\manifest.json
```

## Usage

Equip a gun and hold the fire button.

| Control | Action |
| --- | --- |
| F8 | Toggle the mod on or off |
| F1 | Open the console |

Console commands:

| Command | Action |
| --- | --- |
| `fullauto` | Show current settings |
| `fullauto on` | Enable |
| `fullauto off` | Disable |
| `fullauto rpm 1200` | Set the fire rate, 60 to 6000 rounds per minute |
| `fullauto mode shoot` | Change the fire method, `shoot`, `fire` or `trigger` |

The default is 900 rpm with mode `shoot`. If a gun does not fire full-auto, try
another mode.

Settings are saved to `UserData\FullAuto.cfg` in your game folder.

## Excluded weapons

The bow, slingshot, rope gun and thrown items such as grenades, molotovs, time
bombs and rocks keep their normal behavior.

## Building from source

Requires the .NET 8 SDK and RedLoader installed with its game assemblies
generated.

```powershell
.\build.ps1 -Install
```

Use `-Package` to build `FullAuto.zip` for a release. Pass `-GameDir "path"` if
the game is not found automatically.

## Troubleshooting

Check `_RedLoader\Latest.log` in your game folder. FullAuto logs a line when it
