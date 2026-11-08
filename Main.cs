using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace ADOFAIPlusPlus
{
    public static class Main
    {
        public static UnityModManager.ModEntry ModEntry { get; private set; }

        public static UnityModManager.ModEntry.ModLogger Logger
        {
            get
            {
                return ModEntry != null ? ModEntry.Logger : null;
            }
        }

        public static Settings Settings { get; private set; }
        public static bool Enabled { get; private set; }

        private static Harmony _harmony;

        // GUI 文本输入缓冲区
        private static string _countedTimeStr;
        private static string _perfectTimeStr;
        private static string _pureTimeStr;

        private static string _countedAngleStr;
        private static string _perfectAngleStr;
        private static string _pureAngleStr;

        private static string _percentStr = "100";

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;
            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            Settings.SyncValues();

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;

            _harmony = new Harmony(modEntry.Info.Id);

            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;

            if (value)
            {
                PatchAllSafely();
                try
                {
                    Patches.ApplyJapaneseFontPreference();
                    Patches.ApplyModeChange();
                    if (Settings.NoFail) Patches.ApplyNoFail(true);
                    if (Settings.Autoplay) Patches.ApplyAutoplay(true);
                    if (Settings.AutoplayPlus) Patches.ApplyAutoplayPlus(true);
                }
                catch (Exception ex)
                {
                    if (Logger != null) Logger.Error("ApplyModeChange failed: " + ex);
                }
                if (Logger != null)
                {
                    Logger.Log("BetterJudgment enabled with mode: " + Settings.Mode);
                }
            }
            else
            {
                try
                {
                    Patches.RestoreOriginalState();
                    Patches.ApplyNoFail(false);
                    Patches.ApplyAutoplay(false);
                    Patches.ApplyAutoplayPlus(false);
                }
                catch (Exception ex)
                {
                    if (Logger != null) Logger.Error("RestoreOriginalState failed: " + ex);
                }
                _harmony.UnpatchAll(modEntry.Info.Id);
                if (Logger != null)
                {
                    Logger.Log("BetterJudgment disabled.");
                }
            }

            return true;
        }

        private static void PatchAllSafely()
        {
            int ok = 0, failed = 0;
            foreach (Type type in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly()))
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0)
                    continue;

                try
                {
                    _harmony.CreateClassProcessor(type).Patch();
                    ok++;
                }
                catch (Exception ex)
                {
                    failed++;
                    if (Logger != null) Logger.Error("Patch failed: " + type.Name + " -> " + ex.Message);
                }
            }
            if (Logger != null) Logger.Log(string.Format("Patches applied: {0} ok, {1} failed.", ok, failed));
        }

        private static void SyncTextBuffers()
        {
            _countedTimeStr = Settings.CustomCountedTime.ToString("0.##");
            _perfectTimeStr = Settings.CustomPerfectTime.ToString("0.##");
            _pureTimeStr = Settings.CustomPureTime.ToString("0.##");

            _countedAngleStr = Settings.CustomCountedAngle.ToString("0.##");
            _perfectAngleStr = Settings.CustomPerfectAngle.ToString("0.##");
            _pureAngleStr = Settings.CustomPureAngle.ToString("0.##");
        }

        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            if (_countedTimeStr == null)
            {
                SyncTextBuffers();
            }

            AutoplayBot.CaptureWaitingKeyInGUI();

            // -1. 模组总开关与安全警告 (Master Switch & Safety Warning)
            if (!Settings.MasterEnabled)
            {
                GUILayout.BeginVertical("box");
                GUILayout.Label("<b><size=15><color=#ff3333>" + Loc.Get("master.warning_title") + "</color></size></b>");
                GUILayout.Space(6);
                GUILayout.Label("<color=#ffdd88><b>" + Loc.Get("master.warning_body") + "</b></color>");
                GUILayout.Space(10);
                if (GUILayout.Button("<b><size=13>" + Loc.Get("master.agree_btn") + "</size></b>", GUILayout.Height(38)))
                {
                    Settings.MasterEnabled = true;
                    Settings.DisclaimerAccepted = true;
                    Patches.ApplyModeChange();
                    if (Settings.NoFail) Patches.ApplyNoFail(true);
                    if (Settings.Autoplay) Patches.ApplyAutoplay(true);
                    if (Settings.AutoplayPlus) Patches.ApplyAutoplayPlus(true);
                    Settings.Save(modEntry);
                }
                GUILayout.EndVertical();
                return;
            }

            // 总开关开启状态栏
            GUILayout.BeginVertical("box");
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#26ff59><b>● " + Loc.Get("master.status_on") + "</b></color>", GUILayout.Width(350));
            if (GUILayout.Button(Loc.Get("master.disable_btn"), GUILayout.MinWidth(240), GUILayout.ExpandWidth(false)))
            {
                Settings.MasterEnabled = false;
                Patches.RestoreOriginalState();
                Patches.ApplyNoFail(false);
                Patches.ApplyAutoplay(false);
                Patches.ApplyAutoplayPlus(false);
                Settings.Save(modEntry);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(5);

            // 0. 语言设置 (Language Settings)
            GUILayout.BeginVertical("box");
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>" + Loc.Get("ui.language_label") + "</b>", GUILayout.Width(220));
            GUILayout.Label("<i>(" + Loc.CurrentLanguage.ToString() + ")</i>");
            GUILayout.EndHorizontal();
            GUILayout.Space(3);

            string[] langNames = new string[]
            {
                Loc.Get("ui.lang.auto"),
                "简体中文",
                "繁體中文",
                "English",
                "한국어",
                "日本語",
                "Español",
                "Português",
                "Français",
                "Deutsch",
                "Polski",
                "Русский",
                "Tiếng Việt",
                "Čeština",
                "Română"
            };

            int curLangIdx = (int)Settings.Language;
            int newLangIdx = GUILayout.SelectionGrid(curLangIdx, langNames, 5);
            if (newLangIdx != curLangIdx)
            {
                Settings.Language = (ModLanguage)newLangIdx;
                Patches.ApplyJapaneseFontPreference();
                Patches.UpdateHUDText();
            }

            if (Loc.CurrentLanguage == SystemLanguage.Japanese || RDString.language == SystemLanguage.Japanese)
            {
                GUILayout.Space(4);
                bool oldJpLoc = Settings.BetterJapaneseLocalization;
                bool newJpLoc = GUILayout.Toggle(
                    oldJpLoc,
                    " " + Loc.Get("ui.japanese_better_loc"));
                if (newJpLoc != oldJpLoc)
                {
                    Settings.BetterJapaneseLocalization = newJpLoc;
                    Patches.ApplyModeChange();
                }

                bool oldJpFont = Settings.JapaneseUseChineseFont;
                bool newJpFont = GUILayout.Toggle(
                    oldJpFont,
                    " " + Loc.Get("ui.japanese_use_chinese_font"));
                if (newJpFont != oldJpFont)
                {
                    Settings.JapaneseUseChineseFont = newJpFont;
                    Patches.ApplyJapaneseFontPreference();
                }
            }
            GUILayout.EndVertical();
            GUILayout.Space(5);

            // 1. 判定模式选择
            GUILayout.BeginVertical("box");
            GUILayout.Label("<b>" + Loc.Get("ui.section1") + "</b>");
            GUILayout.Space(5);

            int currentModeIndex = (int)Settings.Mode;
            string[] modeLabels = new string[]
            {
                Loc.Get("mode.vanilla"),
                Loc.Get("mode.mobile"),
                Loc.Get("mode.custom")
            };
            int newModeIndex = GUILayout.Toolbar(currentModeIndex, modeLabels, GUILayout.Height(28));
            if (newModeIndex != currentModeIndex)
            {
                Settings.Mode = (JudgmentMode)newModeIndex;
                Patches.ApplyModeChange();
            }

            GUILayout.Space(4);

            bool curOff = Settings.Mode == JudgmentMode.Disabled;
            bool curMobile = Settings.Mode == JudgmentMode.Mobile;
            bool curCustom = Settings.Mode == JudgmentMode.Custom;

            bool newOff = GUILayout.Toggle(curOff, " " + Loc.Get("mode.vanilla"));
            bool newMobile = GUILayout.Toggle(curMobile, " " + Loc.Get("mode.mobile"));
            bool newCustom = GUILayout.Toggle(curCustom, " " + Loc.Get("mode.custom"));

            if (newOff && !curOff)
            {
                Settings.Mode = JudgmentMode.Disabled;
                Patches.ApplyModeChange();
            }
            else if (newMobile && !curMobile)
            {
                Settings.Mode = JudgmentMode.Mobile;
                Patches.ApplyModeChange();
            }
            else if (newCustom && !curCustom)
            {
                Settings.Mode = JudgmentMode.Custom;
                Patches.ApplyModeChange();
            }

            GUILayout.Space(5);

            if (Settings.Mode == JudgmentMode.Mobile)
            {
                GUILayout.BeginVertical("box");
                GUILayout.Label("<color=#33ccff><b>" + Loc.Get("ui.mobile.desc_title") + "</b></color>");
                GUILayout.Label("  - " + Loc.Get("ui.mobile.desc_counted"));
                GUILayout.Label("  - " + Loc.Get("ui.mobile.desc_perfect"));
                GUILayout.Label("  - " + Loc.Get("ui.mobile.desc_pure"));
                GUILayout.Label("  - " + Loc.Get("ui.mobile.desc_speed"));
                GUILayout.Label("  - " + Loc.Get("ui.mobile.desc_hud"));
                GUILayout.Label("  - " + Loc.Get("ui.mobile.desc_diff"));
                GUILayout.EndVertical();
            }
            else if (Settings.Mode == JudgmentMode.Custom)
            {
                GUILayout.BeginVertical("box");
                GUILayout.Label("<color=#ffaa00><b>" + Loc.Get("ui.custom.desc_title") + "</b></color>");
                GUILayout.Label("  - " + Loc.Get("ui.custom.desc_hud"));
                GUILayout.Label("  - " + Loc.Get("ui.custom.free_input_hint"));
                GUILayout.Space(5);

                // 单位选择 (时间 vs 角度)
                GUILayout.BeginHorizontal();
                GUILayout.Label("<b>" + Loc.Get("ui.custom.unit_label") + "</b>", GUILayout.Width(200));
                bool isTime = Settings.CustomUnit == CustomUnitType.Time;
                bool isAngle = Settings.CustomUnit == CustomUnitType.Angle;
                bool newTime = GUILayout.Toggle(isTime, " " + Loc.Get("ui.custom.unit_time"), GUILayout.Width(180));
                bool newAngle = GUILayout.Toggle(isAngle, " " + Loc.Get("ui.custom.unit_angle"), GUILayout.Width(180));
                if (newTime && !isTime)
                {
                    Settings.CustomUnit = CustomUnitType.Time;
                    Patches.ApplyModeChange();
                }
                else if (newAngle && !isAngle)
                {
                    Settings.CustomUnit = CustomUnitType.Angle;
                    Patches.ApplyModeChange();
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(6);

                if (Settings.CustomUnit == CustomUnitType.Time)
                {
                    // 时间模式
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(string.Format(Loc.Get("ui.custom.counted"), Mathf.RoundToInt(Settings.CustomCountedTime)), GUILayout.Width(240));
                    float newCounted = GUILayout.HorizontalSlider(Settings.CustomCountedTime, 25f, 200f);
                    if (Math.Abs(newCounted - Settings.CustomCountedTime) > 0.1f)
                    {
                        Settings.CustomCountedTime = newCounted;
                        _countedTimeStr = newCounted.ToString("0.##");
                        Settings.SyncValues();
                        Patches.ApplyModeChange();
                    }
                    string typedCounted = GUILayout.TextField(_countedTimeStr, GUILayout.Width(70));
                    if (typedCounted != _countedTimeStr)
                    {
                        _countedTimeStr = typedCounted;
                        float val;
                        if (float.TryParse(typedCounted, out val) && val > 0f)
                        {
                            Settings.CustomCountedTime = val;
                            Settings.SyncValues();
                            Patches.ApplyModeChange();
                        }
                    }
                    GUILayout.Label("ms", GUILayout.Width(30));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(string.Format(Loc.Get("ui.custom.perfect"), Mathf.RoundToInt(Settings.CustomPerfectTime)), GUILayout.Width(240));
                    float newPerfect = GUILayout.HorizontalSlider(Settings.CustomPerfectTime, 25f, 150f);
                    if (Math.Abs(newPerfect - Settings.CustomPerfectTime) > 0.1f)
                    {
                        Settings.CustomPerfectTime = newPerfect;
                        _perfectTimeStr = newPerfect.ToString("0.##");
                        Settings.SyncValues();
                        Patches.ApplyModeChange();
                    }
                    string typedPerfect = GUILayout.TextField(_perfectTimeStr, GUILayout.Width(70));
                    if (typedPerfect != _perfectTimeStr)
                    {
                        _perfectTimeStr = typedPerfect;
                        float val;
                        if (float.TryParse(typedPerfect, out val) && val > 0f)
                        {
                            Settings.CustomPerfectTime = val;
                            Settings.SyncValues();
                            Patches.ApplyModeChange();
                        }
                    }
                    GUILayout.Label("ms", GUILayout.Width(30));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(string.Format(Loc.Get("ui.custom.pure"), Mathf.RoundToInt(Settings.CustomPureTime)), GUILayout.Width(240));
                    float newPure = GUILayout.HorizontalSlider(Settings.CustomPureTime, 25f, 100f);
                    if (Math.Abs(newPure - Settings.CustomPureTime) > 0.1f)
                    {
                        Settings.CustomPureTime = newPure;
                        _pureTimeStr = newPure.ToString("0.##");
                        Settings.SyncValues();
                        Patches.ApplyModeChange();
                    }
                    string typedPure = GUILayout.TextField(_pureTimeStr, GUILayout.Width(70));
                    if (typedPure != _pureTimeStr)
                    {
                        _pureTimeStr = typedPure;
                        float val;
                        if (float.TryParse(typedPure, out val) && val > 0f)
                        {
                            Settings.CustomPureTime = val;
                            Settings.SyncValues();
                            Patches.ApplyModeChange();
                        }
                    }
                    GUILayout.Label("ms", GUILayout.Width(30));
                    GUILayout.EndHorizontal();
                }
                else
                {
                    // 角度模式
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(string.Format(Loc.Get("ui.custom.counted_angle"), Mathf.RoundToInt(Settings.CustomCountedAngle)), GUILayout.Width(240));
                    float newCounted = GUILayout.HorizontalSlider(Settings.CustomCountedAngle, 20f, 150f);
                    if (Math.Abs(newCounted - Settings.CustomCountedAngle) > 0.1f)
                    {
                        Settings.CustomCountedAngle = newCounted;
                        _countedAngleStr = newCounted.ToString("0.##");
                        Patches.ApplyModeChange();
                    }
                    string typedCounted = GUILayout.TextField(_countedAngleStr, GUILayout.Width(70));
                    if (typedCounted != _countedAngleStr)
                    {
                        _countedAngleStr = typedCounted;
                        float val;
                        if (float.TryParse(typedCounted, out val) && val > 0f)
                        {
                            Settings.CustomCountedAngle = val;
                            Patches.ApplyModeChange();
                        }
                    }
                    GUILayout.Label("°", GUILayout.Width(30));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(string.Format(Loc.Get("ui.custom.perfect_angle"), Mathf.RoundToInt(Settings.CustomPerfectAngle)), GUILayout.Width(240));
                    float newPerfect = GUILayout.HorizontalSlider(Settings.CustomPerfectAngle, 15f, 100f);
                    if (Math.Abs(newPerfect - Settings.CustomPerfectAngle) > 0.1f)
                    {
                        Settings.CustomPerfectAngle = newPerfect;
                        _perfectAngleStr = newPerfect.ToString("0.##");
                        Patches.ApplyModeChange();
                    }
                    string typedPerfect = GUILayout.TextField(_perfectAngleStr, GUILayout.Width(70));
                    if (typedPerfect != _perfectAngleStr)
                    {
                        _perfectAngleStr = typedPerfect;
                        float val;
                        if (float.TryParse(typedPerfect, out val) && val > 0f)
                        {
                            Settings.CustomPerfectAngle = val;
                            Patches.ApplyModeChange();
                        }
                    }
                    GUILayout.Label("°", GUILayout.Width(30));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(string.Format(Loc.Get("ui.custom.pure_angle"), Mathf.RoundToInt(Settings.CustomPureAngle)), GUILayout.Width(240));
                    float newPure = GUILayout.HorizontalSlider(Settings.CustomPureAngle, 10f, 80f);
                    if (Math.Abs(newPure - Settings.CustomPureAngle) > 0.1f)
                    {
                        Settings.CustomPureAngle = newPure;
                        _pureAngleStr = newPure.ToString("0.##");
                        Patches.ApplyModeChange();
                    }
                    string typedPure = GUILayout.TextField(_pureAngleStr, GUILayout.Width(70));
                    if (typedPure != _pureAngleStr)
                    {
                        _pureAngleStr = typedPure;
                        float val;
                        if (float.TryParse(typedPure, out val) && val > 0f)
                        {
                            Settings.CustomPureAngle = val;
                            Patches.ApplyModeChange();
                        }
                    }
                    GUILayout.Label("°", GUILayout.Width(30));
                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(5);
                Settings.CustomScaleWithSpeedTrial = GUILayout.Toggle(Settings.CustomScaleWithSpeedTrial, " " + Loc.Get("ui.custom.scale_speed"));

                // 百分比预设乘算区
                GUILayout.Space(5);
                GUILayout.BeginVertical("box");
                GUILayout.BeginHorizontal();
                GUILayout.Label("<b>" + Loc.Get("ui.custom.mult_title") + "</b>", GUILayout.Width(200));
                _percentStr = GUILayout.TextField(_percentStr, GUILayout.Width(60));
                GUILayout.Label("%", GUILayout.Width(25));
                GUILayout.EndHorizontal();

                float pct;
                if (!float.TryParse(_percentStr, out pct) || pct <= 0f)
                {
                    pct = 100f;
                }
                float ratio = pct / 100f;

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(Loc.Get("ui.custom.preset_strict"), GUILayout.Width(130)))
                {
                    if (Settings.CustomUnit == CustomUnitType.Time)
                    {
                        Settings.CustomCountedTime = 40f * ratio;
                        Settings.CustomPerfectTime = 30f * ratio;
                        Settings.CustomPureTime = 20f * ratio;
                    }
                    else
                    {
                        Settings.CustomCountedAngle = 48f * ratio;
                        Settings.CustomPerfectAngle = 36f * ratio;
                        Settings.CustomPureAngle = 24f * ratio;
                    }
                    Settings.SyncValues();
                    SyncTextBuffers();
                    Patches.ApplyModeChange();
                }
                if (GUILayout.Button(Loc.Get("ui.custom.preset_normal"), GUILayout.Width(130)))
                {
                    if (Settings.CustomUnit == CustomUnitType.Time)
                    {
                        Settings.CustomCountedTime = 65f * ratio;
                        Settings.CustomPerfectTime = 45f * ratio;
                        Settings.CustomPureTime = 30f * ratio;
                    }
                    else
                    {
                        Settings.CustomCountedAngle = 60f * ratio;
                        Settings.CustomPerfectAngle = 45f * ratio;
                        Settings.CustomPureAngle = 30f * ratio;
                    }
                    Settings.SyncValues();
                    SyncTextBuffers();
                    Patches.ApplyModeChange();
                }
                if (GUILayout.Button(Loc.Get("ui.custom.preset_lenient"), GUILayout.Width(130)))
                {
                    if (Settings.CustomUnit == CustomUnitType.Time)
                    {
                        Settings.CustomCountedTime = 90f * ratio;
                        Settings.CustomPerfectTime = 60f * ratio;
                        Settings.CustomPureTime = 40f * ratio;
                    }
                    else
                    {
                        Settings.CustomCountedAngle = 72f * ratio;
                        Settings.CustomPerfectAngle = 54f * ratio;
                        Settings.CustomPureAngle = 36f * ratio;
                    }
                    Settings.SyncValues();
                    SyncTextBuffers();
                    Patches.ApplyModeChange();
                }
                if (GUILayout.Button(Loc.Get("ui.custom.preset_mobile"), GUILayout.Width(130)))
                {
                    if (Settings.CustomUnit == CustomUnitType.Time)
                    {
                        Settings.CustomCountedTime = 90f * ratio;
                        Settings.CustomPerfectTime = 70f * ratio;
                        Settings.CustomPureTime = 50f * ratio;
                    }
                    else
                    {
                        Settings.CustomCountedAngle = 80f * ratio;
                        Settings.CustomPerfectAngle = 60f * ratio;
                        Settings.CustomPureAngle = 40f * ratio;
                    }
                    Settings.SyncValues();
                    SyncTextBuffers();
                    Patches.ApplyModeChange();
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();

                // 快速档案槽位区 (Profile Slots)
                GUILayout.Space(5);
                GUILayout.BeginVertical("box");
                GUILayout.Label("<b>" + Loc.Get("ui.custom.profiles_title") + "</b>");

                for (int slot = 1; slot <= 3; slot++)
                {
                    CustomProfile p = slot == 1 ? Settings.Profile1 : (slot == 2 ? Settings.Profile2 : Settings.Profile3);
                    GUILayout.BeginHorizontal();
                    string slotDesc = (p.Unit == CustomUnitType.Time)
                        ? string.Format("Slot {0} ({1:0}ms / {2:0}ms / {3:0}ms)", slot, p.CountedTime, p.PerfectTime, p.PureTime)
                        : string.Format("Slot {0} ({1:0}° / {2:0}° / {3:0}°)", slot, p.CountedAngle, p.PerfectAngle, p.PureAngle);

                    GUILayout.Label(slotDesc, GUILayout.Width(220));

                    if (GUILayout.Button(string.Format(Loc.Get("ui.custom.load_slot"), slot), GUILayout.Width(150)))
                    {
                        Settings.LoadFromProfile(slot);
                        SyncTextBuffers();
                        Patches.ApplyModeChange();
                    }
                    if (GUILayout.Button(string.Format(Loc.Get("ui.custom.save_slot"), slot), GUILayout.Width(150)))
                    {
                        Settings.SaveToProfile(slot);
                        Settings.Save(ModEntry);
                    }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();

                GUILayout.EndVertical();
            }
            GUILayout.EndVertical();

            // 2. PC 模式专属辅助功能
            GUILayout.BeginVertical("box");
            GUILayout.Label("<b>" + Loc.Get("ui.section2") + "</b>");
            GUILayout.Space(5);

            if (Settings.Mode != JudgmentMode.Disabled)
            {
                GUILayout.Label("<i>" + Loc.Get("ui.pc_only_hint") + "</i>");
            }

            Settings.EnableHitIndicator = GUILayout.Toggle(
                Settings.EnableHitIndicator,
                " " + Loc.Get("ui.indicator.toggle"));

            if (Settings.EnableHitIndicator)
            {
                GUILayout.Space(2);
                GUILayout.Label("    • " + Loc.Get("ui.indicator.desc_normal"));
                GUILayout.Label("    • " + Loc.Get("ui.indicator.desc_lenient"));
                GUILayout.Label("    • " + Loc.Get("ui.indicator.desc_meter"));

                GUILayout.Space(5);
                Settings.EnableHitIndicatorPlus = GUILayout.Toggle(
                    Settings.EnableHitIndicatorPlus,
                    " " + Loc.Get("ui.indicator_plus.toggle"));

                if (Settings.EnableHitIndicatorPlus)
                {
                    Settings.EnableAlternativeIndicatorPlus = GUILayout.Toggle(
                        Settings.EnableAlternativeIndicatorPlus,
                        " " + Loc.Get("ui.alt_indicator.toggle"));

                    if (Settings.EnableAlternativeIndicatorPlus)
                    {
                        GUILayout.BeginVertical("box");
                        GUILayout.Label("<b>" + Loc.Get("ui.alt_indicator.style_label") + "</b>");

                        bool curSign = Settings.AltStyle == AlternativeIndicatorStyle.SignPrefix;
                        bool curLetter = Settings.AltStyle == AlternativeIndicatorStyle.LetterPrefix;

                        bool newSign = GUILayout.Toggle(curSign, " " + Loc.Get("ui.alt_style_sign"));
                        bool newLetter = GUILayout.Toggle(curLetter, " " + Loc.Get("ui.alt_style_letter"));

                        if (newSign && !curSign)
                        {
                            Settings.AltStyle = AlternativeIndicatorStyle.SignPrefix;
                        }
                        else if (newLetter && !curLetter)
                        {
                            Settings.AltStyle = AlternativeIndicatorStyle.LetterPrefix;
                        }
                        GUILayout.EndVertical();
                    }
                }

                GUILayout.Space(5);
                Settings.EnableDoubleHitIndicator = GUILayout.Toggle(
                    Settings.EnableDoubleHitIndicator,
                    " " + Loc.Get("ui.double_indicator.toggle"));

                GUILayout.Space(5);
                Settings.ColorHitTextBorder = GUILayout.Toggle(
                    Settings.ColorHitTextBorder,
                    " " + Loc.Get("ui.border_color.toggle"));

                GUILayout.Space(5);
                Settings.EnableTrackColoring = GUILayout.Toggle(
                    Settings.EnableTrackColoring,
                    " " + Loc.Get("ui.track_color.toggle"));

                GUILayout.Space(6);
                GUILayout.BeginVertical("box");
                GUILayout.Label("<b>" + Loc.Get("ui.guide.title") + "</b>");
                GUILayout.Label("• <b>" + Loc.Get("ui.guide.default") + "</b>");
                GUILayout.Label("    - " + Loc.Get("ui.guide.default_plus"));
                GUILayout.Label("    - " + Loc.Get("ui.guide.default_alt_sign"));
                GUILayout.Label("    - " + Loc.Get("ui.guide.default_alt_letter"));
                GUILayout.Label("    - " + Loc.Get("ui.guide.default_double"));
                GUILayout.Label("• <b>" + Loc.Get("ui.guide.shortened") + "</b>");
                GUILayout.Label("    - " + Loc.Get("ui.guide.shortened_plus"));
                GUILayout.Label("    - " + Loc.Get("ui.guide.shortened_alt_sign"));
                GUILayout.Label("    - " + Loc.Get("ui.guide.shortened_alt_letter"));
                GUILayout.Label("• <b>" + Loc.Get("ui.guide.ms") + "</b>");
                GUILayout.Label("    - " + Loc.Get("ui.guide.ms_desc"));
                GUILayout.Label("• <b>" + Loc.Get("ui.guide.results") + "</b>");
                GUILayout.Label("    - " + Loc.Get("ui.guide.results_desc"));
                GUILayout.EndVertical();
            }

            GUILayout.Space(10);
            bool oldForceDiff = Settings.ForceDifficultySelector;
            bool newForceDiff = GUILayout.Toggle(
                oldForceDiff,
                " " + Loc.Get("ui.force_diff.toggle"));
            if (newForceDiff != oldForceDiff)
            {
                Settings.ForceDifficultySelector = newForceDiff;
                Patches.ApplyModeChange();
            }
            GUILayout.EndVertical();

            // 3. 全局辅助功能（不死模式与自动播放）
            GUILayout.BeginVertical("box");
            GUILayout.Label("<b>" + Loc.Get("ui.section3") + "</b>");
            GUILayout.Space(5);

            bool oldNoFail = Settings.NoFail;
            bool newNoFail = GUILayout.Toggle(oldNoFail, " " + Loc.Get("ui.nofail.toggle"));
            if (newNoFail != oldNoFail)
            {
                Settings.NoFail = newNoFail;
                Patches.ApplyNoFail(newNoFail);
            }

            bool oldAuto = Settings.Autoplay;
            bool newAuto = GUILayout.Toggle(oldAuto, " " + Loc.Get("ui.auto.toggle"));
            if (newAuto != oldAuto)
            {
                Settings.Autoplay = newAuto;
                Patches.ApplyAutoplay(newAuto);
            }
            GUILayout.EndVertical();

            // 4. 杂项功能 (Miscellaneous)
            GUILayout.BeginVertical("box");
            GUILayout.Label("<b>" + Loc.Get("misc.section") + "</b>");
            GUILayout.Space(5);

            bool oldRing = Settings.HidePlanetRing;
            bool newRing = GUILayout.Toggle(oldRing, " " + Loc.Get("misc.ring_toggle"));
            if (newRing != oldRing)
            {
                Settings.HidePlanetRing = newRing;
                Patches.ApplyHidePlanetRing(newRing);
            }

            GUILayout.Space(6);

            bool oldEditor = Settings.AlwaysAllowEditor;
            bool newEditor = GUILayout.Toggle(oldEditor, " " + Loc.Get("misc.editor_toggle"));
            if (newEditor != oldEditor)
            {
                Settings.AlwaysAllowEditor = newEditor;
            }

            GUILayout.Space(6);

            bool oldAutoPlus = Settings.AutoplayPlus;
            bool newAutoPlus = GUILayout.Toggle(oldAutoPlus, " " + Loc.Get("misc.autoplay_plus_toggle"));
            if (newAutoPlus != oldAutoPlus)
            {
                Settings.AutoplayPlus = newAutoPlus;
                Patches.ApplyAutoplayPlus(newAutoPlus);
            }

            if (Settings.AutoplayPlus)
            {
                GUILayout.Space(6);
                GUILayout.BeginVertical("box");

                // 拟人目标 X 精准度滑块与快捷按钮
                GUILayout.BeginHorizontal();
                GUILayout.Label("<b>" + string.Format(Loc.Get("misc.autoplay_acc_label"), Settings.AutoplayAccuracy) + "</b>", GUILayout.Width(460));
                float rawAcc = GUILayout.HorizontalSlider(Settings.AutoplayAccuracy, 50f, 100f, GUILayout.Width(200));
                Settings.AutoplayAccuracy = (float)Math.Round(rawAcc * 2f) / 2f;
                if (GUILayout.Button("100%", GUILayout.Width(52))) Settings.AutoplayAccuracy = 100f;
                if (GUILayout.Button("99.5%", GUILayout.Width(55))) Settings.AutoplayAccuracy = 99.5f;
                if (GUILayout.Button("98%", GUILayout.Width(48))) Settings.AutoplayAccuracy = 98f;
                if (GUILayout.Button("95%", GUILayout.Width(48))) Settings.AutoplayAccuracy = 95f;
                GUILayout.EndHorizontal();

                GUILayout.Space(8);
                GUILayout.Label("<b>" + Loc.Get("misc.autoplay_keys_label") + "</b>");
                GUILayout.Label("<i><color=#bbbbbb>" + Loc.Get("misc.autoplay_keys_hint") + "</color></i>");
                GUILayout.Space(4);

                // 左右手分区标头
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#77ddff><b>[ " + Loc.Get("misc.hand_left") + " : 1 ~ 4 ]</b></color>", GUILayout.Width(316));
                GUILayout.Space(16);
                GUILayout.Label("<color=#ffcc77><b>[ " + Loc.Get("misc.hand_right") + " : 5 ~ 8 ]</b></color>", GUILayout.Width(316));
                GUILayout.EndHorizontal();

                // 2 行 x 8 列 交互式按键绑定按钮网格
                for (int row = 0; row < 2; row++)
                {
                    GUILayout.BeginHorizontal();
                    for (int col = 0; col < 8; col++)
                    {
                        int slot = row * 8 + col;
                        if (col == 4)
                        {
                            GUILayout.Label("<b>|</b>", GUILayout.Width(12));
                        }

                        bool isWaiting = (AutoplayBot.WaitingKeySlot == slot);
                        KeyCode boundKey = AutoplayBot.GetSlotKey(slot);
                        string btnLabel;
                        if (isWaiting)
                        {
                            btnLabel = "<color=#ff4444><b>" + Loc.Get("misc.key_waiting") + "</b></color>";
                        }
                        else if (boundKey == KeyCode.None)
                        {
                            btnLabel = "<color=#888888>" + Loc.Get("misc.key_unset") + "</color>";
                        }
                        else
                        {
                            string colorHex = (col < 4) ? "#99eeff" : "#ffdd88";
                            btnLabel = "<b><color=" + colorHex + ">" + AutoplayBot.FormatKeyName(boundKey) + "</color></b>";
                        }

                        if (GUILayout.Button(btnLabel, GUILayout.Width(76), GUILayout.Height(30)))
                        {
                            if (Event.current != null && Event.current.button == 1)
                            {
                                // 右键清空该槽位
                                Settings.AutoplayKeyBindings[slot] = KeyCode.None;
                                if (AutoplayBot.WaitingKeySlot == slot)
                                {
                                    AutoplayBot.WaitingKeySlot = -1;
                                }
                            }
                            else
                            {
                                // 左键进入或取消按键捕获等待
                                AutoplayBot.WaitingKeySlot = (AutoplayBot.WaitingKeySlot == slot) ? -1 : slot;
                            }
                        }
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.Space(3);
                }

                GUILayout.Space(5);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(Loc.Get("misc.autoplay_preset_btn"), GUILayout.Width(160), GUILayout.Height(26)))
                {
                    AutoplayBot.ResetDefaultKeys();
                }
                GUILayout.Space(8);
                if (GUILayout.Button(Loc.Get("misc.sync_jipper_btn"), GUILayout.Width(240), GUILayout.Height(26)))
                {
                    AutoplayBot.SyncFromJipperResourcePack();
                }
                if (!string.IsNullOrEmpty(AutoplayBot.SyncStatusMessage))
                {
                    GUILayout.Space(10);
                    GUILayout.Label("<b>" + AutoplayBot.SyncStatusMessage + "</b>");
                }
                GUILayout.EndHorizontal();

                GUILayout.EndVertical();
            }
            GUILayout.EndVertical();
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            Settings.Save(modEntry);
        }
    }
}
