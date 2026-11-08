using System;
using UnityEngine;
using UnityModManagerNet;

namespace ADOFAIPlusPlus
{
    public enum JudgmentMode
    {
        Disabled = 0, // 原生 PC 模式
        Mobile = 1,   // 移动端判定模式 (90ms / 70ms / 50ms，且倍速不缩减)
        Custom = 2    // 自定义判定模式
    }

    public enum CustomUnitType
    {
        Time = 0,   // 时间模式 (ms)
        Angle = 1   // 角度模式 (度数 °)
    }

    public enum AlternativeIndicatorStyle
    {
        SignPrefix = 0,   // 符号后缀: 标判！+ / 宽判！！+ (太慢 + / 太快 -) [默认]
        LetterPrefix = 1  // 字符名称前缀: L太快！/ N太慢！
    }

    public enum ModLanguage
    {
        Auto = 0,               // 跟随游戏 (Follow Game)
        ChineseSimplified = 1,  // 简体中文
        ChineseTraditional = 2, // 繁體中文
        English = 3,            // English
        Korean = 4,             // 한국어
        Japanese = 5,           // 日本語
        Spanish = 6,            // Español
        Portuguese = 7,         // Português
        French = 8,             // Français
        German = 9,             // Deutsch
        Polish = 10,            // Polski
        Russian = 11,           // Русский
        Vietnamese = 12,        // Tiếng Việt
        Czech = 13,             // Čeština
        Romanian = 14           // Română
    }

    [Serializable]
    public class CustomProfile
    {
        public string Name = "Profile";
        public CustomUnitType Unit = CustomUnitType.Time;
        public float CountedTime = 65f;
        public float PerfectTime = 45f;
        public float PureTime = 30f;
        public float CountedAngle = 60f;
        public float PerfectAngle = 45f;
        public float PureAngle = 30f;
        public bool ScaleWithSpeed = true;
    }

    public class Settings : UnityModManager.ModSettings
    {
        /// <summary>
        /// 模组总开关（打开前需确认免责声明：严禁在多人游戏中使用）
        /// </summary>
        public bool MasterEnabled = false;
        public bool DisclaimerAccepted = false;

        /// <summary>
        /// 模组语言设置（默认跟随游戏当前语言）
        /// </summary>
        public ModLanguage Language = ModLanguage.Auto;

        /// <summary>
        /// 更好的日语本地化（当语言为日语时，将原版未翻译的英文判定词完整本地化为日语；默认开启）
        /// </summary>
        public bool BetterJapaneseLocalization = true;

        /// <summary>
        /// 日语界面下使用中文字体（替换原版日语字体，默认开启）
        /// </summary>
        public bool JapaneseUseChineseFont = true;

        /// <summary>
        /// 一次性迁移标记：确保升级后默认启用日语优化与中文字体开关
        /// </summary>
        public bool MigratedJapaneseFontDefault = false;

        /// <summary>
        /// 当前判定模式
        /// </summary>
        public JudgmentMode Mode = JudgmentMode.Mobile;

        /// <summary>
        /// 自定义判定单位模式（时间 ms vs 角度 °）
        /// </summary>
        public CustomUnitType CustomUnit = CustomUnitType.Time;

        /// <summary>
        /// 自定义时间（毫秒 ms，默认采用官方标准判定 Preset: 65 / 45 / 30 ms）
        /// </summary>
        public float CustomCountedTime = 65f;
        public float CustomPerfectTime = 45f;
        public float CustomPureTime = 30f;

        /// <summary>
        /// 自定义角度（度数 °，默认采用官方标准判定 Preset: 60° / 45° / 30°）
        /// </summary>
        public float CustomCountedAngle = 60f;
        public float CustomPerfectAngle = 45f;
        public float CustomPureAngle = 30f;

        /// <summary>
        /// 保持兼容旧版存档的秒单位数值
        /// </summary>
        public float CustomCounted = 0.065f;
        public float CustomPerfect = 0.045f;
        public float CustomPure = 0.03f;

        /// <summary>
        /// 自定义模式下是否按倍速缩减时间下限（默认与标判一致为开启）
        /// </summary>
        public bool CustomScaleWithSpeedTrial = true;

