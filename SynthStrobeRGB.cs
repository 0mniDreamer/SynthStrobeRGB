// SynthStrobeRGB
// Overrides Synth Riders' stage tile lighting (Util_StageInteractions.DoMatColor) so the
// strobe uses the full RGB range instead of the four hand-orb colours.
//
// How it works: every lit tile colour is written through DoMatColor(mat, controllColor, isLine).
// A Harmony prefix replaces controllColor with a generated colour. "Off"/near-black resets are
// left untouched so the strobe rhythm is preserved. The target method is resolved by simple type
// name at runtime, so the same build works regardless of interop namespace and across both the
// Unity 2021.3 and Unity 6 game branches.
//
// Config: UserData/MelonPreferences.cfg -> [SynthStrobeRGB]. Press F6 in-game to reload it.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(SynthStrobeRGB.Mod), "SynthStrobeRGB", "1.0.0", "OmniDreamer_")]
[assembly: MelonGame("Kluge Interactive", "SynthRiders")]

namespace SynthStrobeRGB
{
    public class Mod : MelonMod
    {
        private MelonPreferences_Category _cfg;
        private MelonPreferences_Entry<bool> _enabled;
        private MelonPreferences_Entry<string> _mode;
        private MelonPreferences_Entry<bool> _timeBased;
        private MelonPreferences_Entry<float> _hueSpeed;
        private MelonPreferences_Entry<float> _hueStep;
        private MelonPreferences_Entry<float> _saturation;
        private MelonPreferences_Entry<float> _value;
        private MelonPreferences_Entry<bool> _preserveBrightness;
        private MelonPreferences_Entry<float> _minBrightness;
        private MelonPreferences_Entry<string> _palette;
        private MelonPreferences_Entry<bool> _logCalls;

        public override void OnInitializeMelon()
        {
            _cfg = MelonPreferences.CreateCategory("SynthStrobeRGB");

            _enabled = _cfg.CreateEntry("Enabled", true,
                description: "Master toggle for the RGB strobe override.");
            _mode = _cfg.CreateEntry("Mode", "HueCycle",
                description: "Colour mode: HueCycle (smooth rainbow), RandomRGB (random per tile), or Palette (cycle the Palette list).");
            _timeBased = _cfg.CreateEntry("HueCycle_TimeBased", true,
                description: "HueCycle only. True: hue follows real time so the whole stage shares a colour that drifts smoothly. False: hue advances per tile (busier, gradient-like).");
            _hueSpeed = _cfg.CreateEntry("HueCycle_Speed", 0.12f,
                description: "HueCycle + TimeBased. Rainbow cycles per second. ~0.1 gentle, ~0.4 fast.");
            _hueStep = _cfg.CreateEntry("HueCycle_StepPerTile", 0.03f,
                description: "HueCycle when TimeBased is false. Hue advance (0..1) per coloured tile.");
            _saturation = _cfg.CreateEntry("Saturation", 1.0f,
                description: "Colour saturation 0..1. Lower = pastel/washed out.");
            _value = _cfg.CreateEntry("Value", 1.0f,
                description: "Colour value/brightness 0..1 of the generated hue, before PreserveBrightness scaling.");
            _preserveBrightness = _cfg.CreateEntry("PreserveBrightness", true,
                description: "Scale each generated colour by the game's original flash intensity, so the strobe keeps its rhythm and bloom. False = constant full-intensity colour.");
            _minBrightness = _cfg.CreateEntry("MinBrightnessToRecolor", 0.04f,
                description: "Tiles whose incoming colour is dimmer than this are treated as 'off' and left black, preserving the on/off strobe pattern.");
            _palette = _cfg.CreateEntry("Palette", "1,0,0; 0,1,0; 0,0,1; 1,1,0; 0,1,1; 1,0,1",
                description: "Palette mode colours as 'r,g,b; r,g,b; ...' with each channel 0..1.");
            _logCalls = _cfg.CreateEntry("LogInterceptedCalls", false,
                description: "Diagnostic: log intercepted DoMatColor calls (throttled). Leave off for normal play.");

            LoadConfig();
            ApplyPatch();

            LoggerInstance.Msg("Loaded. Edit MelonPreferences.cfg [SynthStrobeRGB] and press F6 in-game to reload.");
        }

        public override void OnUpdate()
        {
            try
            {
                if (Input.GetKeyDown(KeyCode.F6))
                {
                    _cfg.LoadFromFile();
                    LoadConfig();
                    LoggerInstance.Msg("Config reloaded.");
                }
            }
            catch { /* legacy Input unavailable on this build; F6 reload simply won't fire */ }
        }

