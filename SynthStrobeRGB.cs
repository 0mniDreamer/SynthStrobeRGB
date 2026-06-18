// SynthStrobeRGB
// Overrides Synth Riders' strobe / stage lighting to use the full RGB range instead of the four
// hand-orb colours, across three lighting systems:
//
//   1. Official tile stages  — Harmony prefix on Util_StageInteractions.DoMatColor (beat-event driven).
//   2. Custom SDK stages     — drive _Color on the "Strobereciever" source material; a camera renders
//                              it into the RT every downstream strobe surface samples.
//   3. Built-in environment  — animated emissive lighting (URP/Lit _EmissionColor, the "Buildings"
//      stages (e.g. Stage 02)  shader, and "DynamicLights Opaque" neon strips), scoped under [Stage].
//
// Systems 2 and 3 are keyframed/texture-driven with no patchable chokepoint, so they're handled by a
// per-frame LateUpdate driver that recolours after the game writes. The driver recolours only lit
// surfaces (near-black "off" is left alone) and preserves the original per-surface brightness so the
// pulse and HDR bloom survive. The [Stage]-scoped rules stand down whenever the tile system (system 1)
// is actively driving, so working tile stages keep their beat-synced behaviour.
//
// Config: UserData/MelonPreferences.cfg -> [SynthStrobeRGB]. Press F6 in-game to reload.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: MelonInfo(typeof(SynthStrobeRGB.Mod), "SynthStrobeRGB", "1.5.3", "OmniDreamer")]
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
        // driver (custom + built-in stages)
        private MelonPreferences_Entry<bool> _customEnabled;
        private MelonPreferences_Entry<string> _customProps;
        private MelonPreferences_Entry<string> _customNameFilter;
        private MelonPreferences_Entry<bool> _builtinEnabled;
        private MelonPreferences_Entry<string> _builtinRules;
        private MelonPreferences_Entry<float> _rescan;
        private MelonPreferences_Entry<float> _settle;
        private MelonPreferences_Entry<string> _watchObjects;
        private MelonPreferences_Entry<float> _interval;
        private MelonPreferences_Entry<string> _exclude;

        public override void OnInitializeMelon()
        {
            _cfg = MelonPreferences.CreateCategory("SynthStrobeRGB");

            _enabled = _cfg.CreateEntry("Enabled", true,
                description: "Master toggle for the official-stage Harmony patch (Util_StageInteractions.DoMatColor).");
            _mode = _cfg.CreateEntry("Mode", "HueCycle",
                description: "Colour mode: HueCycle (smooth rainbow), RandomRGB, or Palette (cycle the Palette list). Shared by all systems.");
            _timeBased = _cfg.CreateEntry("HueCycle_TimeBased", true,
                description: "HueCycle only (official stage). True: hue follows real time. False: hue advances per tile.");
            _hueSpeed = _cfg.CreateEntry("HueCycle_Speed", 0.12f,
                description: "HueCycle cycles per second. Drives the official stage (when time-based) and the per-frame driver.");
            _hueStep = _cfg.CreateEntry("HueCycle_StepPerTile", 0.03f,
                description: "HueCycle when TimeBased is false. Hue advance (0..1) per tile. Official stage only.");
            _saturation = _cfg.CreateEntry("Saturation", 1.0f, description: "Colour saturation 0..1.");
            _value = _cfg.CreateEntry("Value", 1.0f,
                description: "Generated colour value 0..1. Keep at 1.0 so brightness-preservation stays drift-free on HDR emissive surfaces.");
            _preserveBrightness = _cfg.CreateEntry("PreserveBrightness", true,
                description: "Scale each colour by the surface's original intensity so the pulse/bloom and on-off rhythm survive. Applies to all systems.");
            _minBrightness = _cfg.CreateEntry("MinBrightnessToRecolor", 0.04f,
                description: "Surfaces dimmer than this are treated as 'off' and left untouched. Applies to all systems.");
            _palette = _cfg.CreateEntry("Palette", "1,0,0; 0,1,0; 0,0,1; 1,1,0; 0,1,1; 1,0,1",
                description: "Palette mode colours as 'r,g,b; r,g,b; ...' (channels 0..1).");
            _logCalls = _cfg.CreateEntry("LogInterceptedCalls", false,
                description: "Diagnostic: log intercepted official-stage DoMatColor calls (throttled).");

            _customEnabled = _cfg.CreateEntry("CustomStage_Enabled", true,
                description: "Drive custom SDK stages via the Strobereciever source material.");
            _customProps = _cfg.CreateEntry("CustomStage_Properties", "_Color",
                description: "Colour properties to drive on the custom-stage source material, comma-separated.");
            _customNameFilter = _cfg.CreateEntry("CustomStage_MaterialNameFilter", "reciever",
                description: "Material-name substring identifying the custom-stage source (SDK spelling: 'Strobereciever').");

            _builtinEnabled = _cfg.CreateEntry("BuiltinStage_Enabled", true,
                description: "Drive built-in environment stages whose lighting is animated emissive/neon (e.g. Stage 02).");
            _builtinRules = _cfg.CreateEntry("BuiltinStage_Rules",
                "DynamicLights Opaque|[Stage]|_Color",
                description: "Rules as 'shaderContains|pathContains|property', semicolon-separated. Default targets only the neon strips (DynamicLights Opaque). Optional add-ons (usually look worse and cost more): '; Buildings|[Stage]|_EmissionColor' for the Buildings-shader glow, '; Universal Render Pipeline/Lit|[Stage]|_EmissionColor' for emissive scenery. Path scope keeps these off props; they pause while the tile system drives.");

            _rescan = _cfg.CreateEntry("Driver_RescanDelaySeconds", 1.0f,
                description: "Delay after a stage loads before the first scan for target materials.");
            _settle = _cfg.CreateEntry("Driver_StageDetectSeconds", 12.0f,
                description: "After a stage loads, keep re-checking for this long to catch parts that instantiate late (e.g. custom-stage receivers appearing in sequence). Scanning never runs during steady play. Press F5 to force a rescan if a late swap is ever missed.");
            _watchObjects = _cfg.CreateEntry("Driver_WatchObjects", "PlatformAnchor,[Stage]",
                description: "Comma-separated object names whose child-count is watched to detect a stage loading/unloading (custom stages instantiate into PlatformAnchor; built-in stages under [Stage]). Cheap to watch; triggers a rescan when they change.");
            _interval = _cfg.CreateEntry("Driver_ColorIntervalSeconds", 0.4f,
                description: "RandomRGB/Palette modes: how long to hold each colour before switching. HueCycle ignores this.");
            _exclude = _cfg.CreateEntry("Driver_ExcludeMaterials", "",
                description: "Comma-separated material-name substrings to leave untouched (reverts them to stock).");

            LoadConfig();
            ApplyPatch();

            LoggerInstance.Msg("Loaded. Edit MelonPreferences.cfg [SynthStrobeRGB] and press F6 in-game to reload.");
        }

        public override void OnLateUpdate() => MaterialDriver.Tick(LoggerInstance);

        // Stage/scene changed — let the driver rescan immediately instead of polling for it.
        public override void OnSceneWasInitialized(int buildIndex, string sceneName) => MaterialDriver.MarkDirty();

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
                else if (Input.GetKeyDown(KeyCode.F5))
                {
                    MaterialDriver.MarkDirty();
                    LoggerInstance.Msg("Manual rescan requested.");
                }
            }
            catch { /* legacy Input unavailable on this build */ }
        }

        public override void OnDeinitializeMelon() => MaterialDriver.Restore();

        private void ApplyPatch()
        {
            var doMatColor = Finder.Method("Util_StageInteractions", "DoMatColor",
                                           typeof(Material), typeof(Color), typeof(bool));
            if (doMatColor != null)
            {
                HarmonyInstance.Patch(doMatColor,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(Patches), nameof(Patches.DoMatColorPrefix))));
                LoggerInstance.Msg("Official-stage strobe override active.");
            }
            else
            {
                LoggerInstance.Warning("Could not resolve Util_StageInteractions.DoMatColor — official-stage override inactive " +
                                       "(custom and built-in passes still run if enabled).");
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

            // Build the driver's rule set from config.
            var rules = new List<DriveRule>();
            if (_customEnabled.Value)
            {
                string nameFilter = (_customNameFilter.Value ?? "").Trim();
                foreach (var prop in ParseCsv(_customProps.Value))
                    rules.Add(new DriveRule(shaderContains: "", nameContains: nameFilter, pathContains: "", prop: prop, gateOnTile: false));
            }
            if (_builtinEnabled.Value)
            {
                foreach (var raw in (_builtinRules.Value ?? "").Split(';'))
                {
                    var parts = raw.Split('|');
                    if (parts.Length < 3) continue;
                    string prop = parts[2].Trim();
                    if (prop.Length == 0) continue;
                    rules.Add(new DriveRule(parts[0].Trim(), "", parts[1].Trim(), prop, gateOnTile: true));
                }
            }

            MaterialDriver.ScanDelay = Mathf.Max(0f, _rescan.Value);
            MaterialDriver.SettleSeconds = Mathf.Max(0f, _settle.Value);
            MaterialDriver.SetWatchObjects(ParseCsv(_watchObjects.Value));
            MaterialDriver.ColorInterval = Mathf.Max(0f, _interval.Value);
            MaterialDriver.Excludes = ParseCsv(_exclude.Value);
            MaterialDriver.SetRules(rules);   // restores + forces rescan
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

        private static List<string> ParseCsv(string csv)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(csv)) return list;
            foreach (var p in csv.Split(','))
            {
                var s = p.Trim();
                if (s.Length > 0 && !list.Contains(s)) list.Add(s);
            }
            return list;
        }

        private static bool TryF(string s, out float f) =>
            float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f);
    }

    // ---- official-stage Harmony patch ----
    internal static class Patches
    {
        public static bool LogCalls;
        public static float LastFire = -999f;   // set whenever the tile system writes a colour
        private static float _lastLog;

        // names match the original: DoMatColor(Material mat, Color controllColor, bool isLine)
        public static void DoMatColorPrefix(ref Color controllColor, bool isLine)
        {
            LastFire = Time.realtimeSinceStartup;   // lets the driver's [Stage] rules stand down here
            if (!Rgb.ShouldRecolor(controllColor)) return;
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

    internal struct DriveRule
    {
        public string ShaderContains, NameContains, PathContains, Prop;
        public int PropId;
        public bool GateOnTile;
        public DriveRule(string shaderContains, string nameContains, string pathContains, string prop, bool gateOnTile)
        {
            ShaderContains = shaderContains ?? "";
            NameContains = nameContains ?? "";
            PathContains = pathContains ?? "";
            Prop = prop;
            PropId = Shader.PropertyToID(prop);
            GateOnTile = gateOnTile;
        }
    }

    // ---- per-frame material driver (custom + built-in stages) ----
    internal static class MaterialDriver
    {
        public static float ScanDelay = 1f;            // wait this long after a scene load before scanning
        public static float SettleSeconds = 12f;       // keep re-checking this long to catch late-loading stages
        public static float ColorInterval = 0.4f;
        public static List<string> Excludes = new List<string>();

        private const float MinScanInterval = 2f;      // coalesce bursts of scene-load events
        private const float SettleInterval = 2.5f;      // cadence of re-checks during the settle window
        private const float CheckInterval = 0.75f;      // cadence of the cheap load detector

        private static List<DriveRule> _rules = new List<DriveRule>();
        private static readonly List<Target> _targets = new List<Target>();
        private static readonly Dictionary<long, Color> _originals = new Dictionary<long, Color>();
        private static readonly Dictionary<int, string> _shaderNames = new Dictionary<int, string>(); // shaderId -> name
        private static float _scanAt = 0f, _lastScanTime = -999f, _lastColorChange, _settleUntil = 0f;
        private static float _lastCheck = -999f;
        private static int _lastCamCount = -1, _lastSceneCount = -1, _lastReported = -1;

        private class Watch { public string Name; public Transform Tf; public int Count = -1; }
        private static List<Watch> _watches = new List<Watch>();

        public static void SetWatchObjects(List<string> names)
        {
            _watches = new List<Watch>();
            if (names == null) return;
            foreach (var n in names)
                if (!string.IsNullOrEmpty(n)) _watches.Add(new Watch { Name = n });
        }
        private static bool _scanPending = true;        // scan once on first eligible frame
        private static bool _anyNameRule;
        private static Color _current = Color.white;
        private static int _palIdx;
        private static bool _warned;

        private struct Target { public Material Mat; public int PropId; public bool Gate; }

        // Called from a scene-load event or the manual rescan key: schedule a (debounced) rescan.
        public static void MarkDirty()
        {
            _scanPending = true;
            _scanAt = Time.realtimeSinceStartup + ScanDelay;
            _settleUntil = Time.realtimeSinceStartup + SettleSeconds;   // keep re-checking for late stages
            _shaderNames.Clear();
        }

        private static bool Has(string s, string token) =>
            token.Length == 0 || (s != null && s.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);

        public static void SetRules(List<DriveRule> rules)
        {
            Restore();              // put back anything we'd tinted under the old rule set
            _rules = rules ?? new List<DriveRule>();
            _anyNameRule = false;
            foreach (var r in _rules)
                if (r.NameContains.Length > 0) { _anyNameRule = true; break; }
            MarkDirty();            // rescan with the new rules
        }

        public static void Tick(MelonLogger.Instance log)
        {
            if (_rules.Count == 0) return;
            try
            {
                float now = Time.realtimeSinceStartup;

                // Cheap, churn-resistant load detector. Scene/camera counts catch full scene loads, but
                // custom stages instantiate into the existing scene (no scene change) and their receiver
                // cameras are disabled (manual RT render, so not in allCamerasCount). So we also watch the
                // child count of known anchor objects (e.g. PlatformAnchor) — that changes the moment a
                // stage's content is instantiated. All of these are cheap reads; the GameObject.Find only
                // runs while an anchor reference is missing.
                if (now - _lastCheck >= CheckInterval)
                {
                    _lastCheck = now;
                    bool changed = false;

                    int cams = Camera.allCamerasCount;
                    int scenes = SceneManager.sceneCount;
                    if (cams != _lastCamCount || scenes != _lastSceneCount) changed = true;
                    _lastCamCount = cams; _lastSceneCount = scenes;

                    foreach (var w in _watches)
                    {
                        if (w.Tf == null)
                        {
                            var go = GameObject.Find(w.Name);
                            w.Tf = (go != null) ? go.transform : null;
                            if (w.Tf == null) { if (w.Count != -1) { changed = true; w.Count = -1; } continue; }
                        }
                        int c = w.Tf.childCount;
                        if (c != w.Count) { changed = true; w.Count = c; }
                    }

                    if (changed) MarkDirty();   // a stage loaded/unloaded — open a scan window
                }

                // Scan only on demand (detector / scene load / config reload / manual key), never during
                // steady play. Re-check through the whole settle window so every part of a stage that
                // instantiates over time (e.g. the four receivers appearing in sequence) gets picked up.
                if (_scanPending && now >= _scanAt && now - _lastScanTime >= MinScanInterval)
                {
                    Rescan();
                    _lastScanTime = now;
                    if (_targets.Count != _lastReported)
                    {
                        _lastReported = _targets.Count;
                        log.Msg($"Driver: now driving {_targets.Count} material(s).");
                    }
                    if (now < _settleUntil)
                        _scanAt = now + SettleInterval;   // keep re-checking; late-loading parts get added
                    else
                        _scanPending = false;             // window closed — done scanning
                }

                if (_targets.Count == 0) return;

                bool tileActive = (now - Patches.LastFire) < 0.5f;
                Color hue = NextTint();

                foreach (var t in _targets)
                {
                    if (t.Mat == null) continue;
                    if (t.Gate && tileActive) continue;   // leave [Stage] lighting to the tile patch when it's driving

                    Color cur = t.Mat.GetColor(t.PropId);
                    float b = Mathf.Max(cur.r, Mathf.Max(cur.g, cur.b));   // current intensity (may be HDR > 1)
                    if (b < Rgb.MinBrightness) continue;                   // "off" — leave alone

                    Color outc = hue;
                    if (Rgb.PreserveBrightness) { outc.r *= b; outc.g *= b; outc.b *= b; }
                    outc.a = cur.a;
                    t.Mat.SetColor(t.PropId, outc);
                }
            }
            catch (Exception e)
            {
                if (!_warned) { _warned = true; log.Warning($"Driver error (suppressing further): {e.Message}"); }
            }
        }

        private static Color NextTint()
        {
            switch (Rgb.Mode)
            {
                case "Palette":
                case "RandomRGB":
                    if (Time.realtimeSinceStartup - _lastColorChange >= ColorInterval)
                    {
                        _lastColorChange = Time.realtimeSinceStartup;
                        _current = (Rgb.Mode == "Palette" && Rgb.Palette.Count > 0)
                            ? Rgb.Palette[_palIdx++ % Rgb.Palette.Count]
                            : Color.HSVToRGB(UnityEngine.Random.value, Rgb.Sat, Rgb.Val);
                    }
                    return _current;
                default: // HueCycle — continuous, time-driven
                    return Color.HSVToRGB(Mathf.Repeat(Time.time * Rgb.HueSpeed, 1f), Rgb.Sat, Rgb.Val);
            }
        }

        private static string ShaderName(Material m)
        {
            var sh = m.shader;
            if (sh == null) return "";
            int id = sh.GetInstanceID();
            if (_shaderNames.TryGetValue(id, out var n)) return n;
            n = sh.name ?? "";
            _shaderNames[id] = n;        // ~dozens of distinct shaders, not tens of thousands
            return n;
        }

        private static void Rescan()
        {
            _targets.Clear();
            if (_rules.Count == 0) return;
            var seen = new HashSet<long>();

            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root == null) continue;
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r == null) continue;

                        string path = null;   // built lazily, only if a matching rule needs it
                        var mats = r.sharedMaterials;
                        for (int mi = 0; mi < mats.Count; mi++)
                        {
                            var m = mats[mi];
                            if (m == null) continue;

                            // Cheap pre-checks first (no allocations): shader and material name via IndexOf.
                            string shaderName = ShaderName(m);
                            string matName = _anyNameRule ? m.name : null;
                            int matId = m.GetInstanceID();

                            foreach (var rule in _rules)
                            {
                                if (!Has(shaderName, rule.ShaderContains)) continue;
                                if (rule.NameContains.Length > 0 && !Has(matName, rule.NameContains)) continue;
                                if (rule.PathContains.Length > 0)
                                {
                                    if (path == null) path = GetPath(r.transform);   // built once, only when needed
                                    if (!Has(path, rule.PathContains)) continue;
                                }

                                long key = ((long)matId << 32) ^ (uint)rule.PropId;
                                if (!seen.Add(key)) continue;                 // (material, property) once

                                if (IsExcluded(m.name)) { RestoreOne(m, rule.PropId, key); continue; }
                                if (!m.HasProperty(rule.PropId)) continue;

                                if (!_originals.ContainsKey(key)) _originals[key] = m.GetColor(rule.PropId);
                                _targets.Add(new Target { Mat = m, PropId = rule.PropId, Gate = rule.GateOnTile });
                            }
                        }
                    }
                }
            }
        }

        private static bool IsExcluded(string matName)
        {
            foreach (var ex in Excludes)
                if (ex.Length > 0 && Has(matName, ex)) return true;
            return false;
        }

        private static void RestoreOne(Material m, int propId, long key)
        {
            if (m != null && _originals.TryGetValue(key, out var orig)) m.SetColor(propId, orig);
        }

        public static void Restore()
        {
            foreach (var t in _targets)
            {
                if (t.Mat == null) continue;
                long key = ((long)t.Mat.GetInstanceID() << 32) ^ (uint)t.PropId;
                if (_originals.TryGetValue(key, out var orig)) t.Mat.SetColor(t.PropId, orig);
            }
            _targets.Clear();
            _originals.Clear();
        }

        private static string GetPath(Transform t)
        {
            var sb = new System.Text.StringBuilder(t.name);
            var p = t.parent;
            while (p != null) { sb.Insert(0, p.name + "/"); p = p.parent; }
            return sb.ToString();
        }
    }

    // ---- colour generation (official stage) ----
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
            else
                baseCol = Color.HSVToRGB(NextHue(), Sat, Val);

            if (PreserveBrightness)
            {
                float b = Brightness(incoming);
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

    // ---- namespace-agnostic method resolver ----
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