        /// <summary>
        /// 标记是否已将旧版默认移动端预设自动迁移至标准判定预设
        /// </summary>
        public bool MigratedCustomDefaultToNormal = false;

        /// <summary>
        /// 自定义快速配置档案槽位
        /// </summary>
        public CustomProfile Profile1 = new CustomProfile { Name = "Slot 1", Unit = CustomUnitType.Time, CountedTime = 130f, PerfectTime = 90f, PureTime = 60f, CountedAngle = 60f, PerfectAngle = 45f, PureAngle = 30f, ScaleWithSpeed = false };
        public CustomProfile Profile2 = new CustomProfile { Name = "Slot 2", Unit = CustomUnitType.Time, CountedTime = 90f, PerfectTime = 60f, PureTime = 40f, CountedAngle = 54f, PerfectAngle = 40f, PureAngle = 27f, ScaleWithSpeed = false };
        public CustomProfile Profile3 = new CustomProfile { Name = "Slot 3", Unit = CustomUnitType.Angle, CountedTime = 65f, PerfectTime = 45f, PureTime = 30f, CountedAngle = 48f, PerfectAngle = 35f, PureAngle = 22f, ScaleWithSpeed = false };

        /// <summary>
        /// 判定指示器（仅适用于 PC 模式）
        /// 打出 Counted 时重测，标判合格施加灰色描边，宽判合格施加绿色描边
        /// </summary>
        public bool EnableHitIndicator = false;

        /// <summary>
        /// 判定指示器+（前置为判定指示器）
        /// 将 Counted 文字分别替换为「标判！」和「宽判！」（精简模式显示 NOR / LEN）
        /// </summary>
        public bool EnableHitIndicatorPlus = true;

        /// <summary>
        /// 为「太快！！」与「太慢！！」启用判定指示器
        /// 即使在空敲或失误情况下也重测显示「宽判！！」与「标判！！」
        /// </summary>
        public bool EnableDoubleHitIndicator = false;

        /// <summary>
        /// 轨道染色：当打出「标判！」与「宽判！」时对应将轨道染为灰色与绿色
        /// </summary>
        public bool EnableTrackColoring = false;

        /// <summary>
        /// 浮动判定文字边框染对应色（开启：真正染灰/绿描边；关闭：保持旧版高亮白金描边）
        /// </summary>
        public bool ColorHitTextBorder = true;

        /// <summary>
        /// 另类判定指示器+
        /// 默认为启用，为指示器文字提供实际早晚方向指示（太快/太慢）
        /// </summary>
        public bool EnableAlternativeIndicatorPlus = false;

        /// <summary>
        /// 另类指示器工作方式（默认：正负符号前缀 -宽判！太快 / +标判！太慢）
        /// </summary>
        public AlternativeIndicatorStyle AltStyle = AlternativeIndicatorStyle.SignPrefix;

        /// <summary>
        /// 强制启用判定选择（即使在低 BPM 或官方关卡也强制开启判定选择栏）
        /// </summary>
        public bool ForceDifficultySelector = false;

        /// <summary>
        /// 不死模式（No Fail）：任何关卡（含官谱）均不会死
        /// </summary>
        public bool NoFail = false;

        /// <summary>
        /// 自动播放（Autoplay）：任何关卡（含官谱）均开启自动完美命中
        /// </summary>
        public bool Autoplay = false;

        /// <summary>
        /// 杂项：隐藏星球判定圈（虚线圆圈）
        /// </summary>
        public bool HidePlanetRing = false;

        /// <summary>
        /// 杂项：自动播放+（独立开关，内部零延迟判定 + 双通道拟人按键驱动 KV）
        /// </summary>
        public bool AutoplayPlus = false;

        /// <summary>
        /// 自动播放+：惯用手偏好（默认右手优先 P，关闭为左手优先 E）
        /// </summary>
        public bool PreferRightHand = true;

        /// <summary>
        /// 自动播放+：拟人目标 X 精准度 (50% ~ 100%)
        /// </summary>
        public float AutoplayAccuracy = 100f;