        private void ApplyPatch()
        {
            // Resolve by simple type name — namespace-agnostic and branch-agnostic.
            var doMatColor = Finder.Method("Util_StageInteractions", "DoMatColor",
                                           typeof(Material), typeof(Color), typeof(bool));
            if (doMatColor != null)
            {
                HarmonyInstance.Patch(doMatColor,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(Patches), nameof(Patches.DoMatColorPrefix))));
                LoggerInstance.Msg("Full-RGB strobe override active.");
            }
            else
            {
                LoggerInstance.Warning("Could not resolve Util_StageInteractions.DoMatColor — strobe override is inactive. " +
                                       "The game's lighting class may have changed on this version.");
            }
        }

        private void LoadConfig()
        {
            Rgb.Enabled = _enabled.Value;
            Rgb.Mode = NormalizeMode(_mode.Value);
            Rgb.TimeBased = _timeBased.Value;
            Rgb.HueSpeed = Mathf.Max(0f, _hueSpeed.Value);
            Rgb.HueStep = Mathf.Max(0f, _hueStep.Value);
            Rgb.Sat = Mathf.Clamp01(_saturation.Value);
            Rgb.Val = Mathf.Clamp01(_value.Value);
            Rgb.PreserveBrightness = _preserveBrightness.Value;
            Rgb.MinBrightness = Mathf.Max(0f, _minBrightness.Value);
            Rgb.Palette = ParsePalette(_palette.Value);
            Patches.LogCalls = _logCalls.Value;
        }

        private string NormalizeMode(string m)
        {
            switch ((m ?? "").Trim().ToLowerInvariant())
            {
                case "randomrgb": return "RandomRGB";
                case "palette": return "Palette";
                case "huecycle": return "HueCycle";
                default:
                    LoggerInstance.Warning($"Unknown Mode '{m}', falling back to HueCycle.");
                    return "HueCycle";
            }
        }

        private List<Color> ParsePalette(string csv)
        {
            var list = new List<Color>();
            if (string.IsNullOrWhiteSpace(csv)) return list;
            foreach (var entry in csv.Split(';'))
            {
                var parts = entry.Trim().Split(',');
                if (parts.Length < 3) continue;
                if (TryF(parts[0], out var r) && TryF(parts[1], out var g) && TryF(parts[2], out var b))
                    list.Add(new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), 1f));
            }
            return list;
        }

        private static bool TryF(string s, out float f) =>
            float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f);
    }

    internal static class Patches
    {
        public static bool LogCalls;
        private static float _lastLog;

        // Parameter names match the original signature so Harmony binds by name:
        //   DoMatColor(Material mat, Color controllColor, bool isLine)
        public static void DoMatColorPrefix(ref Color controllColor, bool isLine)
        {
            if (!Rgb.ShouldRecolor(controllColor)) return;   // leave "off"/black resets alone
            Color outc = Rgb.Make(controllColor);

            if (LogCalls && Time.realtimeSinceStartup - _lastLog > 0.5f)
            {
                _lastLog = Time.realtimeSinceStartup;
                MelonLogger.Msg($"[DoMatColor] isLine={isLine} in=({controllColor.r:0.00},{controllColor.g:0.00},{controllColor.b:0.00}) " +
                                $"out=({outc.r:0.00},{outc.g:0.00},{outc.b:0.00})");
            }
            controllColor = outc;
        }
    }

    internal static class Rgb
    {
        public static bool Enabled = true;
        public static string Mode = "HueCycle";
        public static bool TimeBased = true;
        public static float HueSpeed = 0.12f, HueStep = 0.03f;
        public static float Sat = 1f, Val = 1f;
        public static bool PreserveBrightness = true;
        public static float MinBrightness = 0.04f;
        public static List<Color> Palette = new List<Color>();

        private static float _hue;
        private static int _palIdx;

        public static bool ShouldRecolor(Color c) => Enabled && Brightness(c) >= MinBrightness;

        public static Color Make(Color incoming)
        {
            Color baseCol;
            if (Mode == "RandomRGB")
                baseCol = Color.HSVToRGB(UnityEngine.Random.value, Sat, Val);
            else if (Mode == "Palette" && Palette.Count > 0)
                baseCol = Palette[_palIdx++ % Palette.Count];
            else // HueCycle (also the Palette fallback when the list is empty)
                baseCol = Color.HSVToRGB(NextHue(), Sat, Val);

            if (PreserveBrightness)
            {
                float b = Brightness(incoming);   // keep the game's intended flash intensity (incl. HDR bloom)
                baseCol.r *= b; baseCol.g *= b; baseCol.b *= b;
            }
            baseCol.a = incoming.a;
            return baseCol;
        }

        private static float NextHue() =>
            TimeBased ? Mathf.Repeat(Time.time * HueSpeed, 1f)
                      : (_hue = Mathf.Repeat(_hue + HueStep, 1f));

        private static float Brightness(Color c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b));
    }

    internal static class Finder
    {
        public static MethodBase Method(string typeName, string methodName, params Type[] argTypes)
        {
            var t = FindType(typeName);
            if (t == null) { MelonLogger.Warning($"[Finder] type '{typeName}' not found"); return null; }
            var m = AccessTools.Method(t, methodName, argTypes);
            if (m == null) MelonLogger.Warning($"[Finder] method '{typeName}.{methodName}' not found");
            return m;
        }

        private static Type FindType(string simpleName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = asm.GetName().Name;
                if (name == null || !name.Contains("Assembly-CSharp")) continue;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                foreach (var t in types)
                    if (t != null && t.Name == simpleName) return t;
            }
            return null;
        }
    }
}
