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

        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint MAPVK_VK_TO_VSC = 0;

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

        // 长条 (Hold) 持续按压状态
        private static KeyCode _activeHoldKey = KeyCode.None;
        private static int _activeHoldSlot = -1;
        private static scrFloor _activeHoldFloor = null;

        // 待在 scrPlanet.SwitchChosen Prefix 中应用的拟人缓存角度（弧度 rad）
        private static bool _hasPendingAngle = false;
        private static double _pendingCandidateAngle = 0.0;

        // 拟人排键状态机
        private static double _lastHitTime = -10.0;
        private static int _dominantHand = 0; // 0 = 左手, 1 = 右手
        private static int _sameHandTapCount = 0;
        private static int _altHandParity = 0;
        private static int _roll4Cursor = 0;
        private static int _roll8Cursor = 0;
        private static int _roll16Cursor = 0;
        private static int _lastUsedSlot = -1;
        private static int _secondLastUsedSlot = -1;

        // 闭环 X-Accuracy 统计与自相关误差发生器
        private static readonly System.Random _rng = new System.Random();
        private static double _correlatedBias = 0.0;
        private static double _simulatedWeightedAccSum = 0.0;
        private static int _simulatedHitCount = 0;
        private static bool _lastEarlyLateSign = false;

        // 缓存所有可绑定的 KeyCode 枚举值
        private static KeyCode[] _allKeyCodes;

        // 手部人体工学槽位拓扑（对应 2x8 16键）
        // 左手主排（从食指到小指）：3(E), 2(2), 1(1), 0(Tab)
        // 右手主排（从食指到小指）：4(P), 5(=), 6(Bksp), 7(\)
        // 左手副排（从内侧拇指到小指）：11(Space), 10(C), 9(LShift), 8(A)
        // 右手副排（从内侧食指到小指）：12(,), 13(.), 14(Enter), 15(H)
        private static readonly int[] _leftAllSlots = new int[] { 3, 2, 1, 0, 11, 10, 9, 8 };
        private static readonly int[] _rightAllSlots = new int[] { 4, 5, 6, 7, 12, 13, 14, 15 };

        // 4指内旋轮指序列（左中->左食->右中->右食）
        private static readonly int[] _roll4Sequence = new int[] { 2, 3, 5, 4 };

        // 8指顺滑内旋流（左手外侧向内滑 Tab->1->2->E，右手外侧向内滑 \->Bksp->=->P）
        private static readonly int[] _roll8Sequence = new int[] { 0, 1, 2, 3, 7, 6, 5, 4 };

        // 16键超高速全指内旋波浪流
        private static readonly int[] _roll16Sequence = new int[]
        {
            0, 1, 2, 3, 7, 6, 5, 4,
            8, 9, 10, 11, 15, 14, 13, 12
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

        public static void Reset()
        {
            ReleaseAllKeys();
            _hasPendingAngle = false;
            _pendingCandidateAngle = 0.0;
            _lastHitTime = -10.0;
            _dominantHand = _rng.Next(2);
            _sameHandTapCount = 0;
            _altHandParity = 0;
            _roll4Cursor = 0;
            _roll8Cursor = 0;
            _roll16Cursor = 0;
            _lastUsedSlot = -1;
            _secondLastUsedSlot = -1;
            _correlatedBias = 0.0;
            _simulatedWeightedAccSum = 0.0;
            _simulatedHitCount = 0;
        }

        public static void ReleaseAllKeys()
        {
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
        }

        public static void Update()
        {
            if (!IsActive())
            {
                if (_heldKeys.Count > 0 || _osPressedKeys.Count > 0)
                {
                    ReleaseAllKeys();
                }
                return;
            }

            // 窗口失去焦点或不在正式关卡 PlayerControl 状态时立即释放所有模拟按键，防止污染主菜单或外部系统输入
            if (!Application.isFocused || !IsPlayingInLevel())
            {
                if (_heldKeys.Count > 0 || _osPressedKeys.Count > 0)
                {
                    ReleaseAllKeys();
                }
                return;
            }

            double now = Time.unscaledTime;

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
            _hasPendingAngle = false;

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

            if (!isAutoTile)
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

                    int chosenSlot = SelectHumanSlot(dtPrev, effectiveDt);
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

            // 2. 拟人目标 X 精准度计算（当目标精准度 < 99.99% 且非自动砖/非中旋/非长条松尾时生效）
            float targetAccPercent = Main.Settings.AutoplayAccuracy;
            if (targetAccPercent >= 99.99f || isAutoTile || isHoldReleaseOnly)
            {
                return;
            }

            if ((currFloor != null && currFloor.midSpin) || (nextFloor != null && nextFloor.midSpin))
            {
                return;
            }

            ApplyHumanizedAccuracyOffset(planet, currFloor, nextFloor, targetAccPercent, ref hitTick, out tempUseOldAuto);
        }

        /// <summary>
        /// 在 scrPlanet.SwitchChosen(long? hitTick) Prefix 中调用：
        /// 此时 scrPlayer.Hit 已执行完 cachedAngle = angle，在此处写入拟人角度即可同时生效于 SwitchChosen 与 UpdateHitErrorMeter
        /// </summary>
        public static void ApplyPendingAngleToPlanet(scrPlanet planet, ref long? hitTick)
        {
            if (_hasPendingAngle && planet != null && IsActive())
            {
                planet.cachedAngle = _pendingCandidateAngle;
                hitTick = null;
                _hasPendingAngle = false;
            }
        }

        private static void ApplyHumanizedAccuracyOffset(
            scrPlanet planet,
            scrFloor currFloor,
            scrFloor nextFloor,
            float targetAccPercent,
            ref long? hitTick,
            out bool tempUseOldAuto)
        {
            tempUseOldAuto = false;

            try
            {
                double pitch = 1.0;
                double baseBpm = 100.0;
                double speed = 1.0;

                if (planet.conductor != null)
                {
                    baseBpm = planet.conductor.bpm;
                    if (planet.conductor.song != null && planet.conductor.song.pitch > 0f)
                    {
                        pitch = (double)planet.conductor.song.pitch;
                    }
                }
                if (planet.planetarySystem != null && planet.planetarySystem.speed > 0.0)
                {
                    speed = planet.planetarySystem.speed;
                }

                float bpmTimesSpeed = (float)(baseBpm * speed);
                if (bpmTimesSpeed <= 0f) bpmTimesSpeed = 100f;

                double marginScale = 1.0;
                if (currFloor != null && currFloor.marginScale > 0.0)
                {
                    marginScale = currFloor.marginScale;
                }

                Difficulty diff = GCS.difficulty;

                // 注意签名：GetAdjustedAngleBoundaryInDeg(Difficulty difficulty, double bpmTimesSpeed, double conductorPitch, double marginMult)
                // 返回值为角度（度 °，例如 30° / 45° / 60°）
                scrMisc.HitMarginGeneralWithXPerfectValues<double> bounds =
                    scrMisc.GetAdjustedAngleBoundaryInDeg(diff, (double)bpmTimesSpeed, pitch, marginScale);

                double pureBound = bounds.Pure;
                double perfectBound = bounds.Perfect;
                double countedBound = bounds.Counted;

                if (pureBound <= 0.001 || countedBound <= pureBound + 0.05)
                {
                    return;
                }

                double targetXAcc = Math.Max(0.50, Math.Min(1.0, (double)targetAccPercent / 100.0));
                double currentXAcc = (_simulatedHitCount > 0)
                    ? (_simulatedWeightedAccSum / (double)_simulatedHitCount)
                    : targetXAcc;

                // 更新自相关人类节奏漂移（AR(1) 过程：模拟人类连续几拍偏早或偏晚的生理惯性）
                double u1 = Math.Max(1e-7, _rng.NextDouble());
                double u2 = _rng.NextDouble();
                double stdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                _correlatedBias = 0.65 * _correlatedBias + 0.35 * stdNormal;

                // 决定本次击打的目标判定区间：
                // PerfectPlus = 1.0 权重, Early/LatePerfect = 0.75 权重, VeryEarly/Late (Counted) = 0.001 权重
                int desiredTier = 0; // 0 = Pure Perfect, 1 = Early/Late Perfect, 2 = Counted (VeryEarly/Late)

                if (targetXAcc >= 0.75)
                {
                    double pPerfectTier = (1.0 - targetXAcc) / 0.25;
                    double accError = currentXAcc - targetXAcc;
                    pPerfectTier = Math.Max(0.0, Math.Min(1.0, pPerfectTier + accError * 3.5));

                    bool canUseCounted = (targetXAcc < 0.86 && countedBound > perfectBound + 0.5 && currentXAcc > targetXAcc + 0.015);
                    if (canUseCounted && Math.Abs(_correlatedBias) > 2.1 && _rng.NextDouble() < 0.08)
                    {
                        desiredTier = 2;
                    }
                    else if (perfectBound > pureBound + 0.2 && _rng.NextDouble() < pPerfectTier)
                    {
                        desiredTier = 1;
                    }
                    else
                    {
                        desiredTier = 0;
                    }
                }
                else
                {
                    if (currentXAcc > targetXAcc + 0.005 && countedBound > perfectBound + 0.2)
                    {
                        desiredTier = 2;
                    }
                    else if (currentXAcc > targetXAcc - 0.02 && perfectBound > pureBound + 0.2)
                    {
                        desiredTier = (_rng.NextDouble() < 0.65) ? 1 : 0;
                    }
                    else
                    {
                        desiredTier = 0;
                    }
                }

                // 在选定的合法判定区间内采样度数偏移量（度 °）
                double absDegOffset;
                if (desiredTier == 2 && countedBound > perfectBound + 0.2)
                {
                    double low = perfectBound + (countedBound - perfectBound) * 0.15;
                    double high = perfectBound + (countedBound - perfectBound) * 0.78;
                    double t = 0.5 + 0.25 * Math.Tanh(_correlatedBias);
                    absDegOffset = low + (high - low) * t;
                }
                else if (desiredTier == 1 && perfectBound > pureBound + 0.2)
                {
                    double low = pureBound + (perfectBound - pureBound) * 0.16;
                    double high = pureBound + (perfectBound - pureBound) * 0.82;
                    double t = 0.5 + 0.30 * Math.Tanh(_correlatedBias);
                    absDegOffset = low + (high - low) * t;
                }
                else
                {
                    double maxPure = pureBound * 0.75;
                    double frac = Math.Min(0.95, Math.Abs(_correlatedBias) * 0.35);
                    absDegOffset = maxPure * frac;
                }

                bool isLate;
                if (Math.Abs(_correlatedBias) > 0.15)
                {
                    isLate = (_correlatedBias > 0.0);
                }
                else
                {
                    isLate = (_rng.NextDouble() < 0.75) ? _lastEarlyLateSign : !_lastEarlyLateSign;
                }
                _lastEarlyLateSign = isLate;

                // scrPlanet.angle / targetExitAngle / cachedAngle 及 GetHitMarginInDeg 内部均使用弧度 (Radians)！
                double absRadOffset = absDegOffset * (Math.PI / 180.0);
                bool isCW = (planet.planetarySystem != null) ? planet.planetarySystem.isCW : (currFloor == null || !currFloor.isCCW);
                double signedRadDiff = isLate ? absRadOffset : -absRadOffset;
                double candidateAngle = planet.targetExitAngle + (isCW ? signedRadDiff : -signedRadDiff);

                // 终极安全校验：直接调用游戏底层 GetHitMarginInDeg + HitMarginHelper.IsCounted 验证！
                HitMargin verifiedMargin = scrMisc.GetHitMarginInDeg(
                    diff,
                    (float)candidateAngle,
                    (float)planet.targetExitAngle,
                    isCW,
                    bpmTimesSpeed,
                    (float)pitch,
                    marginScale);

                if (HitMarginHelper.IsCounted(verifiedMargin, GCS.hitMarginLimit))
                {
                    _hasPendingAngle = true;
                    _pendingCandidateAngle = candidateAngle;
                    planet.cachedAngle = candidateAngle;
                    hitTick = null;
                    tempUseOldAuto = true;

                    double weight = 1.0;
                    if (verifiedMargin == HitMargin.EarlyPerfect || verifiedMargin == HitMargin.LatePerfect)
                    {
                        weight = 0.75;
                    }
                    else if (verifiedMargin == HitMargin.VeryEarly || verifiedMargin == HitMargin.VeryLate)
                    {
                        weight = 0.001;
                    }

                    _simulatedWeightedAccSum += weight;
                    _simulatedHitCount++;
                }
                else
                {
                    _simulatedWeightedAccSum += 1.0;
                    _simulatedHitCount++;
                }
            }
            catch
            {
                _hasPendingAngle = false;
                tempUseOldAuto = false;
            }
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
            double baseHold = effectiveDt * 0.42;
            if (baseHold < 0.020) baseHold = 0.020;
            if (baseHold > 0.068) baseHold = 0.068;

            double jitter = (_rng.NextDouble() - 0.5) * 0.008;
            return Math.Max(0.016, baseHold + jitter);
        }

        /// <summary>
        /// 拟人双手十指生物力学排键决策器
        /// </summary>
        private static int SelectHumanSlot(double dtPrev, double effectiveDt)
        {
            double kps = 1.0 / Math.Max(0.004, effectiveDt);

            // 乐句停顿重置：当与上一拍间隔 > 300ms 时，手部自然回位到主食指
            if (dtPrev > 0.30)
            {
                _sameHandTapCount = 0;
                _roll4Cursor = 0;
                _roll8Cursor = 0;
                if (_rng.NextDouble() < 0.35)
                {
                    _dominantHand = 1 - _dominantHand;
                }
                _altHandParity = _dominantHand;
            }

            int preferredSlot;

            if (kps <= 3.8)
            {
                // 档位 1：慢速节拍（<= 3.8 KPS）—— 人类以惯用食指平点为主（E 或 P），连续数拍后自然换手或微调中指
                _sameHandTapCount++;
                if (_sameHandTapCount > 5 + _rng.Next(5))
                {
                    _dominantHand = 1 - _dominantHand;
                    _sameHandTapCount = 1;
                }

                bool useMiddleFinger = (_sameHandTapCount % 4 == 0 && _rng.NextDouble() < 0.22);
                if (_dominantHand == 0)
                {
                    preferredSlot = useMiddleFinger ? 2 : 3; // 左食指 E(3) 或左中指 2(2)
                }
                else
                {
                    preferredSlot = useMiddleFinger ? 5 : 4; // 右食指 P(4) 或右中指 =(5)
                }
            }
            else if (kps <= 10.5)
            {
                // 档位 2：中速交互（3.8 ~ 10.5 KPS）—— 经典左右手食指单双交替（E <-> P），中高密度时偶发中指分担负荷
                int hand = _altHandParity;
                _altHandParity = 1 - _altHandParity;
                _dominantHand = hand;
                _sameHandTapCount = 0;

                bool useMiddle = (kps > 7.2 && _rng.NextDouble() < 0.28);
                if (hand == 0)
                {
                    preferredSlot = useMiddle ? 2 : 3; // 左手 2 / E
                }
                else
                {
                    preferredSlot = useMiddle ? 5 : 4; // 右手 = / P
                }

                if (preferredSlot == _lastUsedSlot)
                {
                    preferredSlot = (hand == 0) ? (preferredSlot == 3 ? 2 : 3) : (preferredSlot == 4 ? 5 : 4);
                }
            }
            else if (kps <= 19.0)
            {
                // 档位 3：快速四指内旋轮指（10.5 ~ 19.0 KPS）—— 左中(2)->左食(E)->右中(=)->右食(P)
                preferredSlot = _roll4Sequence[_roll4Cursor % _roll4Sequence.Length];
                _roll4Cursor = (_roll4Cursor + 1) % _roll4Sequence.Length;
                _altHandParity = (_roll4Cursor < 2) ? 1 : 0;
            }
            else if (kps <= 32.0)
            {
                // 档位 4：高速八指顺滑内旋流（19.0 ~ 32.0 KPS）—— 左手 Tab->1->2->E 顺滑内滚，右手 \->Bksp->=->P 顺滑内滚
                preferredSlot = _roll8Sequence[_roll8Cursor % _roll8Sequence.Length];
                _roll8Cursor = (_roll8Cursor + 1) % _roll8Sequence.Length;
            }
            else
            {
                // 档位 5：超高速全指爆发（> 32.0 KPS）—— 16键双排顺滑内旋波浪
                preferredSlot = _roll16Sequence[_roll16Cursor % _roll16Sequence.Length];
                _roll16Cursor = (_roll16Cursor + 1) % _roll16Sequence.Length;
            }

            int resolvedSlot = ResolveAvailableSlot(preferredSlot, kps > 4.0);
            _secondLastUsedSlot = _lastUsedSlot;
            _lastUsedSlot = resolvedSlot;
            return resolvedSlot;
        }

        private static int ResolveAvailableSlot(int preferredSlot, bool avoidImmediateRepeat)
        {
            if (IsSlotUsable(preferredSlot, avoidImmediateRepeat))
            {
                return preferredSlot;
            }

            bool isLeftHand = (preferredSlot >= 0 && preferredSlot <= 3) || (preferredSlot >= 8 && preferredSlot <= 11);
            int[] sameHandPool = isLeftHand ? _leftAllSlots : _rightAllSlots;
            int[] otherHandPool = isLeftHand ? _rightAllSlots : _leftAllSlots;

            for (int i = 0; i < sameHandPool.Length; i++)
            {
                int candidate = sameHandPool[i];
                if (IsSlotUsable(candidate, avoidImmediateRepeat))
                {
                    return candidate;
                }
            }

            for (int i = 0; i < otherHandPool.Length; i++)
            {
                int candidate = otherHandPool[i];
                if (IsSlotUsable(candidate, avoidImmediateRepeat))
                {
                    return candidate;
                }
            }

            for (int i = 0; i < 16; i++)
            {
                if (IsSlotUsable(i, false))
                {
                    return i;
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

        private static void TriggerTapKey(KeyCode key, double now, double holdDuration)
        {
            if (key == KeyCode.None) return;

            if (_heldKeys.Contains(key))
            {
                ReleaseKeyImmediate(key);
            }

            _heldKeys.Add(key);
            _downFrames[key] = Time.frameCount;
            _releaseDeadlines[key] = now + holdDuration;
            SendOsKeyDown(key);
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
            if (!Application.isFocused) return;

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
