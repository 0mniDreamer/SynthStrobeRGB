# SynthStrobeRGB

A MelonLoader mod for **Synth Riders** that overrides the stage strobe lighting so it uses the full RGB colour range instead of the four colours derived from your hand orbs. By default it cycles a smooth rainbow; it can also run random colours or your own palette.

It covers all three of the game's lighting systems automatically:

- **Official tile stages** — the stage tile/panel lighting.
- **Custom SDK stages** — the strobe lighting on community-made stages.
- **Built-in environment stages** — the neon-strip lighting on the city/skyline-style stages.

The strobe's timing, intensity, on/off pulse and HDR bloom are preserved; only the colour changes.

## Requirements

- Synth Riders (works on Unity 2021 and also on Unity 6 game branches)
- MelonLoader 0.7.2 or newer (.NET 6 / IL2CPP)

## Install

1. Install MelonLoader into Synth Riders and run the game once.
2. Place `SynthStrobeRGB.dll` in `SynthRiders/Mods/`.
3. Launch. The mod creates its config on first run.

## Configuration

Settings live in `SynthRiders/UserData/MelonPreferences.cfg` under `[SynthStrobeRGB]`, and each is documented inline in that file. Press **F6** in-game to reload after editing.

| Option | Default | Description |
| --- | --- | --- |
| `Enabled` | `true` | Master on/off for the whole mod. When off, stages render their stock colours. |
| `Mode` | `HueCycle` | `HueCycle` (smooth rainbow), `RandomRGB` (random colours), or `Palette` (cycle the `Palette` list). |
| `HueCycle_TimeBased` | `true` | HueCycle: hue follows real time (whole stage shares a drifting colour). If false, advances per tile on the official stage. |
| `HueCycle_Speed` | `0.12` | Rainbow cycles per second. ~0.1 gentle, ~0.4 fast. |
| `HueCycle_StepPerTile` | `0.03` | Hue advance per tile when `HueCycle_TimeBased` is false (official stage only). |
| `Saturation` | `1.0` | Colour saturation, 0–1. |
| `Value` | `1.0` | Colour brightness of the generated hue, 0–1. Best left at 1.0. |
| `PreserveBrightness` | `true` | Scale each colour by the original flash intensity so the pulse and bloom survive. |
| `MinBrightnessToRecolor` | `0.04` | Surfaces dimmer than this are treated as "off" and left untouched, preserving the strobe rhythm. |
| `Palette` | red/green/blue/yellow/cyan/magenta | Palette-mode colours as `r,g,b; r,g,b; ...` (channels 0–1). |

### Keys

- **F6** — reload the config file.
- **F5** — force a re-scan for the current stage's lighting (only needed in the rare case a stage swap isn't detected automatically).

## Building from source

Edit `<GamePath>` in `SynthStrobeRGB.csproj` to your install folder, then:

```
dotnet build -c Release
```

References resolve from the game's `MelonLoader` folders; no game assembly reference is required, which is what lets one build run on both game branches.

## Notes

The mod identifies the game's lighting by name at runtime, so it should work across both Unity branches without separate builds. If a future game or stage-SDK update renames the relevant classes/shaders or the stage container objects, lighting on some stages could revert to stock until the mod is updated — pressing **F5** is the first thing to try if a custom stage ever loads without colour.

Note that the mod does not affect the hand orb colours, which remain the same as in stock Synth Riders. The mod only changes the stage lighting.

## Score chasers use at your own risk : the mod does not change the timing of the strobe, but the new colours may make it harder to see the beat cues on some stages.

## Credits
- Synth Riders by Kluge Interactive
- MelonLoader by LavaGang