        /// <summary>
        /// 自动播放+：16 键位绑定槽位（2行 x 8列，对应 CharonKV 布局）
        /// </summary>
        public KeyCode[] AutoplayKeyBindings = new KeyCode[]
        {
            KeyCode.Tab, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.E,
            KeyCode.P, KeyCode.Equals, KeyCode.Backspace, KeyCode.Backslash,
            KeyCode.A, KeyCode.LeftShift, KeyCode.C, KeyCode.Space,
            KeyCode.Comma, KeyCode.Period, KeyCode.Return, KeyCode.H
        };

        public void SyncValues()
        {
            if (!MigratedJapaneseFontDefault)
            {
                MigratedJapaneseFontDefault = true;
                BetterJapaneseLocalization = true;
                JapaneseUseChineseFont = true;
            }

            if (!MigratedCustomDefaultToNormal)
            {
                MigratedCustomDefaultToNormal = true;
                if (Math.Abs(CustomCountedTime - 90f) < 0.01f &&
                    Math.Abs(CustomPerfectTime - 70f) < 0.01f &&
                    Math.Abs(CustomPureTime - 50f) < 0.01f)
                {
                    CustomCountedTime = 65f;
                    CustomPerfectTime = 45f;
                    CustomPureTime = 30f;
                    CustomCounted = 0.065f;
                    CustomPerfect = 0.045f;
                    CustomPure = 0.03f;
                    CustomScaleWithSpeedTrial = true;
                }
            }

            if (CustomCountedTime <= 0f && CustomCounted > 0f)
            {
                CustomCountedTime = CustomCounted * 1000f;
                CustomPerfectTime = CustomPerfect * 1000f;
                CustomPureTime = CustomPure * 1000f;
            }
            else
            {
                CustomCounted = CustomCountedTime / 1000f;
                CustomPerfect = CustomPerfectTime / 1000f;
                CustomPure = CustomPureTime / 1000f;
            }

            if (AutoplayKeyBindings == null || AutoplayKeyBindings.Length != 16)
            {
                KeyCode[] defaults = new KeyCode[]
                {
                    KeyCode.Tab, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.E,
                    KeyCode.P, KeyCode.Equals, KeyCode.Backspace, KeyCode.Backslash,
                    KeyCode.A, KeyCode.LeftShift, KeyCode.C, KeyCode.Space,
                    KeyCode.Comma, KeyCode.Period, KeyCode.Return, KeyCode.H
                };
                if (AutoplayKeyBindings != null && AutoplayKeyBindings.Length > 0)
                {
                    for (int i = 0; i < Math.Min(AutoplayKeyBindings.Length, 16); i++)
                    {
                        defaults[i] = AutoplayKeyBindings[i];
                    }
                }
                AutoplayKeyBindings = defaults;
            }

            if (AutoplayAccuracy < 50f) AutoplayAccuracy = 50f;
            if (AutoplayAccuracy > 100f) AutoplayAccuracy = 100f;
        }

        public void SaveToProfile(int slot)
        {
            CustomProfile p = slot == 1 ? Profile1 : (slot == 2 ? Profile2 : Profile3);
            p.Unit = CustomUnit;
            p.CountedTime = CustomCountedTime;
            p.PerfectTime = CustomPerfectTime;
            p.PureTime = CustomPureTime;
            p.CountedAngle = CustomCountedAngle;
            p.PerfectAngle = CustomPerfectAngle;
            p.PureAngle = CustomPureAngle;
            p.ScaleWithSpeed = CustomScaleWithSpeedTrial;
        }

        public void LoadFromProfile(int slot)
        {
            CustomProfile p = slot == 1 ? Profile1 : (slot == 2 ? Profile2 : Profile3);
            CustomUnit = p.Unit;
            CustomCountedTime = p.CountedTime;
            CustomPerfectTime = p.PerfectTime;
            CustomPureTime = p.PureTime;
            CustomCountedAngle = p.CountedAngle;
            CustomPerfectAngle = p.PerfectAngle;
            CustomPureAngle = p.PureAngle;
            CustomScaleWithSpeedTrial = p.ScaleWithSpeed;
            SyncValues();
        }

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            SyncValues();
            Save(this, modEntry);
        }
    }
}
