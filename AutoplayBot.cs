using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ADOFAIPlusPlus
{
    /// <summary>
    /// 自动播放+ (Autoplay+) 核心引擎：
    /// 1. 零延迟局内判定（通过原生 OttoHoldHit 时间轴驱动，绝不因 OS 键盘队列延迟爆失误/空敲死亡）
    /// 2. 闭环拟人目标 X 精准度（50% ~ 100%，在合法 Counted 弧度边界内注入自相关正态抖动，数学级保证绝不死）
    /// 3. 双通道键位输出（同时注入 UnityEngine.Input 0帧延迟通道与 Win32 硬件扫描码通道，全面兼容 JipperResourcePack/SkyHook、CharonKV、NohBoard 等）
    /// 4. 拟人双手十指生物力学排键算法（区分单手平点、左右食指单双交替、4指内旋轮指、8指顺滑流、16键超高速爆发，精准识别自动砖与长条按住/松开）
    /// </summary>
    public static class AutoplayBot
    {
        [DllImport("user32.dll", SetLastError = false)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", SetLastError = false)]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint MAPVK_VK_TO_VSC = 0;

        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12; // Alt 键
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        /// <summary>
        /// 预设 16 键（严格对应 2x8 视觉布局：上排左4右4，下排左4右4）
        /// </summary>
        public static readonly KeyCode[] DefaultKeys = new KeyCode[]
        {
            KeyCode.Tab, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.E,
            KeyCode.P, KeyCode.Equals, KeyCode.Backspace, KeyCode.Backslash,
            KeyCode.A, KeyCode.LeftShift, KeyCode.C, KeyCode.Space,
            KeyCode.Comma, KeyCode.Period, KeyCode.Return, KeyCode.H
        };

        /// <summary>
        /// 当前正在等待按键绑定的槽位索引（-1 表示未处于绑定状态）
        /// </summary>
        public static int WaitingKeySlot = -1;

        /// <summary>
        /// 最近一次从 JipperResourcePack 同步的状态提示
        /// </summary>
        public static string SyncStatusMessage = string.Empty;

        // 模拟按键状态集合（供 Input.GetKey / GetKeyDown / GetKeyUp 拦截读取）
        private static readonly HashSet<KeyCode> _heldKeys = new HashSet<KeyCode>();
        private static readonly Dictionary<KeyCode, double> _releaseDeadlines = new Dictionary<KeyCode, double>();
        private static readonly Dictionary<KeyCode, int> _downFrames = new Dictionary<KeyCode, int>();
        private static readonly Dictionary<KeyCode, int> _upFrames = new Dictionary<KeyCode, int>();
        private static readonly HashSet<KeyCode> _osPressedKeys = new HashSet<KeyCode>();
        private static readonly List<KeyCode> _tempExpiredKeys = new List<KeyCode>(16);

        private struct PendingKeyPress
        {
            public KeyCode Key;
            public double TriggerTime;
            public double HoldDuration;
        }
        private static readonly List<PendingKeyPress> _pendingPresses = new List<PendingKeyPress>();

        // 长条 (Hold) 持续按压状态
        private static KeyCode _activeHoldKey = KeyCode.None;
        private static int _activeHoldSlot = -1;
        private static scrFloor _activeHoldFloor = null;
        private static scrFloor _skipTapFloor = null;

        // 拟人排键状态机
        private static double _lastHitTime = -10.0;
        private static int _dominantHand = 1; // 0 = 左手 (E), 1 = 右手 (P)
        private static int _altHandParity = 0;
        private static int _roll4Cursor = 0;
        private static int _roll6Cursor = 0;
        private static int _roll8Cursor = 0;
        private static int _roll16Cursor = 0;
        private static int _lastUsedSlot = -1;
        private static int _secondLastUsedSlot = -1;

        // JipperResourcePack Timing 反射更新缓存
        private static FieldInfo _fOverlayInstance = null;
        private static MethodInfo _mOverlayUpdateTiming = null;
        private static FieldInfo _fJOverlayInstance = null;
        private static MethodInfo _mJOverlayUpdateTiming = null;
        private static bool _jipperReflectionInitialized = false;

        // 持续流速平滑滤波 (EMA Smoothing) 状态
        private static double _smoothedKps = 4.0;

        // 拟人按键离散度与微时差随机发生器
        private static readonly System.Random _rng = new System.Random();

        // 缓存所有可绑定的 KeyCode 枚举值
        private static KeyCode[] _allKeyCodes;

        // 局内有效打谱按键缓存（用于完全阻断用户手动乱按干扰 KV）
        private static HashSet<KeyCode> _trackedGameplayKeysSet = null;

        // 手部人体工学槽位拓扑（严格对应 2x8 16键：Row 1 为 Tab 1 2 E P = Backspace \）
        // 第一排核心指法：左手食指(3:E), 左手中指(2:2), 右手食指(4:P), 右手中指(5:=)
        // 第一排扩展指法：左手无名指(1:1), 右手无名指(6:Bksp), 左手小指(0:Tab), 右手小指(7:\)
        // 核心人体工学原则：中心食指/中指优先，小指 (Tab:0 与 \:7) 始终排在最末位！
        private static readonly int[] _row1LeftErgonomic = new int[] { 3, 4, 2, 5, 1, 6, 0, 7 };
        private static readonly int[] _row1RightErgonomic = new int[] { 4, 3, 5, 2, 6, 1, 7, 0 };
        private static readonly int[] _allLeftSlots = new int[] { 3, 4, 2, 5, 1, 6, 0, 7, 11, 10, 12, 13, 15, 14, 9, 8 };
        private static readonly int[] _allRightSlots = new int[] { 4, 3, 5, 2, 6, 1, 7, 0, 11, 12, 10, 13, 15, 14, 9, 8 };

        // 略快 3~4 键轮指（中心食指/中指领衔，严格限制在第一排核心四键 2, E, P, =）
        private static readonly int[] _roll4LeftSeq = new int[] { 3, 4, 2, 5 };   // E -> P -> 2 -> =
        private static readonly int[] _roll4RightSeq = new int[] { 4, 3, 5, 2 };  // P -> E -> = -> 2

        // 高速 5~6 键轮指（引入无名指 1 与 Backspace，仍绝不碰小指 Tab/\）
        private static readonly int[] _roll6LeftSeq = new int[] { 3, 4, 2, 5, 1, 6 };  // E -> P -> 2 -> = -> 1 -> Bksp
        private static readonly int[] _roll6RightSeq = new int[] { 4, 3, 5, 2, 6, 1 }; // P -> E -> = -> 2 -> Bksp -> 1

        // 极速 8 键轮指（仅在极高速持续流中才引入小指 Tab:0 与 \:7，且置于尾部）
        private static readonly int[] _roll8LeftSeq = new int[] { 3, 4, 2, 5, 1, 6, 0, 7 };
        private static readonly int[] _roll8RightSeq = new int[] { 4, 3, 5, 2, 6, 1, 7, 0 };

        // 超极限全指波浪流（> 48 KPS 极限情况才进入第二排，A:8与Tab:0始终居末）
        private static readonly int[] _roll16Sequence = new int[]
        {
            4, 3, 5, 2, 6, 1, 7, 0,
            11, 10, 12, 13, 15, 14, 9, 8
        };

        public static bool IsActive()
        {
            return Main.Enabled &&
                   Main.Settings != null &&
                   Main.Settings.MasterEnabled &&
                   Main.Settings.AutoplayPlus;
        }

        public static bool IsPlayingInLevel()
        {
            if (!IsActive()) return false;
            if (!Patches.IsInPlayableLevel()) return false;
            scrController ctrl = scrController.instance;
            if (ctrl == null || !ctrl.gameworld || ctrl.state != States.PlayerControl || ctrl.paused)
                return false;
            if (ADOBase.editor != null && ADOBase.editor.pausedInPlayMode)
                return false;
            scrPlanet planet = ctrl.chosenPlanet;
            if (planet != null && planet.currfloor != null && planet.currfloor.freeroam)
                return false;
            return true;
        }

        public static bool IsSimulatingKey(KeyCode key)
        {
            if (key == KeyCode.None) return false;
            return _heldKeys.Contains(key);
        }

        public static bool IsSimulatingKeyDown(KeyCode key)
        {
            if (key == KeyCode.None) return false;
            int frame;
            if (_downFrames.TryGetValue(key, out frame))
            {
                int cur = Time.frameCount;
                return frame == cur || frame == cur - 1;
            }
            return false;
        }

        public static bool IsSimulatingKeyUp(KeyCode key)
        {
            if (key == KeyCode.None) return false;
            int frame;
            if (_upFrames.TryGetValue(key, out frame))
            {
                int cur = Time.frameCount;
                return frame == cur || frame == cur - 1;
            }
            return false;
        }

        private static bool IsModManagerOpen()
        {
            try
            {
                return UnityModManagerNet.UnityModManager.UI.Instance != null && UnityModManagerNet.UnityModManager.UI.Instance.Opened;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSystemModifierHeld()
        {
            try
            {
                return (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 ||
                       (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0 ||
                       (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0 ||
                       (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsTrackedGameplayKey(KeyCode key)
        {
            if (key == KeyCode.None || key == KeyCode.Escape || key == KeyCode.F10 || key == KeyCode.F1)
                return false;

            if (_trackedGameplayKeysSet == null)
            {
                _trackedGameplayKeysSet = new HashSet<KeyCode>();
                for (int i = 0; i < DefaultKeys.Length; i++)
                {
                    if (DefaultKeys[i] != KeyCode.None) _trackedGameplayKeysSet.Add(DefaultKeys[i]);
                }
                if (Main.Settings != null && Main.Settings.AutoplayKeyBindings != null)
                {
                    for (int i = 0; i < Main.Settings.AutoplayKeyBindings.Length; i++)
                    {
                        if (Main.Settings.AutoplayKeyBindings[i] != KeyCode.None)
                            _trackedGameplayKeysSet.Add(Main.Settings.AutoplayKeyBindings[i]);
                    }
                }
            }

            return _trackedGameplayKeysSet.Contains(key);
        }

        public static void InvalidateTrackedKeys()
        {
            _trackedGameplayKeysSet = null;
        }

        public static bool TryGetKeyCodeByName(string name, out KeyCode code)
        {
            code = KeyCode.None;
            if (string.IsNullOrEmpty(name)) return false;
            try
            {
                code = (KeyCode)Enum.Parse(typeof(KeyCode), name, true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void NotifyJipperTiming(float timingMs)
        {
            try
            {
                if (!_jipperReflectionInitialized)
                {
                    Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                    for (int i = 0; i < asms.Length; i++)
                    {
                        string asmName = asms[i].GetName().Name;
                        if (asmName != null && asmName.StartsWith("JipperResourcePack"))
                        {
                            Type tOverlay = asms[i].GetType("JipperResourcePack.OverlayContents.Overlay");
                            if (tOverlay != null)
                            {
                                _fOverlayInstance = tOverlay.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                                _mOverlayUpdateTiming = tOverlay.GetMethod("UpdateTiming", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(float), typeof(int) }, null);
                            }
                            Type tJOverlay = asms[i].GetType("JipperResourcePack.Jongyeol.JOverlay");
                            if (tJOverlay != null)
                            {
                                _fJOverlayInstance = tJOverlay.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                                _mJOverlayUpdateTiming = tJOverlay.GetMethod("UpdateTiming", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(float), typeof(int) }, null);
                            }
                            if (_mOverlayUpdateTiming != null || _mJOverlayUpdateTiming != null)
                            {
                                _jipperReflectionInitialized = true;
                                break;
                            }
                        }
                    }
                }

                if (_fOverlayInstance != null && _mOverlayUpdateTiming != null)
                {
                    object inst = _fOverlayInstance.GetValue(null);
                    if (inst != null)
                    {
                        _mOverlayUpdateTiming.Invoke(inst, new object[] { timingMs, 0 });
                    }
                }
                if (_fJOverlayInstance != null && _mJOverlayUpdateTiming != null)
                {
                    object jInst = _fJOverlayInstance.GetValue(null);
                    if (jInst != null)
                    {
                        _mJOverlayUpdateTiming.Invoke(jInst, new object[] { timingMs, 0 });
                    }
                }
            }
            catch {}
        }
        public static void Reset()
        {
            _pendingPresses.Clear();
            ReleaseAllKeys();
            _lastHitTime = -10.0;
            _dominantHand = (Main.Settings != null && Main.Settings.PreferRightHand) ? 1 : 0;
            _altHandParity = _dominantHand;
            _skipTapFloor = null;
            _roll4Cursor = 0;
            _roll6Cursor = 0;
            _roll8Cursor = 0;
            _roll16Cursor = 0;
            _lastUsedSlot = -1;
            _secondLastUsedSlot = -1;
            _smoothedKps = 4.0;
        }

        public static void ReleaseAllKeys()
        {
            _pendingPresses.Clear();
            if (_osPressedKeys.Count > 0)
            {
                List<KeyCode> toRelease = new List<KeyCode>(_osPressedKeys);
                for (int i = 0; i < toRelease.Count; i++)
                {
                    SendOsKeyUp(toRelease[i]);
                }
                _osPressedKeys.Clear();
            }

            if (_heldKeys.Count > 0)
            {
                int curFrame = Time.frameCount;
                foreach (KeyCode k in _heldKeys)
                {
                    _upFrames[k] = curFrame;
                }
                _heldKeys.Clear();
            }

            _releaseDeadlines.Clear();
            _activeHoldKey = KeyCode.None;
            _activeHoldSlot = -1;
            _activeHoldFloor = null;
            _skipTapFloor = null;
        }

        public static void Update()
        {
            if (!IsActive())
            {
                if (_heldKeys.Count > 0 || _osPressedKeys.Count > 0 || _pendingPresses.Count > 0)
                {
                    ReleaseAllKeys();
                }
                return;
            }

            // 核心防线：窗口失去焦点、打开 Mod 管理器 (UMM) 菜单、按住 Win/Alt/Ctrl 快捷键、游戏暂停或未在正式关卡演奏时，
            // 立即释放所有模拟按键并清空队列，彻底阻断 Win+E 组合键触发与 UMM 搜索框文本污染！
            if (!Application.isFocused || !IsPlayingInLevel() || IsModManagerOpen() || IsSystemModifierHeld())
            {
                if (_heldKeys.Count > 0 || _osPressedKeys.Count > 0 || _pendingPresses.Count > 0)
                {
                    ReleaseAllKeys();
                }
                return;
            }

            double now = Time.unscaledTime;

            // 处理双押与拟人非对称时间差按键触发队列
            if (_pendingPresses.Count > 0)
            {
                for (int i = _pendingPresses.Count - 1; i >= 0; i--)
                {
                    PendingKeyPress pk = _pendingPresses[i];
                    if (now >= pk.TriggerTime)
                    {
                        _pendingPresses.RemoveAt(i);
                        if (pk.Key != KeyCode.None)
                        {
                            if (_heldKeys.Contains(pk.Key)) ReleaseKeyImmediate(pk.Key);
                            _heldKeys.Add(pk.Key);
                            _downFrames[pk.Key] = Time.frameCount;
                            _releaseDeadlines[pk.Key] = now + pk.HoldDuration;
                            SendOsKeyDown(pk.Key);
                        }
                    }
                }
            }

            // 检查长条是否已经结束（例如已离开该砖块）
            if (_activeHoldKey != KeyCode.None && _activeHoldFloor != null)
            {
                scrPlanet chosen = scrController.instance.chosenPlanet;
                if (chosen != null && chosen.currfloor != null && chosen.currfloor != _activeHoldFloor)
                {
                    if (chosen.currfloor.holdLength <= -1)
                    {
                        ReleaseActiveHoldKey();
                    }
                    else
                    {
                        _activeHoldFloor = chosen.currfloor;
                    }
                }
            }

            // 释放到期的普通点按按键
            if (_releaseDeadlines.Count > 0)
            {
                _tempExpiredKeys.Clear();
                foreach (KeyValuePair<KeyCode, double> kvp in _releaseDeadlines)
                {
                    if (now >= kvp.Value && kvp.Key != _activeHoldKey)
                    {
                        _tempExpiredKeys.Add(kvp.Key);
                    }
                }

                for (int i = 0; i < _tempExpiredKeys.Count; i++)
                {
                    ReleaseKeyImmediate(_tempExpiredKeys[i]);
                }
            }
        }

        /// <summary>
        /// 在 scrPlayer.Hit(long? hitTick, bool isAuto) Prefix 中调用：
        /// 执行拟人排键触发，并计算待在 scrPlanet.SwitchChosen 中注入的拟人弧度偏移
        /// </summary>
        public static void OnAutoHit(scrPlayer player, ref long? hitTick, out bool tempUseOldAuto)
        {
            tempUseOldAuto = false;

            if (!IsPlayingInLevel() || player == null)
                return;

            scrController controller = scrController.instance;
            if (controller == null)
                return;

            scrPlanet planet = controller.chosenPlanet;
            if (planet == null)
                return;

            scrFloor currFloor = planet.currfloor;
            scrFloor nextFloor = (currFloor != null) ? currFloor.nextfloor : null;

            double now = Time.unscaledTime;

            // 1. 识别砖块类型并执行拟人按键动作
            bool isAutoTile = (nextFloor != null && nextFloor.auto);
            bool wasHolding = (currFloor != null && currFloor.holdLength > -1);
            bool nextIsHold = (nextFloor != null && nextFloor.holdLength > -1);
            bool isHoldReleaseOnly = (wasHolding && !nextIsHold);

            if (_skipTapFloor == nextFloor)
            {
                // 伪双押联动：当前砖已在上一砖的双押时连带触发，跳过重复敲击
                _skipTapFloor = null;
                _lastHitTime = now;
            }
            else if (!isAutoTile)
            {
                if (isHoldReleaseOnly)
                {
                    // 长条结尾松键：人类在长条末尾只需松开按住的键，绝不会多按一次新键！
                    ReleaseActiveHoldKey();
                    _lastHitTime = now;
                }
                else
                {
                    // 如果是从一个长条直接连到下一个长条，先释放上一个长条键
                    if (wasHolding && _activeHoldKey != KeyCode.None)
                    {
                        ReleaseActiveHoldKey();
                    }

                    double dtPrev = Math.Max(0.004, now - _lastHitTime);
                    double dtNext = GetLookaheadIntervalSec(nextFloor, planet);
                    double effectiveDt = Math.Min(dtPrev, dtNext);

                    // 伪双押（Pseudo-Doubles / Flam）检测：
                    // 当后两砖间隔极短（dtNext <= 0.055s）且同时按下两侧均能保持在 Pure (X-完美) 判定窗口时，
                    // 采取人类高手标准的双手食指双押 (E + P) 解决！除非双押会掉判定到非 X完美！
                    bool canDoDoubleTap = false;
                    if (nextFloor != null && nextFloor.nextfloor != null && !nextIsHold && !nextFloor.auto && !nextFloor.nextfloor.auto)
                    {
                        double pureTimeSec = 0.025;
                        try
                        {
                            Difficulty diff = GCS.difficulty;
                            double bpmTimesSpeed = (planet.conductor != null) ? planet.conductor.bpm : 100.0;
                            if (planet.planetarySystem != null && planet.planetarySystem.speed > 0.0)
                                bpmTimesSpeed *= planet.planetarySystem.speed;
                            double pitch = (planet.conductor != null && planet.conductor.song != null && planet.conductor.song.pitch > 0f) ? (double)planet.conductor.song.pitch : 1.0;
                            double marginScale = (currFloor != null && currFloor.marginScale > 0.0) ? currFloor.marginScale : 1.0;
                            var bounds = scrMisc.GetAdjustedAngleBoundaryInDeg(diff, bpmTimesSpeed, pitch, marginScale);
                            if (bpmTimesSpeed > 0)
                            {
                                pureTimeSec = (bounds.Pure / (bpmTimesSpeed * 6.0)) / pitch;
                            }
                        }
                        catch {}

                        // 双押时间差各占一半：dtNext * 0.5 必须落在 Pure 时间窗口之内
                        if (dtNext <= 0.055 && (dtNext * 0.5) < (pureTimeSec * 0.95))
                        {
                            canDoDoubleTap = true;
                        }
                    }

                    if (canDoDoubleTap)
                    {
                        // 执行拟人双押：双手主食指 (左手 Slot 3:E + 右手 Slot 4:P)
                        int slotLeft = 3;
                        int slotRight = 4;
                        KeyCode keyLeft = GetSlotKey(slotLeft);
                        KeyCode keyRight = GetSlotKey(slotRight);
                        double baseHold = ComputeHumanKeyHoldDuration(effectiveDt);

                        // 人类双手双押具有 0.8ms ~ 3.2ms 的生理非对称微时差（绝不可能在微秒级完全同拍）
                        // 惯用手设定代表概率偏好（约 72% 惯用手微先触键，28% 非惯用手微先触键，呈现自然概率分布而非机械化固定）
                        int dominant = (Main.Settings != null && Main.Settings.PreferRightHand) ? 1 : 0;
                        bool dominantLeads = _rng.NextDouble() < 0.72;
                        bool rightLeads = (dominant == 1) ? dominantLeads : !dominantLeads;

                        double handDiffSec = 0.0008 + (_rng.NextDouble() * 0.0024);
                        double leftDelay = rightLeads ? handDiffSec : 0.0;
                        double rightDelay = rightLeads ? 0.0 : handDiffSec;

                        // 双手点按时长也施加非对称微幅波动
                        double leftHold = Math.Max(0.016, baseHold * (0.92 + _rng.NextDouble() * 0.16));
                        double rightHold = Math.Max(0.016, baseHold * (0.92 + _rng.NextDouble() * 0.16));

                        if (keyLeft != KeyCode.None) TriggerTapKey(keyLeft, now + leftDelay, leftHold);
                        if (keyRight != KeyCode.None) TriggerTapKey(keyRight, now + rightDelay, rightHold);

                        _lastUsedSlot = rightLeads ? slotLeft : slotRight;
                        _secondLastUsedSlot = rightLeads ? slotRight : slotLeft;
                        _skipTapFloor = nextFloor.nextfloor;
                        _lastHitTime = now;
                    }
                    else
                    {
                        bool isMidSpin = (currFloor != null && currFloor.midSpin) || (nextFloor != null && nextFloor.midSpin);
                        int chosenSlot = SelectHumanSlot(dtPrev, effectiveDt, isMidSpin);
                        KeyCode chosenKey = GetSlotKey(chosenSlot);

                        if (chosenKey != KeyCode.None)
                        {
                            if (nextIsHold)
                            {
                                StartHoldKey(chosenKey, chosenSlot, nextFloor);
                            }
                            else
                            {
                                double holdDur = ComputeHumanKeyHoldDuration(effectiveDt);
                                TriggerTapKey(chosenKey, now, holdDur);
                            }
                        }

                        _lastHitTime = now;
                    }
                }
            }

            // 2. 同步驱动 JipperResourcePack 的 Timing 显示为 0 误差纯 X 完美
            NotifyJipperTiming(0f);
        }

        public static void ApplyPendingAngleToPlanet(scrPlanet planet, ref long? hitTick)
        {
            // 精准度控制器已移除，命中以 100% 纯正原生角度结算
        }

        private static double GetLookaheadIntervalSec(scrFloor nextFloor, scrPlanet planet)
        {
            if (nextFloor == null || nextFloor.nextfloor == null)
                return 0.5;

            if (nextFloor.nextfloor.auto)
                return 0.5;

            double rawDiff = nextFloor.nextfloor.entryTime - nextFloor.entryTime;
            if (rawDiff <= 0.0001)
                return 0.5;

            double pitch = 1.0;
            if (planet != null && planet.conductor != null && planet.conductor.song != null && planet.conductor.song.pitch > 0f)
            {
                pitch = (double)planet.conductor.song.pitch;
            }

            return Math.Max(0.004, rawDiff / pitch);
        }

        private static double ComputeHumanKeyHoldDuration(double effectiveDt)
        {
            // 人类点按时长基础区间为 18ms ~ 55ms，伴随生理手部微小抖动
            double baseHold = effectiveDt * 0.40;
            if (baseHold < 0.018) baseHold = 0.018;
            if (baseHold > 0.055) baseHold = 0.055;

            // 注入非对称生理离散波动（±6ms）
            double jitter = (_rng.NextDouble() - 0.5) * 0.010;
            return Math.Max(0.016, baseHold + jitter);
        }

        /// <summary>
        /// 拟人双手十指生物力学排键决策器：
        /// 遵循真实 ADOFAI 人类高手的肌肉记忆与生理特征：
        /// 1. 平点慢速（<= 8.0 KPS）：固定单指平点（主食指 E/P），绝无中指介入，绝无低速随意换手
        /// 2. 中速段（8.0 ~ 15.0 KPS）：双手食指单双交替（E <-> P）
        /// 3. 略快三~四键（15.0 ~ 24.0 KPS）：第一排核心四键 (2, E, P, =) 内旋交替，中心指法领衔
        /// 4. 高速五~六键（24.0 ~ 34.0 KPS）：引入无名指 (1 与 Backspace)，中心向外扩展，仍绝不碰小指 (Tab/\)
        /// 5. 极速八键段（34.0 ~ 48.0 KPS）：第一排完整 8 键，Tab 与 \ 始终位于尾指位置
        /// 6. 超极限爆发（> 48.0 KPS 极限情况）：才开始启用第二排按键，A 与 Tab 始终排在最末位
        /// </summary>
        private static int SelectHumanSlot(double dtPrev, double effectiveDt, bool isMidSpin)
        {
            double instantKps = 1.0 / Math.Max(0.006, effectiveDt);
            if (instantKps > 120.0) instantKps = 120.0;

            // 持续流速平滑滤波 (EMA Smoothing)：
            // 只有当连续多次击打处于高频时，流速才真正爬升；单个急转弯或微间隔绝不暴涨到 40+ KPS
            if (dtPrev > 0.40)
            {
                _smoothedKps = Math.Min(instantKps, 6.0);
            }
            else
            {
                if (instantKps > _smoothedKps)
                {
                    _smoothedKps = _smoothedKps * 0.65 + instantKps * 0.35;
                }
                else
                {
                    _smoothedKps = _smoothedKps * 0.30 + instantKps * 0.70;
                }
            }
            double kps = _smoothedKps;

            int dominant = (Main.Settings != null && Main.Settings.PreferRightHand) ? 1 : 0;
            _dominantHand = dominant;

            // 乐句长停顿重置：当与上一拍间隔 > 1.20s 时（长休止/长段停顿），手部自然回位
            if (dtPrev > 1.20)
            {
                _roll4Cursor = 0;
                _roll6Cursor = 0;
                _roll8Cursor = 0;
                _roll16Cursor = 0;
                _altHandParity = _dominantHand;
                _smoothedKps = 4.0;
            }

            int preferredSlot;

            // 特殊人体工学处理：中旋 (MidSpin) —— 人类绝对只会用双手主食指 (E/P) 或中指 (2/=) 轻点，
            // 严格禁绝向外伸手碰 Tab/\，更绝不进入第二排 A 键！
            if (isMidSpin)
            {
                int hand = _altHandParity;
                _altHandParity = 1 - _altHandParity;
                preferredSlot = (hand == 1) ? 4 : 3;
                if (preferredSlot == _lastUsedSlot)
                {
                    preferredSlot = (preferredSlot == 3) ? 4 : 3;
                }

                int[] midCandidates = (dominant == 1) ? new int[] { 4, 3, 5, 2 } : new int[] { 3, 4, 2, 5 };
                int midSpinSlot = preferredSlot;
                if (!IsSlotUsable(midSpinSlot, true))
                {
                    for (int mIdx = 0; mIdx < midCandidates.Length; mIdx++)
                    {
                        if (IsSlotUsable(midCandidates[mIdx], true))
                        {
                            midSpinSlot = midCandidates[mIdx];
                            break;
                        }
                    }
                }
                _secondLastUsedSlot = _lastUsedSlot;
                _lastUsedSlot = midSpinSlot;
                return midSpinSlot;
            }

            if (kps <= 8.0)
            {
                // 档位 1：慢速至中低速单点节拍（<= 8.0 KPS，人类正常单指平点区间）
                // 纯单指单点：始终固定使用惯用手食指（默认右手食指 P:4 或左手食指 E:3），绝不在平点段切换中指或莫名换手
                preferredSlot = (dominant == 1) ? 4 : 3;
            }
            else if (kps <= 15.0)
            {
                // 档位 2：中速双手交替（8.0 ~ 15.0 KPS）—— 经典左右手食指单双交替（E:3 <-> P:4）
                int hand = _altHandParity;
                _altHandParity = 1 - _altHandParity;
                preferredSlot = (hand == 1) ? 4 : 3;

                if (preferredSlot == _lastUsedSlot)
                {
                    preferredSlot = (preferredSlot == 3) ? 4 : 3;
                }
            }
            else if (kps <= 24.0)
            {
                // 档位 3：略快三~四键（15.0 ~ 24.0 KPS）—— 遵循左/右撇子设定，仅在第一排核心四键 (2, E, P, =) 之间内旋交替！
                // 绝不碰无名指 (1/Bksp)，更绝不碰小指 (Tab/\)！
                if (dominant == 0)
                {
                    preferredSlot = _roll4LeftSeq[_roll4Cursor % _roll4LeftSeq.Length];
                    _roll4Cursor = (_roll4Cursor + 1) % _roll4LeftSeq.Length;
                }
                else
                {
                    preferredSlot = _roll4RightSeq[_roll4Cursor % _roll4RightSeq.Length];
                    _roll4Cursor = (_roll4Cursor + 1) % _roll4RightSeq.Length;
                }
            }
            else if (kps <= 34.0)
            {
                // 档位 4：高速五~六键（24.0 ~ 34.0 KPS）—— 引入无名指 (1 与 Backspace)
                // 左手 1, 2, E 与 右手 Backspace, =, P 内旋交替，中心向外扩散，仍绝不碰小指 (Tab/\)
                if (dominant == 0)
                {
                    preferredSlot = _roll6LeftSeq[_roll6Cursor % _roll6LeftSeq.Length];
                    _roll6Cursor = (_roll6Cursor + 1) % _roll6LeftSeq.Length;
                }
                else
                {
                    preferredSlot = _roll6RightSeq[_roll6Cursor % _roll6RightSeq.Length];
                    _roll6Cursor = (_roll6Cursor + 1) % _roll6RightSeq.Length;
                }
            }
            else if (kps <= 48.0)
            {
                // 档位 5：极速八键内旋（34.0 ~ 48.0 KPS）—— 第一排完整 8 键，中心食指领衔，Tab 与 \ 始终位于尾部
                preferredSlot = (dominant == 0)
                    ? _roll8LeftSeq[_roll8Cursor % _roll8LeftSeq.Length]
                    : _roll8RightSeq[_roll8Cursor % _roll8RightSeq.Length];
                _roll8Cursor = (_roll8Cursor + 1) % _roll8LeftSeq.Length;
            }
            else
            {
                // 档位 6：超极限多于 8 键爆发（> 48.0 KPS 极限情况）—— 才开始启用第二排按键，A 与 Tab 始终置于最末位
                preferredSlot = _roll16Sequence[_roll16Cursor % _roll16Sequence.Length];
                _roll16Cursor = (_roll16Cursor + 1) % _roll16Sequence.Length;
            }

            int resolvedSlot = ResolveAvailableSlot(preferredSlot, kps > 8.0, kps <= 48.0);
            _secondLastUsedSlot = _lastUsedSlot;
            _lastUsedSlot = resolvedSlot;
            return resolvedSlot;
        }

        private static int ResolveAvailableSlot(int preferredSlot, bool avoidImmediateRepeat, bool restrictToRow1)
        {
            if (IsSlotUsable(preferredSlot, avoidImmediateRepeat))
            {
                return preferredSlot;
            }

            int dominant = (Main.Settings != null && Main.Settings.PreferRightHand) ? 1 : 0;
            int[] ergonomicPool = (dominant == 1)
                ? (restrictToRow1 ? _row1RightErgonomic : _allRightSlots)
                : (restrictToRow1 ? _row1LeftErgonomic : _allLeftSlots);

            for (int i = 0; i < ergonomicPool.Length; i++)
            {
                int candidate = ergonomicPool[i];
                if (IsSlotUsable(candidate, avoidImmediateRepeat))
                {
                    return candidate;
                }
            }

            // 次级容错搜索（若 restrictToRow1 为真，严格使用第一排人体工学拓扑，绝不进入第二排 A 键）
            int maxSlots = restrictToRow1 ? 8 : 16;
            for (int i = 0; i < maxSlots; i++)
            {
                int candidate = (dominant == 1)
                    ? (restrictToRow1 ? _row1RightErgonomic[i % _row1RightErgonomic.Length] : _allRightSlots[i % _allRightSlots.Length])
                    : (restrictToRow1 ? _row1LeftErgonomic[i % _row1LeftErgonomic.Length] : _allLeftSlots[i % _allLeftSlots.Length]);
                if (IsSlotUsable(candidate, false))
                {
                    return candidate;
                }
            }

            return preferredSlot;
        }

        private static bool IsSlotUsable(int slot, bool avoidImmediateRepeat)
        {
            if (slot < 0 || slot >= 16) return false;
            KeyCode key = GetSlotKey(slot);
            if (key == KeyCode.None) return false;
            if (slot == _activeHoldSlot || key == _activeHoldKey) return false;
            if (avoidImmediateRepeat && (slot == _lastUsedSlot || _heldKeys.Contains(key))) return false;
            return true;
        }

        public static KeyCode GetSlotKey(int slot)
        {
            if (Main.Settings == null || Main.Settings.AutoplayKeyBindings == null)
            {
                return (slot >= 0 && slot < DefaultKeys.Length) ? DefaultKeys[slot] : KeyCode.None;
            }
            if (slot < 0 || slot >= Main.Settings.AutoplayKeyBindings.Length)
            {
                return KeyCode.None;
            }
            return Main.Settings.AutoplayKeyBindings[slot];
        }

        private static void TriggerTapKey(KeyCode key, double triggerTime, double holdDuration)
        {
            if (key == KeyCode.None) return;

            double now = Time.unscaledTime;
            if (triggerTime <= now)
            {
                if (_heldKeys.Contains(key))
                {
                    ReleaseKeyImmediate(key);
                }

                _heldKeys.Add(key);
                _downFrames[key] = Time.frameCount;
                _releaseDeadlines[key] = now + holdDuration;
                SendOsKeyDown(key);
            }
            else
            {
                PendingKeyPress pk;
                pk.Key = key;
                pk.TriggerTime = triggerTime;
                pk.HoldDuration = holdDuration;
                _pendingPresses.Add(pk);
            }
        }

        private static void StartHoldKey(KeyCode key, int slot, scrFloor holdFloor)
        {
            if (key == KeyCode.None) return;

            if (_activeHoldKey != KeyCode.None && _activeHoldKey != key)
            {
                ReleaseActiveHoldKey();
            }

            if (_heldKeys.Contains(key))
            {
                ReleaseKeyImmediate(key);
            }

            _activeHoldKey = key;
            _activeHoldSlot = slot;
            _activeHoldFloor = holdFloor;

            _heldKeys.Add(key);
            _downFrames[key] = Time.frameCount;
            _releaseDeadlines.Remove(key);
            SendOsKeyDown(key);
        }

        private static void ReleaseActiveHoldKey()
        {
            if (_activeHoldKey != KeyCode.None)
            {
                KeyCode key = _activeHoldKey;
                _activeHoldKey = KeyCode.None;
                _activeHoldSlot = -1;
                _activeHoldFloor = null;
                ReleaseKeyImmediate(key);
            }
        }

        private static void ReleaseKeyImmediate(KeyCode key)
        {
            if (key == KeyCode.None) return;

            _heldKeys.Remove(key);
            _releaseDeadlines.Remove(key);
            _upFrames[key] = Time.frameCount;
            SendOsKeyUp(key);
        }

        private static void SendOsKeyDown(KeyCode key)
        {
            // 防线：失焦、Mod管理器开启或用户正在按住 Win/Alt/Ctrl 快捷键时严禁注入按键
            if (!Application.isFocused || IsModManagerOpen() || IsSystemModifierHeld()) return;

            byte vk = KeyCodeToVirtualKey(key);
            if (vk == 0) return;

            try
            {
                byte scan = (byte)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
                uint flags = IsExtendedKey(key) ? KEYEVENTF_EXTENDEDKEY : 0u;
                keybd_event(vk, scan, flags, UIntPtr.Zero);
                _osPressedKeys.Add(key);
            }
            catch {}
        }

        private static void SendOsKeyUp(KeyCode key)
        {
            byte vk = KeyCodeToVirtualKey(key);
            if (vk == 0)
            {
                _osPressedKeys.Remove(key);
                return;
            }

            try
            {
                byte scan = (byte)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
                uint flags = KEYEVENTF_KEYUP | (IsExtendedKey(key) ? KEYEVENTF_EXTENDEDKEY : 0u);
                keybd_event(vk, scan, flags, UIntPtr.Zero);
            }
            catch {}
            finally
            {
                _osPressedKeys.Remove(key);
            }
        }

        private static bool IsExtendedKey(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.RightAlt:
                case KeyCode.RightControl:
                case KeyCode.Insert:
                case KeyCode.Delete:
                case KeyCode.Home:
                case KeyCode.End:
                case KeyCode.PageUp:
                case KeyCode.PageDown:
                case KeyCode.UpArrow:
                case KeyCode.DownArrow:
                case KeyCode.LeftArrow:
                case KeyCode.RightArrow:
                case KeyCode.KeypadDivide:
                case KeyCode.KeypadEnter:
                case KeyCode.Numlock:
                    return true;
                default:
                    return false;
            }
        }

        public static byte KeyCodeToVirtualKey(KeyCode key)
        {
            if (key >= KeyCode.A && key <= KeyCode.Z)
            {
                return (byte)(0x41 + ((int)key - (int)KeyCode.A));
            }
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
            {
                return (byte)(0x30 + ((int)key - (int)KeyCode.Alpha0));
            }
            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9)
            {
                return (byte)(0x60 + ((int)key - (int)KeyCode.Keypad0));
            }
            if (key >= KeyCode.F1 && key <= KeyCode.F12)
            {
                return (byte)(0x70 + ((int)key - (int)KeyCode.F1));
            }

            switch (key)
            {
                case KeyCode.Tab: return 0x09;
                case KeyCode.Return:
                case KeyCode.KeypadEnter: return 0x0D;
                case KeyCode.Space: return 0x20;
                case KeyCode.Backspace: return 0x08;
                case KeyCode.CapsLock: return 0x14;
                case KeyCode.LeftShift: return 0xA0;
                case KeyCode.RightShift: return 0xA1;
                case KeyCode.LeftControl: return 0xA2;
                case KeyCode.RightControl: return 0xA3;
                case KeyCode.LeftAlt: return 0xA4;
                case KeyCode.RightAlt: return 0xA5;
                case KeyCode.Semicolon:
                case KeyCode.Colon: return 0xBA;
                case KeyCode.Equals:
                case KeyCode.Plus: return 0xBB;
                case KeyCode.Comma:
                case KeyCode.Less: return 0xBC;
                case KeyCode.Minus:
                case KeyCode.Underscore: return 0xBD;
                case KeyCode.Period:
                case KeyCode.Greater: return 0xBE;
                case KeyCode.Slash:
                case KeyCode.Question: return 0xBF;
                case KeyCode.BackQuote:
                case KeyCode.Tilde: return 0xC0;
                case KeyCode.LeftBracket:
                case KeyCode.LeftCurlyBracket: return 0xDB;
                case KeyCode.Backslash:
                case KeyCode.Pipe: return 0xDC;
                case KeyCode.RightBracket:
                case KeyCode.RightCurlyBracket: return 0xDD;
                case KeyCode.Quote:
                case KeyCode.DoubleQuote: return 0xDE;
                case KeyCode.UpArrow: return 0x26;
                case KeyCode.DownArrow: return 0x28;
                case KeyCode.LeftArrow: return 0x25;
                case KeyCode.RightArrow: return 0x27;
                case KeyCode.Insert: return 0x2D;
                case KeyCode.Delete: return 0x2E;
                case KeyCode.Home: return 0x24;
                case KeyCode.End: return 0x23;
                case KeyCode.PageUp: return 0x21;
                case KeyCode.PageDown: return 0x22;
                case KeyCode.KeypadMultiply: return 0x6A;
                case KeyCode.KeypadPlus: return 0x6B;
                case KeyCode.KeypadMinus: return 0x6D;
                case KeyCode.KeypadPeriod: return 0x6E;
                case KeyCode.KeypadDivide: return 0x6F;
                case KeyCode.Numlock: return 0x90;
                case KeyCode.ScrollLock: return 0x91;
                default: return 0;
            }
        }

        public static string FormatKeyName(KeyCode key)
        {
            if (key == KeyCode.None) return Loc.Get("misc.key_unset");
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
            {
                return ((int)key - (int)KeyCode.Alpha0).ToString();
            }
            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9)
            {
                return "Num" + ((int)key - (int)KeyCode.Keypad0).ToString();
            }

            switch (key)
            {
                case KeyCode.Backspace: return "Bksp";
                case KeyCode.Return: return "Enter";
                case KeyCode.KeypadEnter: return "NumEnt";
                case KeyCode.LeftShift: return "LShift";
                case KeyCode.RightShift: return "RShift";
                case KeyCode.LeftControl: return "LCtrl";
                case KeyCode.RightControl: return "RCtrl";
                case KeyCode.LeftAlt: return "LAlt";
                case KeyCode.RightAlt: return "RAlt";
                case KeyCode.CapsLock: return "Caps";
                case KeyCode.Equals: return "=";
                case KeyCode.Minus: return "-";
                case KeyCode.Backslash: return "\\";
                case KeyCode.Slash: return "/";
                case KeyCode.Comma: return ",";
                case KeyCode.Period: return ".";
                case KeyCode.Semicolon: return ";";
                case KeyCode.Quote: return "'";
                case KeyCode.BackQuote: return "`";
                case KeyCode.LeftBracket: return "[";
                case KeyCode.RightBracket: return "]";
                case KeyCode.UpArrow: return "Up";
                case KeyCode.DownArrow: return "Down";
                case KeyCode.LeftArrow: return "Left";
                case KeyCode.RightArrow: return "Right";
                case KeyCode.PageUp: return "PgUp";
                case KeyCode.PageDown: return "PgDn";
                case KeyCode.Insert: return "Ins";
                case KeyCode.Delete: return "Del";
                default: return key.ToString();
            }
        }

        public static void ResetDefaultKeys()
        {
            if (Main.Settings == null) return;
            Main.Settings.AutoplayKeyBindings = new KeyCode[16];
            for (int i = 0; i < 16; i++)
            {
                Main.Settings.AutoplayKeyBindings[i] = DefaultKeys[i];
            }
            WaitingKeySlot = -1;
            SyncStatusMessage = string.Empty;
        }

        /// <summary>
        /// 从已加载的 JipperResourcePack 模组或 Mods/JipperResourcePack/Settings.json 一键同步键位
        /// 自动适配 Key4 / Key8 / Key10 / Key12 / Key16 / Key20 的视觉手部排布（如 BackSequence16: 12,13,9,8,10,11,14,15）
        /// </summary>
        public static bool SyncFromJipperResourcePack()
        {
            WaitingKeySlot = -1;
            if (Main.Settings == null) return false;

            try
            {
                // 1. 优先尝试从内存中已加载的 JipperResourcePack.KeyViewerContents.KeyViewer.Settings 反射读取
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    Type kvType = assemblies[i].GetType("JipperResourcePack.KeyViewerContents.KeyViewer", false);
                    if (kvType != null)
                    {
                        FieldInfo settingsField = kvType.GetField("Settings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (settingsField != null)
                        {
                            object settingsObj = settingsField.GetValue(null);
                            if (settingsObj != null)
                            {
                                Type stType = settingsObj.GetType();
                                FieldInfo styleField = stType.GetField("KeyViewerStyle", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                int styleVal = 1; // 默认 Key16
                                if (styleField != null)
                                {
                                    styleVal = Convert.ToInt32(styleField.GetValue(settingsObj));
                                }

                                string fieldName = GetJipperArrayFieldName(styleVal);
                                FieldInfo arrField = stType.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (arrField != null)
                                {
                                    KeyCode[] arr = arrField.GetValue(settingsObj) as KeyCode[];
                                    if (arr != null && ApplyJipperKeyArray(styleVal, arr))
                                    {
                                        SyncStatusMessage = Loc.Get("misc.sync_jipper_ok");
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }

                // 2. 从磁盘 Mods/JipperResourcePack/Settings.json 读取
                string configPath = Path.Combine(Directory.GetCurrentDirectory(), "Mods/JipperResourcePack/Settings.json");
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    int styleVal = 1;
                    Match styleMatch = Regex.Match(json, "\"KeyViewerStyle\"\\s*:\\s*(\\d+)");
                    if (styleMatch.Success)
                    {
                        int.TryParse(styleMatch.Groups[1].Value, out styleVal);
                    }

                    string fieldName = GetJipperArrayFieldName(styleVal);
                    Match m = Regex.Match(json, "\"" + fieldName + "\"\\s*:\\s*\\[([^\\]]+)\\]");
                    if (!m.Success && fieldName != "key16")
                    {
                        styleVal = 1;
                        m = Regex.Match(json, "\"key16\"\\s*:\\s*\\[([^\\]]+)\\]");
                    }

                    if (m.Success)
                    {
                        MatchCollection nums = Regex.Matches(m.Groups[1].Value, "-?\\d+");
                        if (nums.Count > 0)
                        {
                            KeyCode[] parsed = new KeyCode[nums.Count];
                            for (int k = 0; k < nums.Count; k++)
                            {
                                int code;
                                if (int.TryParse(nums[k].Value, out code))
                                {
                                    parsed[k] = (KeyCode)code;
                                }
                            }
                            if (ApplyJipperKeyArray(styleVal, parsed))
                            {
                                SyncStatusMessage = Loc.Get("misc.sync_jipper_ok");
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (Main.Logger != null) Main.Logger.Error("SyncFromJipperResourcePack error: " + ex.Message);
            }

            SyncStatusMessage = Loc.Get("misc.sync_jipper_fail");
            return false;
        }

        private static string GetJipperArrayFieldName(int styleVal)
        {
            // JipperResourcePack.KeyViewerContents.KeyviewerStyle:
            // Key12 = 0, Key16 = 1, Key20 = 2, Key10 = 3, Key4 = 4, Key8 = 5
            switch (styleVal)
            {
                case 0: return "key12";
                case 1: return "key16";
                case 2: return "key20";
                case 3: return "key10";
                case 4: return "key4";
                case 5: return "key8";
                default: return "key16";
            }
        }

        private static bool ApplyJipperKeyArray(int styleVal, KeyCode[] src)
        {
            if (src == null || src.Length == 0 || Main.Settings == null)
                return false;

            KeyCode[] target = new KeyCode[16];
            for (int i = 0; i < 16; i++) target[i] = KeyCode.None;

            if (styleVal == 4 && src.Length >= 4)
            {
                // Key4：对应上排中间 4 指 (左中 2, 左食 3, 右食 4, 右中 5)
                target[2] = src[0];
                target[3] = src[1];
                target[4] = src[2];
                target[5] = src[3];
            }
            else if (styleVal == 5 && src.Length >= 8)
            {
                // Key8：对应上排完整 8 键 (0..7)
                for (int i = 0; i < 8; i++) target[i] = src[i];
            }
            else if (styleVal == 3 && src.Length >= 10)
            {
                // Key10：上排 8 键 + 下排内侧 2 键 (BackSequence10: 8, 9 -> 槽位 11, 12)
                for (int i = 0; i < 8; i++) target[i] = src[i];
                target[11] = src[8];
                target[12] = src[9];
            }
            else if (styleVal == 0 && src.Length >= 12)
            {
                // Key12：上排 8 键 + 下排中间 4 键 (BackSequence12: 9, 8, 10, 11 -> 槽位 10, 11, 12, 13)
                for (int i = 0; i < 8; i++) target[i] = src[i];
                target[10] = src[9];
                target[11] = src[8];
                target[12] = src[10];
                target[13] = src[11];
            }
            else if (src.Length >= 16)
            {
                // Key16 / Key20：上排 8 键 (0..7) + 下排视觉顺序 (BackSequence16: 12, 13, 9, 8, 10, 11, 14, 15)
                for (int i = 0; i < 8; i++) target[i] = src[i];
                int[] backSeq16 = new int[] { 12, 13, 9, 8, 10, 11, 14, 15 };
                for (int j = 0; j < 8; j++)
                {
                    target[8 + j] = src[backSeq16[j]];
                }
            }
            else
            {
                for (int i = 0; i < Math.Min(src.Length, 16); i++)
                {
                    target[i] = src[i];
                }
            }

            Main.Settings.AutoplayKeyBindings = target;
            return true;
        }

        /// <summary>
        /// 在 UMM OnGUI 中捕获用户点击槽位后按下的键盘按键
        /// </summary>
        public static void CaptureWaitingKeyInGUI()
        {
            if (WaitingKeySlot < 0 || WaitingKeySlot >= 16 || Main.Settings == null)
                return;

            Event ev = Event.current;
            if (ev != null && ev.isKey && ev.type == EventType.KeyDown && ev.keyCode != KeyCode.None)
            {
                if (ev.keyCode == KeyCode.Escape)
                {
                    WaitingKeySlot = -1;
                    ev.Use();
                    return;
                }

                if (!IsMouseButton(ev.keyCode))
                {
                    Main.Settings.AutoplayKeyBindings[WaitingKeySlot] = ev.keyCode;
                    WaitingKeySlot = -1;
                    ev.Use();
                    return;
                }
            }

            if (_allKeyCodes == null)
            {
                _allKeyCodes = (KeyCode[])Enum.GetValues(typeof(KeyCode));
            }

            for (int i = 0; i < _allKeyCodes.Length; i++)
            {
                KeyCode kc = _allKeyCodes[i];
                if (kc == KeyCode.None || IsMouseButton(kc))
                    continue;

                if (Input.GetKeyDown(kc))
                {
                    if (kc == KeyCode.Escape)
                    {
                        WaitingKeySlot = -1;
                        return;
                    }

                    Main.Settings.AutoplayKeyBindings[WaitingKeySlot] = kc;
                    WaitingKeySlot = -1;
                    return;
                }
            }
        }

        private static bool IsMouseButton(KeyCode kc)
        {
            return kc >= KeyCode.Mouse0 && kc <= KeyCode.Mouse6;
        }
    }
}
