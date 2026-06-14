# SynthStrobeRGB

A MelonLoader mod for **Synth Riders** that overrides the stage strobe lighting so it uses the full RGB colour range instead of the four colours derived from your hand orbs.

## What it does

Synth Riders colours its stage tile lighting by writing one of four orb-derived colours onto each tile. This mod intercepts that colour as it's applied (`Util_StageInteractions.DoMatColor`) and replaces it with a generated colour — a smooth rainbow by default. The game's flash timing, intensity, and on/off rhythm are untouched; only the hue changes. Tiles being turned off are left black so the strobe pattern is preserved.

The target method is resolved by name at runtime, so a single build works on both the Unity 2021.3 and Unity 6 branches of the game.

## Install

1. Install MelonLoader (0.6.x, .NET 6) into Synth Riders and run the game once.
2. Drop `SynthStrobeRGB.dll` into `Synth Riders/Mods/`.
3. Launch. The mod creates its config on first run.

## Configuration

Settings live in `Synth Riders/UserData/MelonPreferences.cfg` under `[SynthStrobeRGB]`. Each entry is documented inline in that file. 

The main ones:

- `Mode` — `HueCycle` (smooth rainbow, default), `RandomRGB` (random colour per tile), or `Palette` (cycle your own list).
- `HueCycle_Speed` — rainbow speed when in HueCycle/time-based mode.
- `Palette` — your own colours as `r,g,b; r,g,b; ...` (channels 0–1) for Palette mode.
- `PreserveBrightness` — keep the game's original flash intensity per tile (recommended on).
- `Saturation` / `Value` — tune how vivid the colours are.

## Building from source

Edit `<GamePath>` in `SynthStrobeRGB.csproj` to your install folder, then `dotnet build -c Release`. References resolve from the game's `MelonLoader` folders; no game assembly reference is required.

## Notes

If the mod logs that it could not resolve `Util_StageInteractions.DoMatColor`, the game's lighting class changed on your version — the override will simply stay inactive rather than break anything. Set `LogInterceptedCalls = true` to diagnose.
