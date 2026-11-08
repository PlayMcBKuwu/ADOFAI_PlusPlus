using System;
using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ADOFAIPlusPlus
{
    public class IndicatorData
    {
        public Color OutlineColor;
        public string DisplayText;
    }

    public static class HitIndicatorStats
    {
        public static int NormalCount = 0;
        public static int LenientCount = 0;

        public static void Reset()
        {
            NormalCount = 0;
            LenientCount = 0;
            Patches.ClearColoredFloors();
        }
    }

    public static class Patches
    {
        public const string NativeHitTextToken = "<NATIVE_HIT_TEXT>";

        private static readonly FieldInfo TxtField = AccessTools.Field(typeof(scrShowIfDebug), "txt");
        private static readonly MethodInfo UpdateDifficultyUIMethod = AccessTools.Method(typeof(scrUIController), "UpdateDifficultyUI");
        private static readonly FieldInfo RDStringFontDataField = AccessTools.Field(typeof(RDString), "<fontData>k__BackingField");
        private static readonly FieldInfo F_ClsCurrentFolderName = AccessTools.Field(typeof(scnCLS), "currentFolderName");

        private static readonly Dictionary<scrFloor, Color> ColoredFloors = new Dictionary<scrFloor, Color>();

        public static void ClearColoredFloors()
        {
            ColoredFloors.Clear();
        }

        public static void ColorHitFloor(scrFloor floor, Color targetColor)
        {
            if (floor == null) return;
            ColoredFloors[floor] = targetColor;
            ApplyFloorColorInternal(floor, targetColor);
        }

        public static void ApplyFloorColorInternal(scrFloor floor, Color targetColor)
        {
            if (floor == null) return;
            try
            {
                floor.specialColorType = TrackColorType.Single;
                floor.specialColor1 = targetColor;
                floor.specialColor2 = targetColor;

                if (floor.floorRenderer != null)
                {
                    float alpha = floor.floorRenderer.color.a;
                    Color fc = new Color(targetColor.r, targetColor.g, targetColor.b, alpha);
                    floor.floorRenderer.color = fc;
                    floor.floorRenderer.deselectedColor = fc;
                }

                if (floor.topGlow != null)
                {
                    float topA = floor.topGlow.color.a;
                    if (topA <= 0.05f)
                    {
                        float baseGlow = floor.glowMultiplier > 0f ? floor.glowMultiplier : 1f;
                        float floorA = floor.floorRenderer != null ? floor.floorRenderer.color.a : 1f;
                        topA = Mathf.Min(baseGlow, floorA);
                    }
                    floor.topGlow.color = new Color(targetColor.r, targetColor.g, targetColor.b, topA);
                }

                if (floor.bottomGlow != null)
                {
                    float botA = floor.bottomGlow.color.a;
                    if (botA <= 0.05f)
                    {
                        botA = floor.topGlow != null ? floor.topGlow.color.a : 1f;
                    }
                    floor.bottomGlow.color = new Color(targetColor.r, targetColor.g, targetColor.b, botA);
                }
            }
            catch {}
        }

        public static bool IsCjkStart(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<')
                {
                    int close = s.IndexOf('>', i);
                    if (close > i)
                    {
                        i = close;
                        continue;
                    }
                }
                if (char.IsWhiteSpace(c) || c == '-' || c == '+')
                    continue;
                return c > 0x2E80;
            }
            return false;
        }

        /// <summary>
        /// 当前命中的指示器状态缓存（主线程单帧生命周期）
        /// </summary>
        public static IndicatorData CurrentIndicator;

        /// <summary>
        /// 本次关卡尝试中是否使用过移动判定或自定判定
        /// </summary>
        public static bool UsedMobileThisAttempt = false;
        public static bool UsedCustomThisAttempt = false;

        public static bool IsChinese()
        {
            return Loc.IsCJK();
        }

        public static bool IsInMainMenuOrLobby()
        {
            if (ADOBase.isLevelSelect || ADOBase.isCLS || ADOBase.isFreeroamScene)
                return true;
            if (scrController.instance != null && (!scrController.instance.gameworld || scrController.instance.isPuzzleRoom))
                return true;
            return false;
        }

        public static bool IsInPlayableLevel()
        {
            if (IsInMainMenuOrLobby())
                return false;
            return ADOBase.isScnGame || ADOBase.isLevelEditor || ADOBase.isOfficialLevel || ADOBase.isInternalLevel ||
                   (scrController.instance != null && scrController.instance.gameworld);
        }

        public static bool IsInLevelScene()
        {
            return IsInPlayableLevel();
        }

        private static bool _bypassDetermineDifficultyUIModePatch = false;

        public static float GetLevelBPM()
        {
            float baseBpm = 0f;
            if (scrConductor.instance != null && scrConductor.instance.bpm > 0)
            {
                baseBpm = (float)scrConductor.instance.bpm;
            }
            else if (ADOBase.conductor != null && ADOBase.conductor.bpm > 0)
            {
                baseBpm = (float)ADOBase.conductor.bpm;
            }
            else if (ADOBase.editor != null && ADOBase.editor.levelData != null && ADOBase.editor.levelData.bpm > 0f)
            {
                baseBpm = ADOBase.editor.levelData.bpm;
            }
            else if (ADOBase.customLevel != null && ADOBase.customLevel.levelData != null && ADOBase.customLevel.levelData.bpm > 0f)
            {
                baseBpm = ADOBase.customLevel.levelData.bpm;
            }

            float maxFloorSpeed = 0f;
            scrLevelMaker lm = scrLevelMaker.instance != null ? scrLevelMaker.instance : ADOBase.lm;
            if (lm != null && lm.listFloors != null && lm.listFloors.Count > 0)
            {
                for (int i = 0; i < lm.listFloors.Count; i++)
                {
                    scrFloor f = lm.listFloors[i];
                    if (f != null && f.speed > maxFloorSpeed)
                    {
                        maxFloorSpeed = (float)f.speed;
                    }
                }
            }

            float bpm = 0f;
            if (baseBpm > 0f && maxFloorSpeed > 0f)
            {
                bpm = baseBpm * maxFloorSpeed;
            }
            else if (lm != null && lm.highestBPM > 0f)
            {
                bpm = lm.highestBPM;
            }
            else if (ADOBase.customLevel != null && ADOBase.customLevel.highestBPM > 0f)
            {
                bpm = ADOBase.customLevel.highestBPM;
            }
            else if (ADOBase.editor != null && ADOBase.editor.customLevel != null && ADOBase.editor.customLevel.highestBPM > 0f)
            {
                bpm = ADOBase.editor.customLevel.highestBPM;
            }
            else if (baseBpm > 0f)
            {
                bpm = baseBpm;
            }

            float pitch = 1f;
            if (scrConductor.instance != null && scrConductor.instance.song != null && scrConductor.instance.song.pitch > 0f)
            {
                pitch = scrConductor.instance.song.pitch;
            }
            else if (GCS.currentSpeedTrial > 0f)
            {
                pitch = GCS.currentSpeedTrial;
            }

            if (bpm > 0f && pitch > 0f)
            {
                bpm *= pitch;
            }

            return bpm;
        }

        public static bool ShouldHideDifficultyAdvice()
        {
            // 仅在由于开启了移动判定、自定判定或强制开启判定选择，使得判定菜单被迫展开常驻时：
            // 若此时最高 BPM < 200，才隐藏「关卡速度过快」提示。
            if (Main.Settings == null || !Main.Settings.MasterEnabled)
                return false;

            if (Main.Settings.Mode == JudgmentMode.Disabled && !Main.Settings.ForceDifficultySelector)
            {
                // 原版 PC 模式且未强制开启判定栏时，完全不干涉，保留原版自然行为
                return false;
            }

            if (!IsInPlayableLevel())
                return false;

            float bpm = GetLevelBPM();

            // 若尚未计算出 BPM（例如场景刚初始化时），不提前盲目隐藏，等待 BPM 就绪
            if (bpm <= 0f)
            {
                return false;
            }

            // 若最高 BPM >= 200，说明确实是高速关卡，达标！绝不隐藏！
            if (bpm >= 200f)
            {
                return false;
            }

            // BPM 明确计算出且不达标（< 200 BPM 的慢速/常速关卡），隐藏速度过快提示
            return true;
        }

        public static float GetCurrentBpm()
        {
            if (scrConductor.instance != null && scrConductor.instance.bpm > 0)
                return (float)scrConductor.instance.bpm;
            if (ADOBase.conductor != null && ADOBase.conductor.bpm > 0)
                return (float)ADOBase.conductor.bpm;
            return 100f;
        }

        public static Difficulty GetCustomRepresentativeDifficulty()
        {
            if (Main.Settings.CustomUnit == CustomUnitType.Time)
            {
                float counted = Main.Settings.CustomCountedTime;
                if (counted <= 52.5f) return Difficulty.Strict;
                if (counted <= 77.5f) return Difficulty.Normal;
                return Difficulty.Lenient;
            }
            else
            {
                float angle = Main.Settings.CustomCountedAngle;
                if (angle <= 54f) return Difficulty.Strict;
                if (angle <= 72f) return Difficulty.Normal;
                return Difficulty.Lenient;
            }
        }

        public static string GetHardcodedTimingText(bool isEarly, bool isDouble)
        {
            SystemLanguage lang = Loc.CurrentLanguage;
            if (lang == SystemLanguage.Chinese || lang == SystemLanguage.ChineseSimplified || lang == SystemLanguage.ChineseTraditional)
            {
                if (isEarly) return isDouble ? "太快！！" : "太快！";
                return isDouble ? "太慢！！" : "太慢！";
            }
            if (lang == SystemLanguage.Korean)
            {
                if (isEarly) return isDouble ? "빠름!!" : "빠름!";
                return isDouble ? "느림!!" : "느림!";
            }
            if (lang == SystemLanguage.Japanese)
            {
                if (Main.Settings != null && Main.Settings.BetterJapaneseLocalization)
                {
                    if (isEarly) return isDouble ? "早すぎ！！" : "早すぎ！";
                    return isDouble ? "遅すぎ！！" : "遅すぎ！";
                }
                if (isEarly) return isDouble ? "Too Early!!" : "Early!";
                return isDouble ? "Late!!" : "Late!";
            }
            if (lang == SystemLanguage.English)
            {
                if (isEarly) return isDouble ? "Too Early!!" : "Early!";
                return isDouble ? "Late!!" : "Late!";
            }

            string word = Loc.Get(isEarly ? "indicator.tooEarly" : "indicator.tooLate");
            string excl = isDouble ? Loc.GetDoubleExcl() : Loc.GetSingleExcl();
            return word + excl;
        }

        public static string GetIndicatorDisplayText(bool isLenient, bool isEarly, bool isDouble, int rawMs)
        {
            HitMarginTextPreset preset = Persistence.hitMarginText;
            string suffix = isLenient ? "L" : "N";

            // 1. 毫秒判定模式 (MsDifference / MsDifferenceColorSign)
            if (preset == HitMarginTextPreset.MsDifference || preset == HitMarginTextPreset.MsDifferenceColorSign)
            {
                string sign = (rawMs < 0 || (rawMs == 0 && isEarly)) ? "-" : "+";
                if (isDouble) sign = sign + sign; // -- 或 ++
                int absMs = Math.Abs(rawMs);
                string excl = isDouble ? Loc.GetDoubleExcl() : string.Empty;
                return string.Format("{0}{1}ms{2}{3}", sign, absMs, suffix, excl);
            }

            // 2. 精简判定模式 (Shortened)
            if (preset == HitMarginTextPreset.Shortened)
            {
                if (Main.Settings.EnableHitIndicatorPlus && Main.Settings.EnableAlternativeIndicatorPlus)
                {
                    if (Main.Settings.AltStyle == AlternativeIndicatorStyle.SignPrefix)
                    {
                        string sign = isEarly ? "-" : "+";
                        return (isLenient ? "LEN" : "NOR") + (isDouble ? "!!" : string.Empty) + sign;
                    }
                    else
                    {
                        string dir = isEarly ? (isDouble ? "TE" : "E") : (isDouble ? "TL" : "L");
                        return suffix + dir;
                    }
                }
                if (Main.Settings.EnableHitIndicatorPlus)
                {
                    return (isLenient ? "LEN" : "NOR") + (isDouble ? "!!" : string.Empty);
                }
                return null;
            }

            // 3. 默认文字模式 (Default / EarlyOrLate 等)
            if (!Main.Settings.EnableHitIndicatorPlus)
            {
                return null;
            }

            string exclText = isDouble ? Loc.GetDoubleExcl() : Loc.GetSingleExcl();

            if (Main.Settings.EnableAlternativeIndicatorPlus)
            {
                if (Main.Settings.AltStyle == AlternativeIndicatorStyle.SignPrefix)
                {
                    // 另类指示器+（符号后缀）：标判！+ 和 宽判！！+ (太慢 +) / 标判！- 和 宽判！！- (太快 -)
                    string sign = isEarly ? "-" : "+";
                    string word = isLenient ? Loc.Get("indicator.lenient") : Loc.Get("indicator.normal");
                    return word + exclText + sign;
                }
                else
                {
                    // 另类指示器+（字符前缀）：完全硬编码对应语言词汇 (如 L-太快！/ N-太慢！/ L-Early! / N-Late!)
                    string timingText = GetHardcodedTimingText(isEarly, isDouble);
                    return suffix + "-" + timingText;
                }
            }

            // 标准判定指示器+：纯正硬编码文字「宽判！」/「标判！」(双感叹号时: 宽判！！/ 标判！！)
            string baseWord = isLenient ? Loc.Get("indicator.lenient") : Loc.Get("indicator.normal");
            return baseWord + exclText;
        }

        public static void SetDifficultyArrowsActive(scrUIController ui, bool showArrows)
        {
            if (ui == null) return;
            if (ui.leftArrow != null)
                ui.leftArrow.gameObject.SetActive(showArrows);
            if (ui.rightArrow != null)
                ui.rightArrow.gameObject.SetActive(showArrows);
            if (ui.difficultyButtonLeft != null)
            {
                ui.difficultyButtonLeft.enabled = showArrows;
                ui.difficultyButtonLeft.interactable = showArrows;
                ui.difficultyButtonLeft.gameObject.SetActive(showArrows);
            }
            if (ui.difficultyButtonRight != null)
            {
                ui.difficultyButtonRight.enabled = showArrows;
                ui.difficultyButtonRight.interactable = showArrows;
                ui.difficultyButtonRight.gameObject.SetActive(showArrows);
            }
        }

        public static void UpdateDifficultyArrows(bool showArrows)
        {
            if (scrUIController.instance != null)
            {
                if (IsInPlayableLevel() && scrUIController.instance.difficultyContainer != null && scrUIController.instance.difficultyContainer.gameObject.activeSelf)
                {
                    SetDifficultyArrowsActive(scrUIController.instance, showArrows);
                }
            }

            EditorDifficultySelector editorSelector = UnityEngine.Object.FindObjectOfType<EditorDifficultySelector>();
            if (editorSelector != null)
            {
                if (editorSelector.buttonChangeDifficulty != null)
                    editorSelector.buttonChangeDifficulty.interactable = showArrows;
                if (editorSelector.upArrowImage != null)
                    editorSelector.upArrowImage.gameObject.SetActive(showArrows);
                if (editorSelector.downArrowImage != null)
                    editorSelector.downArrowImage.gameObject.SetActive(showArrows);
            }
        }

        public static void ApplyModeChange()
        {
            if (!Main.Settings.MasterEnabled)
            {
                RestoreOriginalState();
                return;
            }

            if (!IsInPlayableLevel())
            {
                // 非关卡内（主界面、选曲菜单等）：只保留左上角水印，不修改判定，绝对保证判定菜单处于隐藏关闭状态
                if (scrUIController.instance != null && scrUIController.instance.difficultyContainer != null)
                {
                    scrUIController.instance.difficultyContainer.gameObject.SetActive(false);
                    scrUIController.instance.difficultyUIMode = DifficultyUIMode.DontShow;
                }
                UpdateHUDText();
                return;
            }

            if (Main.Settings.Mode == JudgmentMode.Disabled)
            {
                RestoreOriginalState();

                if (scrUIController.instance != null && scrUIController.instance.difficultyContainer != null)
                {
                    if (Main.Settings.ForceDifficultySelector)
                    {
                        bool isNotPlaying = (scrController.instance == null || scrController.instance.state != States.PlayerControl);
                        if (!scrUIController.instance.difficultyContainer.gameObject.activeSelf && isNotPlaying)
                        {
                            scrUIController.instance.ShowDifficultyContainer(DifficultyUIMode.ShowAll);
                        }
                        SetDifficultyArrowsActive(scrUIController.instance, true);
                        HideSpeedWarningIfSubstandard();
                    }
                    else
                    {
                        // 实时隐藏判定菜单：当在关卡内切换回 PC 模式且未强制常驻时，若原版判定为 DontShow 则即刻隐藏
                        float bpm = GetLevelBPM();
                        if (bpm <= 0f) bpm = GetCurrentBpm();

                        DifficultyUIMode uiMode;
                        _bypassDetermineDifficultyUIModePatch = true;
                        try
                        {
                            uiMode = scrMisc.DetermineDifficultyUIMode(bpm);
                        }
                        finally
                        {
                            _bypassDetermineDifficultyUIModePatch = false;
                        }

                        if (uiMode == DifficultyUIMode.DontShow)
                        {
                            scrUIController.instance.difficultyContainer.gameObject.SetActive(false);
                            scrUIController.instance.difficultyUIMode = DifficultyUIMode.DontShow;
                        }
                        else
                        {
                            scrUIController.instance.ShowDifficultyContainer(uiMode);
                            SetDifficultyArrowsActive(scrUIController.instance, true);
                        }
                    }
                }
            }
            else
            {
                // 标记本次通关尝试使用过特殊模式（仅在可游玩关卡内标记）
                if (Main.Settings.Mode == JudgmentMode.Mobile) UsedMobileThisAttempt = true;
                if (Main.Settings.Mode == JudgmentMode.Custom) UsedCustomThisAttempt = true;

                Difficulty targetDiff = (Main.Settings.Mode == JudgmentMode.Mobile)
                    ? Difficulty.Lenient
                    : GetCustomRepresentativeDifficulty();

                GCS.difficulty = targetDiff;

                if (scrUIController.instance != null)
                {
                    bool isNotPlaying = (scrController.instance == null || scrController.instance.state != States.PlayerControl);
                    if (scrUIController.instance.difficultyContainer != null &&
                        !scrUIController.instance.difficultyContainer.gameObject.activeSelf &&
                        isNotPlaying)
                    {
                        scrUIController.instance.ShowDifficultyContainer(DifficultyUIMode.ShowAll);
                    }

                    if (UpdateDifficultyUIMethod != null)
                    {
                        UpdateDifficultyUIMethod.Invoke(scrUIController.instance, new object[] { targetDiff });
                    }

                    if (scrUIController.instance.difficultyText != null)
                    {
                        scrUIController.instance.difficultyText.text = (Main.Settings.Mode == JudgmentMode.Mobile)
                            ? Loc.Get("diff.mobile")
                            : Loc.Get("diff.custom");
                    }

                    SetDifficultyArrowsActive(scrUIController.instance, false);
                }

                EditorDifficultySelector editorSelector = UnityEngine.Object.FindObjectOfType<EditorDifficultySelector>();
                if (editorSelector != null)
                {
                    editorSelector.SendMessage("UpdateDifficultyDisplay", SendMessageOptions.DontRequireReceiver);
                }

                UpdateDifficultyArrows(false);
                UpdateHUDText();
                HideSpeedWarningIfSubstandard();
            }
        }

        public static void RestoreOriginalState()
        {
            GCS.difficulty = Persistence.GetDefaultDifficulty();

            if (scrUIController.instance != null)
            {
                if (UpdateDifficultyUIMethod != null)
                {
                    UpdateDifficultyUIMethod.Invoke(scrUIController.instance, new object[] { GCS.difficulty });
                }
            }

            if (!IsInLevelScene())
            {
                if (scrUIController.instance != null && scrUIController.instance.difficultyContainer != null)
                {
                    scrUIController.instance.difficultyContainer.gameObject.SetActive(false);
                    scrUIController.instance.difficultyUIMode = DifficultyUIMode.DontShow;
                }
                UpdateHUDText();
                return;
            }

            UpdateDifficultyArrows(true);

            EditorDifficultySelector editorSelector = UnityEngine.Object.FindObjectOfType<EditorDifficultySelector>();
            if (editorSelector != null)
            {
                editorSelector.SendMessage("UpdateDifficultyDisplay", SendMessageOptions.DontRequireReceiver);
            }

            UpdateHUDText();
        }

        public static void ApplyNoFail(bool enabled)
        {
            GCS.useNoFail = enabled;
            if (scrController.instance != null)
            {
                scrController.instance.noFail = enabled;
                scrController.instance.freeroamInvulnerability = enabled;
            }
            if (scrUIController.instance != null)
            {
                if (scrUIController.instance.modifiersContainer != null && enabled && !scrUIController.instance.modifiersContainer.gameObject.activeSelf)
                {
                    scrUIController.instance.modifiersContainer.gameObject.SetActive(true);
                }
                if (scrUIController.instance.noFailImage != null)
                {
                    scrUIController.instance.noFailImage.gameObject.SetActive(enabled);
                }
            }
        }

        public static void ApplyAutoplay(bool enabled)
        {
            bool effectiveAuto = ((enabled && Main.Settings != null && Main.Settings.MasterEnabled) ||
                                 (Main.Settings != null && Main.Settings.MasterEnabled && Main.Settings.AutoplayPlus)) &&
                                 IsInPlayableLevel();
            RDC.auto = effectiveAuto;

            if (!effectiveAuto && ADOBase.isLevelEditor && ADOBase.editor != null)
            {
                ADOBase.editor.autoFailed = false;
            }
            UpdateHUDText();
        }

        public static void ApplyAutoplayPlus(bool enabled)
        {
            AutoplayBot.Reset();
            bool effectiveAuto = ((enabled && Main.Settings != null && Main.Settings.MasterEnabled) ||
                                 (Main.Settings != null && Main.Settings.MasterEnabled && Main.Settings.Autoplay)) &&
                                 IsInPlayableLevel();
            RDC.auto = effectiveAuto;

            if (!effectiveAuto && ADOBase.isLevelEditor && ADOBase.editor != null)
            {
                ADOBase.editor.autoFailed = false;
            }
            UpdateHUDText();
        }

        public static string GetAutoplayPlusText()
        {
            try
            {
                string baseAuto = RDString.Get("status.autoplay", null);
                if (!string.IsNullOrEmpty(baseAuto))
                {
                    return baseAuto + "+";
                }
            }
            catch {}
            return Loc.Get("hud.autoplay_plus");
        }

        public static void ApplyJapaneseFontPreference()
        {
            try
            {
                if (RDConstants.data == null)
                    return;
                if (Loc.CurrentLanguage != SystemLanguage.Japanese)
                    return;

                FontData fd = RDString.GetFontDataForLanguage(SystemLanguage.Japanese);
                if (RDStringFontDataField != null)
                {
                    RDStringFontDataField.SetValue(null, fd);
                }

                if (fd.font != null)
                {
                    Text[] texts = UnityEngine.Object.FindObjectsOfType<Text>(true);
                    if (texts != null)
                    {
                        for (int i = 0; i < texts.Length; i++)
                        {
                            if (texts[i] != null)
                            {
                                texts[i].font = fd.font;
                            }
                        }
                    }
                }

                if (fd.fontTMP != null)
                {
                    Material borderMatAsset = (Main.Settings != null && Main.Settings.BetterJapaneseLocalization && Main.Settings.JapaneseUseChineseFont)
                        ? RDConstants.data.chineseFontTMProXPerfectBorderMaterial
                        : RDConstants.data.japaneseFontTMProXPerfectBorderMaterial;

                    scrHitTextMesh[] hitMeshes = UnityEngine.Object.FindObjectsOfType<scrHitTextMesh>(true);
                    if (hitMeshes != null)
                    {
                        for (int i = 0; i < hitMeshes.Length; i++)
                        {
                            scrHitTextMesh m = hitMeshes[i];
                            if (m != null)
                            {
                                if (m.text != null)
                                {
                                    m.text.font = fd.fontTMP;
                                    if (fd.fontTMP.material != null)
                                    {
                                        m.text.fontSharedMaterial = fd.fontTMP.material;
                                    }
                                }
                                if (m.xPerfectBorder != null)
                                {
                                    m.xPerfectBorder.font = fd.fontTMP;
                                    if (borderMatAsset != null)
                                    {
                                        m.xPerfectBorder.fontMaterial = borderMatAsset;
                                    }
                                    else if (fd.fontTMP.material != null)
                                    {
                                        m.xPerfectBorder.fontSharedMaterial = fd.fontTMP.material;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (Main.Logger != null) Main.Logger.Error("ApplyJapaneseFontPreference failed: " + ex);
            }
        }

        public static void ApplyHidePlanetRing(bool hide)
        {
            scrRing[] rings = UnityEngine.Object.FindObjectsOfType<scrRing>();
            if (rings != null)
            {
                for (int i = 0; i < rings.Length; i++)
                {
                    if (rings[i] != null && rings[i].line != null)
                    {
                        rings[i].line.enabled = !hide;
                    }
                }
            }
        }

        public static void UpdateHUDText()
        {
            try
            {
                scrShowIfDebug[] debugs = UnityEngine.Object.FindObjectsOfType<scrShowIfDebug>();
                if (debugs == null) return;
                for (int i = 0; i < debugs.Length; i++)
                {
                    scrShowIfDebug debug = debugs[i];
                    if (debug == null) continue;
                    Text txt = (Text)TxtField.GetValue(debug);
                    if (txt == null) continue;

                    if (Main.Enabled && Main.Settings != null && Main.Settings.MasterEnabled && Main.Settings.AutoplayPlus)
                    {
                        txt.enabled = true;
                        txt.text = GetAutoplayPlusText();
                        if (ADOBase.editor != null && ADOBase.editor.pausedInPlayMode)
                        {
                            txt.text = txt.text + " + " + RDString.Get("status.paused", null);
                        }
                    }
                    else if (Main.Settings.Autoplay || RDC.auto)
                    {
                        txt.enabled = true;
                        txt.text = RDString.Get("status.autoplay", null);
                        if (ADOBase.editor != null && ADOBase.editor.pausedInPlayMode)
                        {
                            txt.text = txt.text + " + " + RDString.Get("status.paused", null);
                        }
                    }
                    else if (Main.Enabled && Main.Settings.Mode != JudgmentMode.Disabled)
                    {
                        if (!RDC.noHud && !RDC.noAutoHud)
                        {
                            txt.enabled = true;
                            txt.text = (Main.Settings.Mode == JudgmentMode.Mobile)
                                ? Loc.Get("hud.mobile")
                                : Loc.Get("hud.custom");
                            if (ADOBase.editor != null && ADOBase.editor.pausedInPlayMode)
                            {
                                txt.text = txt.text + " + " + RDString.Get("status.paused", null);
                            }
                        }
                    }
                    else
                    {
                        if (!RDC.debug)
                        {
                            txt.enabled = false;
                            txt.text = string.Empty;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (Main.Logger != null) Main.Logger.Error("UpdateHUDText failed: " + ex);
            }
        }

        public static void HideSpeedWarningIfSubstandard()
        {
            try
            {
                bool hide = ShouldHideDifficultyAdvice();

                // 1. 处理 RDStringToUIText 组件（游戏原版 difficultyFadeContainer 内「关卡速度过快」文本的实际组件）
                List<RDStringToUIText> rdTexts = new List<RDStringToUIText>();
                if (scrUIController.instance != null)
                {
                    RDStringToUIText[] uiRdTexts = scrUIController.instance.GetComponentsInChildren<RDStringToUIText>(true);
                    if (uiRdTexts != null)
                    {
                        rdTexts.AddRange(uiRdTexts);
                    }
                }
                RDStringToUIText[] allRdTexts = UnityEngine.Object.FindObjectsOfType<RDStringToUIText>(true);
                if (allRdTexts != null)
                {
                    for (int j = 0; j < allRdTexts.Length; j++)
                    {
                        if (allRdTexts[j] != null && !rdTexts.Contains(allRdTexts[j]))
                        {
                            rdTexts.Add(allRdTexts[j]);
                        }
                    }
                }

                for (int i = 0; i < rdTexts.Count; i++)
                {
                    RDStringToUIText rt = rdTexts[i];
                    if (rt != null && rt.key == "status.difficultyAdvice")
                    {
                        Text uiTxt = rt.GetComponent<Text>();
                        if (uiTxt != null)
                        {
                            if (!hide && string.IsNullOrEmpty(uiTxt.text))
                            {
                                uiTxt.text = RDString.Get(rt.key, null);
                            }
                            if (uiTxt.enabled == hide)
                            {
                                uiTxt.enabled = !hide;
                            }
                        }
                        if (rt.gameObject.activeSelf == hide)
                        {
                            rt.gameObject.SetActive(!hide);
                        }
                    }
                }

                // 2. 兼容处理 scrTextWithLink 组件
                List<scrTextWithLink> links = new List<scrTextWithLink>();
                if (scrUIController.instance != null)
                {
                    scrTextWithLink[] uiLinks = scrUIController.instance.GetComponentsInChildren<scrTextWithLink>(true);
                    if (uiLinks != null)
                    {
                        links.AddRange(uiLinks);
                    }
                }
                scrTextWithLink[] allLinks = UnityEngine.Object.FindObjectsOfType<scrTextWithLink>(true);
                if (allLinks != null)
                {
                    for (int j = 0; j < allLinks.Length; j++)
                    {
                        if (allLinks[j] != null && !links.Contains(allLinks[j]))
                        {
                            links.Add(allLinks[j]);
                        }
                    }
                }

                for (int i = 0; i < links.Count; i++)
                {
                    scrTextWithLink link = links[i];
                    if (link != null && link.localizationKey == "status.difficultyAdvice")
                    {
                        if (link.text != null)
                        {
                            if (!hide && string.IsNullOrEmpty(link.text.text))
                            {
                                link.text.text = RDString.Get(link.localizationKey, null);
                            }
                            if (link.text.enabled == hide)
                            {
                                link.text.enabled = !hide;
                            }
                        }

                        if (link.gameObject.activeSelf == hide)
                        {
                            link.gameObject.SetActive(!hide);
                        }
                    }
                }

                // 3. 兜底扫描 difficultyFadeContainer / difficultyContainer 下非难度名称本身的提示 Text
                if (scrUIController.instance != null)
                {
                    Transform container = scrUIController.instance.difficultyFadeContainer != null
                        ? scrUIController.instance.difficultyFadeContainer.transform
                        : (scrUIController.instance.difficultyContainer != null ? scrUIController.instance.difficultyContainer.transform : null);

                    if (container != null)
                    {
                        string adviceStr = RDString.Get("status.difficultyAdvice", null);
                        Text[] childTexts = container.GetComponentsInChildren<Text>(true);
                        if (childTexts != null)
                        {
                            for (int i = 0; i < childTexts.Length; i++)
                            {
                                Text t = childTexts[i];
                                if (t == null || t == scrUIController.instance.difficultyText)
                                    continue;

                                RDStringToUIText rt = t.GetComponent<RDStringToUIText>();
                                bool isAdvice = (rt != null && rt.key == "status.difficultyAdvice") ||
                                                (!string.IsNullOrEmpty(adviceStr) && t.text == adviceStr);
                                if (isAdvice)
                                {
                                    if (!hide && string.IsNullOrEmpty(t.text) && !string.IsNullOrEmpty(adviceStr))
                                    {
                                        t.text = adviceStr;
                                    }
                                    if (t.enabled == hide)
                                    {
                                        t.enabled = !hide;
                                    }
                                    if (t.gameObject.activeSelf == hide)
                                    {
                                        t.gameObject.SetActive(!hide);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (Main.Logger != null) Main.Logger.Error("HideSpeedWarningIfSubstandard failed: " + ex);
            }
        }

        /// <summary>
        /// 1. 核心判定下限替换：提供移动端/自定义时间下限（主界面与选曲大厅不修改判定）
        /// </summary>
        [HarmonyPatch(typeof(scrMisc), "GetMinimumTimes")]
        public static class Patch_GetMinimumTimes
        {
            public static bool Prefix(Difficulty difficulty, ref scrMisc.HitMarginGeneralValues<double> __result)
            {
                if (!Main.Enabled || Main.Settings.Mode == JudgmentMode.Disabled || !IsInPlayableLevel())
                {
                    return true;
                }

                double counted;
                double perfect;
                double pure;

                if (Main.Settings.Mode == JudgmentMode.Mobile)
                {
                    counted = 0.09;
                    perfect = 0.07;
                    pure = 0.05;
                }
                else
                {
                    if (Main.Settings.CustomUnit == CustomUnitType.Angle)
                    {
                        // 角度模式下由角度边界驱动，最小时间提供极小安全阈值 (25ms)
                        counted = 0.025;
                        perfect = 0.025;
                        pure = 0.025;
                    }
                    else
                    {
                        counted = (double)Main.Settings.CustomCountedTime / 1000.0;
                        perfect = (double)Main.Settings.CustomPerfectTime / 1000.0;
                        pure = (double)Main.Settings.CustomPureTime / 1000.0;

                        if (Main.Settings.CustomScaleWithSpeedTrial && GCS.currentSpeedTrial > 0f)
                        {
                            counted /= (double)GCS.currentSpeedTrial;
                            perfect /= (double)GCS.currentSpeedTrial;
                            pure /= (double)GCS.currentSpeedTrial;
                        }
                    }
                }

                counted = Math.Max(counted, 0.025);
                perfect = Math.Max(perfect, 0.025);
                pure = Math.Max(pure, 0.025);

                __result = new scrMisc.HitMarginGeneralValues<double>
                {
                    Counted = counted,
                    Perfect = perfect,
                    Pure = pure
                };

                return false;
            }
        }

        /// <summary>
        /// 自定义角度模式：拦截并直接依据角度计算时间边界
        /// 原生签名：GetAdjustedTimeBoundaries(Difficulty difficulty, double bpmTimesSpeed, float conductorPitch, double marginMult)
        /// </summary>
        [HarmonyPatch(typeof(scrMisc), "GetAdjustedTimeBoundaries")]
        public static class Patch_GetAdjustedTimeBoundaries
        {
            public static bool Prefix(Difficulty difficulty, double bpmTimesSpeed, float conductorPitch, double marginMult, ref scrMisc.HitMarginGeneralWithXPerfectValues<double> __result)
            {
                if (Main.Enabled && Main.Settings.Mode == JudgmentMode.Custom && Main.Settings.CustomUnit == CustomUnitType.Angle && IsInPlayableLevel())
                {
                    double pitch = conductorPitch > 0f ? (double)conductorPitch : 1.0;
                    double degToRad = Math.PI / 180.0;
                    double countedRad = (double)Main.Settings.CustomCountedAngle * marginMult * degToRad;
                    double perfectRad = (double)Main.Settings.CustomPerfectAngle * marginMult * degToRad;
                    double pureRad = (double)Main.Settings.CustomPureAngle * marginMult * degToRad;
                    double xPerfectRad = pureRad * (12.5 / 30.0);

                    double countedTime = scrMisc.AngleToTime(countedRad, bpmTimesSpeed) / pitch;
                    double perfectTime = scrMisc.AngleToTime(perfectRad, bpmTimesSpeed) / pitch;
                    double pureTime = scrMisc.AngleToTime(pureRad, bpmTimesSpeed) / pitch;
                    double xPerfectTime = scrMisc.AngleToTime(xPerfectRad, bpmTimesSpeed) / pitch;

                    __result = new scrMisc.HitMarginGeneralWithXPerfectValues<double>
                    {
                        Counted = countedTime,
                        Perfect = perfectTime,
                        Pure = pureTime,
                        XPerfect = xPerfectTime
                    };
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// 自定义角度模式：拦截角度边界计算
        /// 原生签名：GetAdjustedAngleBoundaryInDeg(Difficulty difficulty, double bpmTimesSpeed, double conductorPitch, double marginMult)
        /// </summary>
        [HarmonyPatch(typeof(scrMisc), "GetAdjustedAngleBoundaryInDeg")]
        public static class Patch_GetAdjustedAngleBoundaryInDeg
        {
            public static bool Prefix(Difficulty difficulty, double bpmTimesSpeed, double conductorPitch, double marginMult, ref scrMisc.HitMarginGeneralWithXPerfectValues<double> __result)
            {
                if (Main.Enabled && Main.Settings.Mode == JudgmentMode.Custom && Main.Settings.CustomUnit == CustomUnitType.Angle && IsInPlayableLevel())
                {
                    double countedAngle = (double)Main.Settings.CustomCountedAngle * marginMult;
                    double perfectAngle = (double)Main.Settings.CustomPerfectAngle * marginMult;
                    double pureAngle = (double)Main.Settings.CustomPureAngle * marginMult;
                    double xPerfectAngle = pureAngle * (12.5 / 30.0);

                    __result = new scrMisc.HitMarginGeneralWithXPerfectValues<double>
                    {
                        Counted = countedAngle,
                        Perfect = perfectAngle,
                        Pure = pureAngle,
                        XPerfect = xPerfectAngle
                    };
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// 2. 左上角自动播放指示器：自动播放+ 强制显示水印，非自动播放时替换为「移动判定」/「自定判定」
        /// </summary>
        [HarmonyPatch(typeof(scrShowIfDebug), "Update")]
        public static class Patch_ScrShowIfDebug_Update
        {
            public static void Postfix(scrShowIfDebug __instance)
            {
                if (!Main.Enabled || Main.Settings == null || !Main.Settings.MasterEnabled)
                    return;

                if (Main.Settings.AutoplayPlus)
                {
                    Text autoPlusTxt = (Text)TxtField.GetValue(__instance);
                    if (autoPlusTxt != null)
                    {
                        autoPlusTxt.enabled = true;
                        autoPlusTxt.text = GetAutoplayPlusText();
                        if (ADOBase.editor != null && ADOBase.editor.pausedInPlayMode)
                        {
                            autoPlusTxt.text = autoPlusTxt.text + " + " + RDString.Get("status.paused", null);
                        }
                    }
                    return;
                }

                if (Main.Settings.Mode == JudgmentMode.Disabled)
                    return;

                if (RDC.auto)
                    return;

                if (RDC.noHud || RDC.noAutoHud)
                    return;

                Text txt = (Text)TxtField.GetValue(__instance);
                if (txt == null)
                    return;

                txt.enabled = true;
                txt.text = (Main.Settings.Mode == JudgmentMode.Mobile)
                    ? Loc.Get("hud.mobile")
                    : Loc.Get("hud.custom");

                if (ADOBase.editor != null && ADOBase.editor.pausedInPlayMode)
                {
                    txt.text = txt.text + " + " + RDString.Get("status.paused", null);
                }
            }
        }

        /// <summary>
        /// 3. 所有关卡无论 BPM 均强制常驻显示判定栏（仅限可游玩关卡内）
        /// </summary>
        [HarmonyPatch(typeof(scrMisc), "DetermineDifficultyUIMode")]
        public static class Patch_DetermineDifficultyUIMode
        {
            public static void Postfix(ref DifficultyUIMode __result)
            {
                if (_bypassDetermineDifficultyUIModePatch)
                    return;

                if (Main.Enabled && IsInPlayableLevel())
                {
                    if (Main.Settings.Mode != JudgmentMode.Disabled || Main.Settings.ForceDifficultySelector)
                    {
                        __result = DifficultyUIMode.ShowAll;
                    }
                }
            }
        }

        /// <summary>
        /// 4. 局内判定 UI 弹出时强制显示并处理切换按钮
        /// </summary>
        [HarmonyPatch(typeof(scrUIController), "ShowDifficultyContainer")]
        public static class Patch_ShowDifficultyContainer
        {
            public static void Prefix(ref DifficultyUIMode mode)
            {
                if (!Main.Enabled)
                    return;

                // 如果不在可游玩关卡内，绝不强行弹出判定选择栏！
                if (!IsInPlayableLevel())
                    return;

                if (Main.Settings.Mode != JudgmentMode.Disabled || Main.Settings.ForceDifficultySelector)
                {
                    mode = DifficultyUIMode.ShowAll;
                }
            }

            public static void Postfix(scrUIController __instance)
            {
                if (!Main.Enabled)
                    return;

                if (!IsInPlayableLevel())
                {
                    if (__instance.difficultyContainer != null)
                    {
                        __instance.difficultyContainer.gameObject.SetActive(false);
                        __instance.difficultyUIMode = DifficultyUIMode.DontShow;
                    }
                    return;
                }

                if (Main.Settings.NoFail)
                {
                    GCS.useNoFail = true;
                    if (__instance.modifiersContainer != null && !__instance.modifiersContainer.gameObject.activeSelf)
                    {
                        __instance.modifiersContainer.gameObject.SetActive(true);
                    }
                    if (__instance.noFailImage != null && !__instance.noFailImage.gameObject.activeSelf)
                    {
                        __instance.noFailImage.gameObject.SetActive(true);
                    }
                }

                if (Main.Settings.Mode != JudgmentMode.Disabled)
                {
                    SetDifficultyArrowsActive(__instance, false);

                    Difficulty targetDiff = (Main.Settings.Mode == JudgmentMode.Mobile)
                        ? Difficulty.Lenient
                        : GetCustomRepresentativeDifficulty();

                    GCS.difficulty = targetDiff;
                    if (__instance.difficultyText != null)
                    {
                        __instance.difficultyText.text = (Main.Settings.Mode == JudgmentMode.Mobile)
                            ? Loc.Get("diff.mobile")
                            : Loc.Get("diff.custom");
                    }
                }
                else
                {
                    SetDifficultyArrowsActive(__instance, true);
                }

                HideSpeedWarningIfSubstandard();
            }
        }

        [HarmonyPatch(typeof(RDStringToUIText), "Start")]
        public static class Patch_RDStringToUIText_Start
        {
            public static void Postfix(RDStringToUIText __instance)
            {
                if (!Main.Enabled)
                    return;

                if (__instance != null && __instance.key == "status.difficultyAdvice")
                {
                    HideSpeedWarningIfSubstandard();
                }
            }
        }

        [HarmonyPatch(typeof(scrTextWithLink), "Start")]
        public static class Patch_ScrTextWithLink_Start
        {
            public static void Postfix(scrTextWithLink __instance)
            {
                if (!Main.Enabled)
                    return;

                if (__instance != null && __instance.localizationKey == "status.difficultyAdvice")
                {
                    HideSpeedWarningIfSubstandard();
                }
            }
        }

        /// <summary>
        /// 4b. 关卡计算完每格 BPM EntryTimes 之后，即刻依据最终真实最高 BPM 刷新提示显示状态
        /// </summary>
        [HarmonyPatch(typeof(scrLevelMaker), "CalculateFloorEntryTimes")]
        public static class Patch_ScrLevelMaker_CalculateFloorEntryTimes
        {
            public static void Postfix()
            {
                if (!Main.Enabled)
                    return;

                HideSpeedWarningIfSubstandard();
            }
        }

        /// <summary>
        /// 5. 局内判定 UI 刷新时重定向难度图标与文本
        /// </summary>
        [HarmonyPatch(typeof(scrUIController), "UpdateDifficultyUI")]
        public static class Patch_UpdateDifficultyUI
        {
            public static void Prefix(ref Difficulty difficulty)
            {
                if (Main.Enabled && Main.Settings.Mode != JudgmentMode.Disabled && IsInPlayableLevel())
                {
                    difficulty = (Main.Settings.Mode == JudgmentMode.Mobile)
                        ? Difficulty.Lenient
                        : GetCustomRepresentativeDifficulty();
                }
            }

            public static void Postfix(scrUIController __instance)
            {
                if (!Main.Enabled || !IsInPlayableLevel())
                    return;

                if (Main.Settings.Mode == JudgmentMode.Disabled)
                {
                    SetDifficultyArrowsActive(__instance, true);
                    return;
                }

                Difficulty targetDiff = (Main.Settings.Mode == JudgmentMode.Mobile)
                    ? Difficulty.Lenient
                    : GetCustomRepresentativeDifficulty();

                GCS.difficulty = targetDiff;

                if (__instance.difficultyText != null)
                {
                    __instance.difficultyText.text = (Main.Settings.Mode == JudgmentMode.Mobile)
                        ? Loc.Get("diff.mobile")
                        : Loc.Get("diff.custom");
                }

                SetDifficultyArrowsActive(__instance, false);
            }
        }

        /// <summary>
        /// 6. 禁用局内左右方向键切换难度（仅移动/自定模式锁定）
        /// </summary>
        [HarmonyPatch(typeof(scrUIController), "DifficultyArrowPressed")]
        public static class Patch_DifficultyArrowPressed
        {
            public static bool Prefix()
            {
                if (Main.Enabled && Main.Settings.Mode != JudgmentMode.Disabled)
                {
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// 7. 编辑器内禁止切换判定（仅移动/自定模式锁定）
        /// </summary>
        [HarmonyPatch(typeof(EditorDifficultySelector), "ToggleDifficulty")]
        public static class Patch_EditorToggleDifficulty
        {
            public static bool Prefix()
            {
                if (Main.Enabled && Main.Settings.Mode != JudgmentMode.Disabled)
                {
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// 8. 编辑器内判定文本重命名并禁用按钮
        /// </summary>
        [HarmonyPatch(typeof(EditorDifficultySelector), "UpdateDifficultyDisplay")]
        public static class Patch_EditorUpdateDifficultyDisplay
        {
            public static void Postfix(EditorDifficultySelector __instance)
            {
                if (!Main.Enabled || Main.Settings.Mode == JudgmentMode.Disabled)
                    return;

                GCS.difficulty = (Main.Settings.Mode == JudgmentMode.Mobile)
                    ? Difficulty.Lenient
                    : GetCustomRepresentativeDifficulty();

                if (__instance.editorDifficultyText != null)
                {
                    __instance.editorDifficultyText.text = (Main.Settings.Mode == JudgmentMode.Mobile)
                        ? Loc.Get("diff.mobile")
                        : Loc.Get("diff.custom");
                }

                if (__instance.buttonChangeDifficulty != null)
                    __instance.buttonChangeDifficulty.interactable = false;
                if (__instance.upArrowImage != null)
                    __instance.upArrowImage.gameObject.SetActive(false);
                if (__instance.downArrowImage != null)
                    __instance.downArrowImage.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 9. 编辑器交互状态变化时保持禁用
        /// </summary>
        [HarmonyPatch(typeof(EditorDifficultySelector), "SetChangeable")]
        public static class Patch_EditorSetChangeable
        {
            public static void Postfix(EditorDifficultySelector __instance)
            {
                if (!Main.Enabled || Main.Settings.Mode == JudgmentMode.Disabled)
                    return;

                if (__instance.buttonChangeDifficulty != null)
                    __instance.buttonChangeDifficulty.interactable = false;
                if (__instance.upArrowImage != null)
                    __instance.upArrowImage.gameObject.SetActive(false);
                if (__instance.downArrowImage != null)
                    __instance.downArrowImage.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 10. 防止将占位用的难度覆盖写入玩家本地存档默认偏好
        /// </summary>
        [HarmonyPatch(typeof(Persistence), "SetDefaultDifficulty")]
        public static class Patch_SetDefaultDifficulty
        {
            public static bool Prefix()
            {
                if (Main.Enabled && Main.Settings.Mode != JudgmentMode.Disabled)
                {
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// 11. 全局本地化字串拦截：判定名称重定向
        /// </summary>
        [HarmonyPatch(typeof(RDString), "Get", new Type[] { typeof(string), typeof(Dictionary<string, object>) })]
        public static class Patch_RDString_Get
        {
            public static void Postfix(string key, ref string __result)
            {
                if (!Main.Enabled)
                    return;

                if (Main.Settings.Mode != JudgmentMode.Disabled)
                {
                    if (key == "enum.Difficulty.Lenient" || key == "enum.Difficulty.Strict" || key == "enum.Difficulty.Normal")
                    {
                        Difficulty rep = (Main.Settings.Mode == JudgmentMode.Mobile)
                            ? Difficulty.Lenient
                            : GetCustomRepresentativeDifficulty();

                        if (key == "enum.Difficulty." + rep.ToString())
                        {
                            __result = (Main.Settings.Mode == JudgmentMode.Mobile)
                                ? Loc.Get("diff.mobile")
                                : Loc.Get("diff.custom");
                        }
                    }
                }
            }
        }

        // 缓存最近一次原生判定调用的未截断精确参数（避免 MoveToNextFloor 改变砖块 BPM 或 msDiff 整数截断造成重测失真）
        private static bool _isReEvaluating = false;
        private static bool _hasCachedHitParams = false;
        private static bool _cachedUsedSec = false;
        private static double _cachedTimeDiff = 0.0;
        private static float _cachedHitAngle = 0f;
        private static float _cachedRefAngle = 0f;
        private static bool _cachedIsCW = true;
        private static float _cachedFloorBpm = 100f;
        private static float _cachedConductorPitch = 1f;
        private static double _cachedMarginScale = 1.0;

        [HarmonyPatch(typeof(scrMisc), "GetHitMarginInSec")]
        public static class Patch_ScrMisc_GetHitMarginInSec
        {
            public static void Postfix(double timeDiff, float floorBpm, float conductorPitch, double marginScale)
            {
                if (_isReEvaluating) return;
                _hasCachedHitParams = true;
                _cachedUsedSec = true;
                _cachedTimeDiff = timeDiff;
                _cachedFloorBpm = floorBpm;
                _cachedConductorPitch = conductorPitch;
                _cachedMarginScale = marginScale;
            }
        }

        [HarmonyPatch(typeof(scrMisc), "GetHitMarginInDeg")]
        public static class Patch_ScrMisc_GetHitMarginInDeg
        {
            public static void Postfix(float hitAngle, float refAngle, bool clockwise, float floorBpm, float conductorPitch, double marginScale)
            {
                if (_isReEvaluating) return;
                _hasCachedHitParams = true;
                _cachedUsedSec = false;
                _cachedHitAngle = hitAngle;
                _cachedRefAngle = refAngle;
                _cachedIsCW = clockwise;
                _cachedFloorBpm = floorBpm;
                _cachedConductorPitch = conductorPitch;
                _cachedMarginScale = marginScale;
            }
        }

        private static HitMargin EvaluateDifficultyMargin(Difficulty diff, double fallbackTimeDiffSec, float fallbackBpm, float fallbackPitch, double fallbackMarginScale)
        {
            _isReEvaluating = true;
            try
            {
                if (_hasCachedHitParams)
                {
                    if (_cachedUsedSec)
                    {
                        return scrMisc.GetHitMarginInSec(diff, _cachedTimeDiff, _cachedFloorBpm, _cachedConductorPitch, _cachedMarginScale);
                    }
                    else
                    {
                        return scrMisc.GetHitMarginInDeg(diff, _cachedHitAngle, _cachedRefAngle, _cachedIsCW, _cachedFloorBpm, _cachedConductorPitch, _cachedMarginScale);
                    }
                }
                return scrMisc.GetHitMarginInSec(diff, fallbackTimeDiffSec, fallbackBpm, fallbackPitch, fallbackMarginScale);
            }
            finally
            {
                _isReEvaluating = false;
            }
        }

        /// <summary>
        /// 12. 判定指示器核心逻辑：
        /// 严格梯级测算 Strict -> Normal -> Lenient，完美适配练习模式降速 (0.9x) 与变速砖块，严判已成立时绝不误报标判/宽判
        /// </summary>
        [HarmonyPatch(typeof(scrHitTextManager), "ShowHitText")]
        public static class Patch_ShowHitText
        {
            public static void Prefix(HitMargin hitMargin, scrPlanet planet, ref float missAngle, int? msDiffNullable)
            {
                CurrentIndicator = null;

                if (AutoplayBot.IsActive() && planet != null)
                {
                    missAngle = 0f;
                }

                if (!Main.Enabled || Main.Settings.Mode != JudgmentMode.Disabled || !Main.Settings.EnableHitIndicator)
                    return;

                bool isCounted = (hitMargin == HitMargin.VeryEarly || hitMargin == HitMargin.VeryLate);
                bool isDoubleFail = (hitMargin == HitMargin.TooEarly || hitMargin == HitMargin.TooLate ||
                                     hitMargin == HitMargin.OverPress || hitMargin == HitMargin.FailMiss ||
                                     hitMargin == HitMargin.FailOverload);

                if (!isCounted && !(Main.Settings.EnableDoubleHitIndicator && isDoubleFail))
                    return;

                // 核心守卫 1：若当前已是「严格判定 (Strict)」，任何及格命中 (VeryEarly / VeryLate) 本身就已通过严判，绝不可能是标判或宽判保命！
                if (isCounted && GCS.difficulty == Difficulty.Strict)
                    return;

                scrPlanet effectivePlanet = planet;
                if (effectivePlanet == null && scrController.instance != null)
                {
                    effectivePlanet = scrController.instance.chosenPlanet;
                }

                float floorBpmVal = _hasCachedHitParams ? _cachedFloorBpm : 100f;
                float pitchVal = _hasCachedHitParams ? _cachedConductorPitch : 1f;
                double marginScale = _hasCachedHitParams ? _cachedMarginScale : 1.0;

                if (!_hasCachedHitParams)
                {
                    if (effectivePlanet != null && effectivePlanet.conductor != null)
                    {
                        floorBpmVal = (float)(effectivePlanet.conductor.bpm * effectivePlanet.planetarySystem.speed);
                        if (effectivePlanet.conductor.song != null)
                            pitchVal = effectivePlanet.conductor.song.pitch;
                        if (effectivePlanet.currfloor != null)
                            marginScale = effectivePlanet.currfloor.marginScale;
                    }
                    else if (scrConductor.instance != null)
                    {
                        floorBpmVal = (float)scrConductor.instance.bpm;
                        if (scrConductor.instance.song != null)
                            pitchVal = scrConductor.instance.song.pitch;
                    }
                }

                if (floorBpmVal <= 0f) floorBpmVal = 100f;
                if (pitchVal <= 0f) pitchVal = 1f;

                bool isEarly = false;
                if (msDiffNullable.HasValue)
                {
                    isEarly = msDiffNullable.Value < 0;
                }
                else if (hitMargin == HitMargin.VeryEarly || hitMargin == HitMargin.TooEarly || hitMargin == HitMargin.OverPress)
                {
                    isEarly = true;
                }
                else if (hitMargin == HitMargin.VeryLate || hitMargin == HitMargin.TooLate || hitMargin == HitMargin.FailMiss || hitMargin == HitMargin.FailOverload)
                {
                    isEarly = false;
                }
                else
                {
                    isEarly = missAngle < 0f;
                }

                double timeDiffSec;
                int rawMs;
                if (_hasCachedHitParams && _cachedUsedSec)
                {
                    timeDiffSec = _cachedTimeDiff;
                    rawMs = msDiffNullable.HasValue ? msDiffNullable.Value : (int)Math.Round(timeDiffSec * 1000.0);
                }
                else if (msDiffNullable.HasValue)
                {
                    rawMs = msDiffNullable.Value;
                    timeDiffSec = (double)rawMs / 1000.0;
                }
                else
                {
                    // missAngle 单位为弧度 (Radians)
                    timeDiffSec = scrMisc.AngleToTime((double)missAngle, (double)floorBpmVal) / (double)pitchVal;
                    rawMs = (int)Math.Round(timeDiffSec * 1000.0);
                }

                bool isNormalTier = false;
                bool isLenientTier = false;

                if (isCounted)
                {
                    if (GCS.difficulty == Difficulty.Normal)
                    {
                        // 在标准判定 (Normal) 下打出及格 (VeryEarly / VeryLate)，属于标判及格保命
                        isNormalTier = true;
                    }
                    else if (GCS.difficulty == Difficulty.Lenient)
                    {
                        // 在宽松判定 (Lenient) 下打出及格：若落入标准判定区间则为标判保命，否则为宽判保命
                        HitMargin normalRes = EvaluateDifficultyMargin(Difficulty.Normal, timeDiffSec, floorBpmVal, pitchVal, marginScale);
                        if (HitMarginHelper.IsCounted(normalRes, HitMarginLimit.None))
                        {
                            isNormalTier = true;
                        }
                        else
                        {
                            isLenientTier = true;
                        }
                    }
                }
                else if (isDoubleFail)
                {
                    // 失误比对 (DoubleHitIndicator)：测试失误击打是否能落入标准判定或宽松判定窗口
                    HitMargin normalRes = EvaluateDifficultyMargin(Difficulty.Normal, timeDiffSec, floorBpmVal, pitchVal, marginScale);
                    if (HitMarginHelper.IsCounted(normalRes, HitMarginLimit.None))
                    {
                        isNormalTier = true;
                    }
                    else
                    {
                        HitMargin lenientRes = EvaluateDifficultyMargin(Difficulty.Lenient, timeDiffSec, floorBpmVal, pitchVal, marginScale);
                        if (HitMarginHelper.IsCounted(lenientRes, HitMarginLimit.None))
                        {
                            isLenientTier = true;
                        }
                    }
                }

                if (!isNormalTier && !isLenientTier)
                {
                    return;
                }

                // 仅统计及格判定，失误断连不计入及格结算
                if (isCounted)
                {
                    if (isNormalTier)
                    {
                        HitIndicatorStats.NormalCount++;
                    }
                    else if (isLenientTier)
                    {
                        HitIndicatorStats.LenientCount++;
                    }

                    // 轨道染色：当打出及格的「标判！」和「宽判！」时对应给脚下轨道砖块染灰色和绿色
                    if (Main.Settings.EnableTrackColoring && effectivePlanet != null && effectivePlanet.currfloor != null)
                    {
                        Color trackColor = isNormalTier
                            ? new Color(0.55f, 0.56f, 0.58f, 1f)
                            : new Color(0.15f, 0.92f, 0.32f, 1f);
                        ColorHitFloor(effectivePlanet.currfloor, trackColor);
                    }
                }

                CurrentIndicator = new IndicatorData();
                CurrentIndicator.OutlineColor = isNormalTier
                    ? new Color(0.60f, 0.62f, 0.66f, 1f)
                    : new Color(0.15f, 0.95f, 0.35f, 1f);

                CurrentIndicator.DisplayText = GetIndicatorDisplayText(isLenientTier, isEarly, !isCounted, rawMs);
            }

            public static void Postfix()
            {
                _hasCachedHitParams = false;
            }
        }

        /// <summary>
        /// 13. 判定指示器描边与文字替换应用到浮动文本（真正同步修改 TMP 材质的 _OutlineColor / _GlowColor / _UnderlayColor，并兼容原生占位符替换）
        /// </summary>
        [HarmonyPatch(typeof(scrHitTextMesh), "Show")]
        public static class Patch_ScrHitTextMesh_Show
        {
            [HarmonyPriority(Priority.Last)]
            public static void Postfix(scrHitTextMesh __instance, float fadeSpeed, string customText)
            {
                // 排除毫秒误差/X-Score 附加副文本的子调用（副文本 customText 非空）
                if (!string.IsNullOrEmpty(customText))
                    return;

                if (CurrentIndicator == null || !__instance.gameObject.activeSelf)
                    return;

                if (!string.IsNullOrEmpty(CurrentIndicator.DisplayText) && __instance.text != null)
                {
                    __instance.text.text = CurrentIndicator.DisplayText;
                }

                if (__instance.xPerfectBorder != null)
                {
                    Color borderCol = CurrentIndicator.OutlineColor;
                    bool colorShaderBorder = (Main.Settings == null || Main.Settings.ColorHitTextBorder);

                    if (colorShaderBorder)
                    {
                        if (RDConstants.data != null && RDConstants.data.chineseFontTMProXPerfectBorderMaterial != null && Loc.IsCJK())
                        {
                            __instance.xPerfectBorder.fontSharedMaterial = RDConstants.data.chineseFontTMProXPerfectBorderMaterial;
                        }
                        __instance.xPerfectBorder.gameObject.SetActive(true);
                        if (__instance.text != null)
                        {
                            __instance.xPerfectBorder.text = __instance.text.text;
                        }
                        __instance.xPerfectBorder.color = borderCol;

                        __instance.xPerfectBorder.DOKill();
                        __instance.xPerfectBorder.DOFade(0f, 0.7f / fadeSpeed)
                            .SetDelay(0.5f / fadeSpeed)
                            .SetEase(Ease.OutQuad);
                    }
                }

                CurrentIndicator = null;
            }
        }

        /// <summary>
        /// 13b. 轨道染色常驻维持：防止节拍闪烁 (OnBeat -> SetToRandomColor) 或轨道变色事件覆盖已染色的标判/宽判砖块
        /// </summary>
        [HarmonyPatch(typeof(scrFloor), "SetToRandomColor")]
        public static class Patch_ScrFloor_SetToRandomColor
        {
            public static void Postfix(scrFloor __instance)
            {
                if (!Main.Enabled || Main.Settings == null || !Main.Settings.EnableHitIndicator || !Main.Settings.EnableTrackColoring)
                    return;

                Color targetColor;
                if (__instance != null && ColoredFloors.Count > 0 && ColoredFloors.TryGetValue(__instance, out targetColor))
                {
                    ApplyFloorColorInternal(__instance, targetColor);
                }
            }
        }

        [HarmonyPatch(typeof(scrFloor), "ColorFloor")]
        public static class Patch_ScrFloor_ColorFloor
        {
            public static void Postfix(scrFloor __instance)
            {
                if (!Main.Enabled || Main.Settings == null || !Main.Settings.EnableHitIndicator || !Main.Settings.EnableTrackColoring)
                    return;

                Color targetColor;
                if (__instance != null && ColoredFloors.Count > 0 && ColoredFloors.TryGetValue(__instance, out targetColor))
                {
                    ApplyFloorColorInternal(__instance, targetColor);
                }
            }
        }

        /// <summary>
        /// 13c. 更好的日语本地化：当处于日语且开启该选项时，将原版未翻译的英文判定词完整本地化为日语
        /// </summary>
        [HarmonyPatch(typeof(RDString), "GetWithCheck")]
        public static class Patch_RDString_GetWithCheck
        {
            public static void Postfix(string key, ref bool exists, ref string __result)
            {
                if (!Main.Enabled || Main.Settings == null || !Main.Settings.MasterEnabled || !Main.Settings.BetterJapaneseLocalization)
                    return;

                if (Loc.CurrentLanguage != SystemLanguage.Japanese)
                    return;

                string jpVal;
                if (Loc.TryGetBetterJapaneseRDString(key, out jpVal))
                {
                    exists = true;
                    __result = jpVal;
                }
            }
        }

        /// <summary>
        /// 13d. 日语使用中文字体：当处于日语且开启「更好的日语本地化 -> 日语使用中文字体」时，替换原版日语字体为中文字体
        /// </summary>
        [HarmonyPatch(typeof(RDString), "GetFontDataForLanguage")]
        public static class Patch_RDString_GetFontDataForLanguage
        {
            public static void Prefix(ref SystemLanguage language)
            {
                if (Main.Enabled &&
                    Main.Settings != null &&
                    Main.Settings.MasterEnabled &&
                    Main.Settings.BetterJapaneseLocalization &&
                    Main.Settings.JapaneseUseChineseFont &&
                    language == SystemLanguage.Japanese)
                {
                    language = SystemLanguage.ChineseSimplified;
                }
            }
        }

        [HarmonyPatch(typeof(RDString), "GetAppropiateFontForString")]
        public static class Patch_RDString_GetAppropiateFontForString
        {
            public static void Postfix(ref Font __result)
            {
                if (Main.Enabled &&
                    Main.Settings != null &&
                    Main.Settings.MasterEnabled &&
                    Main.Settings.BetterJapaneseLocalization &&
                    Main.Settings.JapaneseUseChineseFont &&
                    Loc.CurrentLanguage == SystemLanguage.Japanese &&
                    RDConstants.data != null &&
                    RDConstants.data.chineseFont != null &&
                    __result == RDConstants.data.japaneseFont)
                {
                    __result = RDConstants.data.chineseFont;
                }
            }
        }

        [HarmonyPatch(typeof(RDString), "GetInlinedFontForString")]
        public static class Patch_RDString_GetInlinedFontForString
        {
            public static void Postfix(ref string __result)
            {
                if (Main.Enabled &&
                    Main.Settings != null &&
                    Main.Settings.MasterEnabled &&
                    Main.Settings.BetterJapaneseLocalization &&
                    Main.Settings.JapaneseUseChineseFont &&
                    Loc.CurrentLanguage == SystemLanguage.Japanese &&
                    !string.IsNullOrEmpty(__result) &&
                    RDConstants.data != null &&
                    RDConstants.data.japaneseFont != null &&
                    RDConstants.data.chineseFont != null)
                {
                    string jpName = RDConstants.data.japaneseFont.name;
                    string cnName = RDConstants.data.chineseFont.name;
                    if (!string.IsNullOrEmpty(jpName) && !string.IsNullOrEmpty(cnName) && __result.IndexOf(jpName) >= 0)
                    {
                        __result = __result.Replace("<font=\"" + jpName + "\">", "<font=\"" + cnName + "\">");
                    }
                }
            }
        }

        /// <summary>
        /// 14. 判定指示器描边应用到判定条打点 (ErrorMeterTick)
        /// </summary>
        [HarmonyPatch(typeof(ADOFAI.ErrorMeterTick), "Show")]
        public static class Patch_ErrorMeterTick_Show
        {
            public static void Postfix(ADOFAI.ErrorMeterTick __instance, string tweenId, float tweenDuration)
            {
                if (CurrentIndicator == null)
                    return;

                if (__instance.tickOutline != null)
                {
                    string outlineTweenId = tweenId + "o";
                    DOTween.Kill(outlineTweenId);
                    __instance.tickOutline.color = CurrentIndicator.OutlineColor;
                    __instance.tickOutline.DOColor(Color.clear, tweenDuration)
                        .SetEase(Ease.InQuad)
                        .SetId(outlineTweenId);
                }
            }
        }

        /// <summary>
        /// 15. 结算详细结果 (Detailed Results) 注入「标判」「宽判」统计数据与通关判定印章
        /// </summary>
        [HarmonyPatch(typeof(DetailedResults), "GenerateResults")]
        public static class Patch_DetailedResults_GenerateResults
        {
            public static void Postfix(scrMarginTracker marginTracker, ref string __result)
            {
                if (!Main.Enabled)
                    return;

                // 1. 判定指示器行：仅当出现标判或宽判时才在结算中显示
                if (Main.Settings.EnableHitIndicator)
                {
                    if (HitIndicatorStats.NormalCount > 0 || HitIndicatorStats.LenientCount > 0)
                    {
                        string normalLabel = Loc.Get("results.normal");
                        string lenientLabel = Loc.Get("results.lenient");

                        string indicatorLine = string.Format(
                            "<color=#aaaaaa>{0}: {1}</color>     <color=#26ff59>{2}: {3}</color>\n",
                            normalLabel, HitIndicatorStats.NormalCount,
                            lenientLabel, HitIndicatorStats.LenientCount);

                        string lateKey = RDString.Get("status.results.late", null) + ": ";
                        int lateIdx = __result.IndexOf(lateKey);
                        if (lateIdx >= 0)
                        {
                            int newlineIdx = __result.IndexOf('\n', lateIdx);
                            if (newlineIdx >= 0)
                            {
                                __result = __result.Insert(newlineIdx + 1, indicatorLine);
                            }
                            else
                            {
                                __result += indicatorLine;
                            }
                        }
                        else
                        {
                            string earlyKey = RDString.Get("status.results.early", null) + ": ";
                            int earlyIdx = __result.IndexOf(earlyKey);
                            if (earlyIdx >= 0)
                            {
                                int newlineIdx = __result.IndexOf('\n', earlyIdx);
                                if (newlineIdx >= 0)
                                {
                                    __result = __result.Insert(newlineIdx + 1, indicatorLine);
                                }
                                else
                                {
                                    __result += indicatorLine;
                                }
                            }
                            else
                            {
                                __result += indicatorLine;
                            }
                        }
                    }
                }

                // 2. 通关判定印章：若通关过程中使用过移动判定或自定判定，显示通关标记（复用严判过关逻辑，不加方括号与多余颜色）
                string stamp = string.Empty;
                if (UsedMobileThisAttempt && UsedCustomThisAttempt)
                {
                    stamp = string.Format("\n{0} / {1}", Loc.Get("stamp.mobile"), Loc.Get("stamp.custom"));
                }
                else if (UsedMobileThisAttempt)
                {
                    stamp = string.Format("\n{0}", Loc.Get("stamp.mobile"));
                }
                else if (UsedCustomThisAttempt)
                {
                    stamp = string.Format("\n{0}", Loc.Get("stamp.custom"));
                }

                if (!string.IsNullOrEmpty(stamp))
                {
                    __result += stamp;
                }
            }
        }

        /// <summary>
        /// 16. 关卡重开或重置时重置指示器统计数据与通关尝试标记
        /// </summary>
        [HarmonyPatch(typeof(scrMarginTracker), "Reset")]
        public static class Patch_ScrMarginTracker_Reset
        {
            public static void Postfix()
            {
                HitIndicatorStats.Reset();
                AutoplayBot.Reset();
                bool inPlayable = IsInPlayableLevel();
                UsedMobileThisAttempt = (Main.Enabled && inPlayable && Main.Settings.Mode == JudgmentMode.Mobile);
                UsedCustomThisAttempt = (Main.Enabled && inPlayable && Main.Settings.Mode == JudgmentMode.Custom);
            }
        }

        /// <summary>
        /// 17. 关卡启动初始化：重置尝试标记并确保不死模式与自动播放状态就绪（主界面只保留水印，不改判定/不锁自动）
        /// </summary>
        [HarmonyPatch(typeof(scrController), "Start")]
        public static class Patch_ScrController_Start
        {
            public static void Prefix()
            {
                HitIndicatorStats.Reset();
                AutoplayBot.Reset();
                bool inPlayable = IsInPlayableLevel();
                UsedMobileThisAttempt = (Main.Enabled && inPlayable && Main.Settings.Mode == JudgmentMode.Mobile);
                UsedCustomThisAttempt = (Main.Enabled && inPlayable && Main.Settings.Mode == JudgmentMode.Custom);

                if (!Main.Enabled || !Main.Settings.MasterEnabled)
                    return;

                if (!inPlayable)
                {
                    RDC.auto = false;
                    return;
                }

                if (Main.Settings.NoFail)
                {
                    GCS.useNoFail = true;
                }

                if (Main.Settings.Autoplay || Main.Settings.AutoplayPlus)
                {
                    RDC.auto = true;
                }
            }

            public static void Postfix(scrController __instance)
            {
                if (!Main.Enabled || !Main.Settings.MasterEnabled)
                    return;

                bool inPlayable = IsInPlayableLevel();
                UsedMobileThisAttempt = (Main.Enabled && inPlayable && Main.Settings.Mode == JudgmentMode.Mobile);
                UsedCustomThisAttempt = (Main.Enabled && inPlayable && Main.Settings.Mode == JudgmentMode.Custom);

                if (!inPlayable)
                {
                    RDC.auto = false;
                    if (__instance.txtAllStrictClear != null)
                    {
                        __instance.txtAllStrictClear.gameObject.SetActive(false);
                    }
                    UpdateHUDText();
                    return;
                }

                if (Main.Settings.NoFail)
                {
                    __instance.noFail = true;
                    __instance.freeroamInvulnerability = true;
                    if (scrUIController.instance != null)
                    {
                        if (scrUIController.instance.modifiersContainer != null && !scrUIController.instance.modifiersContainer.gameObject.activeSelf)
                        {
                            scrUIController.instance.modifiersContainer.gameObject.SetActive(true);
                        }
                        if (scrUIController.instance.noFailImage != null)
                        {
                            scrUIController.instance.noFailImage.gameObject.SetActive(true);
                        }
                    }
                }

                if (Main.Settings.Autoplay || Main.Settings.AutoplayPlus)
                {
                    RDC.auto = true;
                }

                UpdateHUDText();
                HideSpeedWarningIfSubstandard();
            }
        }

        /// <summary>
        /// 18. 不死模式与自动播放快捷键及状态常驻（兼容官谱与任何模式；主界面仅保留水印，不修改判定/不锁自动）
        /// 全场景高灵敏 ESC 响应：自定义关卡菜单退出/返回/关闭面板，关卡内可靠暂停/继续与模拟按键释放
        /// </summary>
        [HarmonyPatch(typeof(scrController), "Update")]
        public static class Patch_ScrController_Update
        {
            public static void Prefix(scrController __instance, out bool __state)
            {
                __state = (__instance != null && __instance.paused);
            }

            public static void Postfix(scrController __instance, bool __state)
            {
                if (!Main.Enabled || !Main.Settings.MasterEnabled)
                    return;

                AutoplayBot.Update();

                bool inPlayable = IsInPlayableLevel();

                // 全场景高灵敏 ESC 按键处理
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    // 场景 A: 自定义关卡菜单 / 选曲界面 (scnCLS)
                    if (ADOBase.isCLS || scnCLS.instance != null)
                    {
                        scnCLS cls = scnCLS.instance ?? ADOBase.cls;
                        if (cls != null)
                        {
                            if (cls.optionsPanels != null && cls.optionsPanels.showingAnyPanel)
                            {
                                cls.optionsPanels.HideAnyPanel();
                                cls.optionsPanels.justHidPanels = true;
                            }
                            else if (cls.importLevelPanel != null && cls.importLevelPanel.gameObject != null && cls.importLevelPanel.gameObject.activeSelf)
                            {
                                cls.CloseImportPanel();
                            }
                            else if (__instance != null && __instance.paused)
                            {
                                __instance.TogglePauseGame();
                            }
                            else
                            {
                                string curFolder = (F_ClsCurrentFolderName != null) ? F_ClsCurrentFolderName.GetValue(cls) as string : null;
                                if (!string.IsNullOrEmpty(curFolder))
                                {
                                    cls.ExitFolder();
                                }
                                else
                                {
                                    cls.QuitPortal();
                                }
                            }
                        }
                    }
                    // 场景 B: 可游玩关卡 (自定义关卡与官方关卡)
                    else if (inPlayable && __instance != null)
                    {
                        if (AutoplayBot.IsActive())
                        {
                            AutoplayBot.ReleaseAllKeys();
                        }

                        if (!__state && !__instance.paused)
                        {
                            __instance.TogglePauseGame();
                        }
                        else if (__state && __instance.paused && __instance.pauseMenu != null && __instance.pauseMenu.gameObject.activeSelf)
                        {
                            __instance.pauseMenu.BackButtonAction(false);
                        }
                    }
                }

                // 仅在可游玩关卡内记录当前尝试使用的判定模式
                if (inPlayable)
                {
                    if (Main.Settings.Mode == JudgmentMode.Mobile) UsedMobileThisAttempt = true;
                    if (Main.Settings.Mode == JudgmentMode.Custom) UsedCustomThisAttempt = true;
                }
                else
                {
                    UsedMobileThisAttempt = false;
                    UsedCustomThisAttempt = false;
                }

                // 快捷键监听：Alt + N 切换不死，Alt + Shift + A 切换自动播放+，Alt + A 切换标准自动播放
                bool holdingAlt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                bool holdingShift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

                if (holdingAlt && Input.GetKeyDown(KeyCode.N))
                {
                    Main.Settings.NoFail = !Main.Settings.NoFail;
                    ApplyNoFail(Main.Settings.NoFail);
                    Main.Settings.Save(Main.ModEntry);
                    if (scrSfx.instance != null)
                    {
                        scrSfx.instance.PlaySfx(Main.Settings.NoFail ? SfxSound.ModifierActivate : SfxSound.ModifierDeactivate, MixerGroup.InterfaceParent);
                    }
                }

                if (holdingAlt && holdingShift && Input.GetKeyDown(KeyCode.A))
                {
                    Main.Settings.AutoplayPlus = !Main.Settings.AutoplayPlus;
                    ApplyAutoplayPlus(Main.Settings.AutoplayPlus);
                    Main.Settings.Save(Main.ModEntry);
                    if (scrSfx.instance != null)
                    {
                        scrSfx.instance.PlaySfx(Main.Settings.AutoplayPlus ? SfxSound.ModifierActivate : SfxSound.ModifierDeactivate, MixerGroup.InterfaceParent);
                    }
                }
                else if (holdingAlt && Input.GetKeyDown(KeyCode.A))
                {
                    Main.Settings.Autoplay = !Main.Settings.Autoplay;
                    ApplyAutoplay(Main.Settings.Autoplay);
                    Main.Settings.Save(Main.ModEntry);
                    if (scrSfx.instance != null)
                    {
                        scrSfx.instance.PlaySfx(Main.Settings.Autoplay ? SfxSound.ModifierActivate : SfxSound.ModifierDeactivate, MixerGroup.InterfaceParent);
                    }
                }

                if (!inPlayable)
                {
                    // 在主页面/选曲大厅中，严禁启用 RDC.auto（避免阻塞主页面行星自由移动），只保留左上角水印
                    if ((Main.Settings.Autoplay || Main.Settings.AutoplayPlus) && RDC.auto)
                    {
                        RDC.auto = false;
                    }
                    return;
                }

                // 不死模式常驻保持
                if (Main.Settings.NoFail)
                {
                    __instance.noFail = true;
                    __instance.freeroamInvulnerability = true;
                    if (!GCS.useNoFail)
                    {
                        GCS.useNoFail = true;
                    }
                    if (scrUIController.instance != null)
                    {
                        if (scrUIController.instance.modifiersContainer != null && !scrUIController.instance.modifiersContainer.gameObject.activeSelf)
                        {
                            scrUIController.instance.modifiersContainer.gameObject.SetActive(true);
                        }
                        if (scrUIController.instance.noFailImage != null && !scrUIController.instance.noFailImage.gameObject.activeSelf)
                        {
                            scrUIController.instance.noFailImage.gameObject.SetActive(true);
                        }
                    }
                }

                // 自动播放与自动播放+ 常驻保持（仅在可游玩关卡内）
                if (Main.Settings.Autoplay || Main.Settings.AutoplayPlus)
                {
                    if (!RDC.auto)
                    {
                        RDC.auto = true;
                        UpdateHUDText();
                    }
                }
            }
        }

        /// <summary>
        /// 19. 保证行星选定时不死模式与自动播放不被关卡脚本覆写，并在 SwitchChosen 前一刻注入自动播放+ 拟人角度
        /// </summary>
        [HarmonyPatch(typeof(scrPlanet), "SwitchChosen")]
        public static class Patch_ScrPlanet_SwitchChosen
        {
            public static void Prefix(scrPlanet __instance, ref long? hitTick)
            {
                if (!Main.Enabled || !Main.Settings.MasterEnabled || !IsInPlayableLevel())
                    return;

                if (Main.Settings.NoFail)
                {
                    GCS.useNoFail = true;
                    if (scrController.instance != null)
                    {
                        scrController.instance.noFail = true;
                        scrController.instance.freeroamInvulnerability = true;
                    }
                    if (scrUIController.instance != null)
                    {
                        if (scrUIController.instance.modifiersContainer != null && !scrUIController.instance.modifiersContainer.gameObject.activeSelf)
                        {
                            scrUIController.instance.modifiersContainer.gameObject.SetActive(true);
                        }
                        if (scrUIController.instance.noFailImage != null && !scrUIController.instance.noFailImage.gameObject.activeSelf)
                        {
                            scrUIController.instance.noFailImage.gameObject.SetActive(true);
                        }
                    }
                }

                if (Main.Settings.Autoplay || Main.Settings.AutoplayPlus)
                {
                    RDC.auto = true;
                }

                if (Main.Settings.AutoplayPlus)
                {
                    AutoplayBot.ApplyPendingAngleToPlanet(__instance, ref hitTick);
                }
            }
        }

        /// <summary>
        /// 20. 拦截一切死亡结算动作（不死模式三重保障）
        /// </summary>
        [HarmonyPatch(typeof(scrController), "FailAction")]
        public static class Patch_ScrController_FailAction
        {
            public static bool Prefix()
            {
                if (Main.Enabled && Main.Settings.MasterEnabled && Main.Settings.NoFail)
                {
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// 21a. 自动播放+：在原生 OttoHoldHit 调用 scrPlayer.Hit(long? hitTick, bool isAuto) 时同步执行拟人排键与目标 X 精准度偏移
        /// </summary>
        [HarmonyPatch(typeof(scrPlayer), "Hit")]
        public static class Patch_ScrPlayer_Hit
        {
            public static void Prefix(scrPlayer __instance, ref long? hitTick, bool isAuto, out bool __state)
            {
                __state = false;
                if (isAuto && AutoplayBot.IsPlayingInLevel())
                {
                    bool tempUseOldAuto;
                    AutoplayBot.OnAutoHit(__instance, ref hitTick, out tempUseOldAuto);
                    if (tempUseOldAuto && !RDC.useOldAuto)
                    {
                        RDC.useOldAuto = true;
                        __state = true;
                    }
                }
            }

            public static void Postfix(bool __state)
            {
                if (__state)
                {
                    RDC.useOldAuto = false;
                }
            }
        }

        /// <summary>
        /// 21b. 自动播放+：在关卡内 PlayerControl 演奏期间清空 AsyncInputManager.keyQueue 回环队列，
        /// 让 UpdateInput 直接进入 ProcessKeyInputs(currFrameTick) -> Simulated_PlayerControl_Update -> OttoHoldHit 驱动行星前进，
        /// 同时防止 keybd_event 模拟按键回环触发二次击打（不影响 SkyHook 向 JipperResourcePack 推送按键）
        /// </summary>
        [HarmonyPatch(typeof(scrController), "UpdateInput")]
        public static class Patch_ScrController_UpdateInput
        {
            public static void Prefix(scrController __instance)
            {
                // 不再在此处调用 AsyncInputManager.ClearKeys()，防止吞掉 ESC 键导致无法暂停游戏
                // 模拟按键与多余击打的拦截已由 Patch_ScrPlayer_HitInputEvent 和 ValidInputWasTriggered 等可靠负责
            }
        }

        /// <summary>
        /// 21c. 自动播放+：在关卡内 PlayerControl 演奏期间拦截非自动 HitInputEvent
        /// </summary>
        [HarmonyPatch(typeof(scrPlayer), "HitInputEvent")]
        public static class Patch_ScrPlayer_HitInputEvent
        {
            public static bool Prefix(bool isAuto)
            {
                if (!isAuto &&
                    AutoplayBot.IsPlayingInLevel() &&
                    scrController.instance != null &&
                    scrController.instance.state == States.PlayerControl &&
                    !scrController.instance.paused)
                {
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// 21d. 自动播放+：在关卡内 PlayerControl 演奏期间阻断外部物理按键与 keybd_event 回环信号触发多余击打（绝不拦截主页面移动与倒计时前的按任意键开始）
        /// </summary>
        [HarmonyPatch(typeof(scrPlayer), "ValidInputWasTriggered")]
        public static class Patch_ScrPlayer_ValidInputWasTriggered
        {
            public static bool Prefix(ref bool __result)
            {
                if (AutoplayBot.IsPlayingInLevel() &&
                    scrController.instance != null &&
                    scrController.instance.state == States.PlayerControl &&
                    !scrController.instance.paused)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(scrPlayer), "ValidInputWasReleased")]
        public static class Patch_ScrPlayer_ValidInputWasReleased
        {
            public static bool Prefix(ref bool __result)
            {
                if (AutoplayBot.IsPlayingInLevel() &&
                    scrController.instance != null &&
                    scrController.instance.state == States.PlayerControl &&
                    !scrController.instance.paused)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// 21e. 自动播放+：在关卡内 PlayerControl 演奏期间清零按键数，防止 keybd_event 回环按键引起多押空敲
        /// </summary>
        [HarmonyPatch(typeof(scrPlayer), "CountValidKeysPressed")]
        public static class Patch_ScrPlayer_CountValidKeysPressed
        {
            public static bool Prefix(ref int __result)
            {
                if (AutoplayBot.IsPlayingInLevel() &&
                    scrController.instance != null &&
                    scrController.instance.state == States.PlayerControl &&
                    !scrController.instance.paused)
                {
                    __result = 0;
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// 21f. 自动播放+：零延迟向局内 KV 模组（如 CharonKV）注入 Input.GetKey 状态，并彻底隔离玩家物理按键对 KV 的干扰
        /// </summary>
        [HarmonyPatch(typeof(Input), "GetKey", new Type[] { typeof(KeyCode) })]
        public static class Patch_Input_GetKey
        {
            public static void Postfix(KeyCode key, ref bool __result)
            {
                if (AutoplayBot.IsPlayingInLevel() && AutoplayBot.IsTrackedGameplayKey(key))
                {
                    __result = AutoplayBot.IsSimulatingKey(key);
                }
            }
        }

        [HarmonyPatch(typeof(Input), "GetKey", new Type[] { typeof(string) })]
        public static class Patch_Input_GetKey_String
        {
            public static void Postfix(string name, ref bool __result)
            {
                KeyCode code;
                if (AutoplayBot.IsPlayingInLevel() && AutoplayBot.TryGetKeyCodeByName(name, out code) && AutoplayBot.IsTrackedGameplayKey(code))
                {
                    __result = AutoplayBot.IsSimulatingKey(code);
                }
            }
        }

        /// <summary>
        /// 21g. 自动播放+：零延迟向局内 KV 模组注入 Input.GetKeyDown 状态，并彻底隔离玩家物理按键对 KV 的干扰
        /// </summary>
        [HarmonyPatch(typeof(Input), "GetKeyDown", new Type[] { typeof(KeyCode) })]
        public static class Patch_Input_GetKeyDown
        {
            public static void Postfix(KeyCode key, ref bool __result)
            {
                if (AutoplayBot.IsPlayingInLevel() && AutoplayBot.IsTrackedGameplayKey(key))
                {
                    __result = AutoplayBot.IsSimulatingKeyDown(key);
                }
            }
        }

        [HarmonyPatch(typeof(Input), "GetKeyDown", new Type[] { typeof(string) })]
        public static class Patch_Input_GetKeyDown_String
        {
            public static void Postfix(string name, ref bool __result)
            {
                KeyCode code;
                if (AutoplayBot.IsPlayingInLevel() && AutoplayBot.TryGetKeyCodeByName(name, out code) && AutoplayBot.IsTrackedGameplayKey(code))
                {
                    __result = AutoplayBot.IsSimulatingKeyDown(code);
                }
            }
        }

        /// <summary>
        /// 21h. 自动播放+：零延迟向局内 KV 模组注入 Input.GetKeyUp 状态，并彻底隔离玩家物理按键对 KV 的干扰
        /// </summary>
        [HarmonyPatch(typeof(Input), "GetKeyUp", new Type[] { typeof(KeyCode) })]
        public static class Patch_Input_GetKeyUp
        {
            public static void Postfix(KeyCode key, ref bool __result)
            {
                if (AutoplayBot.IsPlayingInLevel() && AutoplayBot.IsTrackedGameplayKey(key))
                {
                    __result = AutoplayBot.IsSimulatingKeyUp(key);
                }
            }
        }

        [HarmonyPatch(typeof(Input), "GetKeyUp", new Type[] { typeof(string) })]
        public static class Patch_Input_GetKeyUp_String
        {
            public static void Postfix(string name, ref bool __result)
            {
                KeyCode code;
                if (AutoplayBot.IsPlayingInLevel() && AutoplayBot.TryGetKeyCodeByName(name, out code) && AutoplayBot.IsTrackedGameplayKey(code))
                {
                    __result = AutoplayBot.IsSimulatingKeyUp(code);
                }
            }
        }


        /// <summary>
        /// 22. 杂项：隐藏星球判定圈 (scrRing.Update)
        /// </summary>
        [HarmonyPatch(typeof(scrRing), "Update")]
        public static class Patch_ScrRing_Update
        {
            public static void Postfix(scrRing __instance)
            {
                if (Main.Enabled && Main.Settings.MasterEnabled && Main.Settings.HidePlanetRing)
                {
                    if (__instance.line != null && __instance.line.enabled)
                    {
                        __instance.line.enabled = false;
                    }
                }
            }
        }

        /// <summary>
        /// 23. 杂项：隐藏星球判定圈 (scrRing.Switch)
        /// </summary>
        [HarmonyPatch(typeof(scrRing), "Switch")]
        public static class Patch_ScrRing_Switch
        {
            public static void Postfix(scrRing __instance)
            {
                if (Main.Enabled && Main.Settings.MasterEnabled && Main.Settings.HidePlanetRing)
                {
                    if (__instance.line != null && __instance.line.enabled)
                    {
                        __instance.line.enabled = false;
                    }
                }
            }
        }


        /// <summary>
        /// 25. 通关判定印章（直接复用官方严判通关 txtAllStrictClear 逻辑，仅限真实关卡终点，绝不在主页面进入关卡传送门时触发）
        /// </summary>
        [HarmonyPatch(typeof(scrController), "OnLandOnPortal")]
        public static class Patch_ScrController_OnLandOnPortal
        {
            public static void Postfix(scrController __instance)
            {
                if (!Main.Enabled || !Main.Settings.MasterEnabled)
                    return;

                if (!IsInPlayableLevel() || !__instance.gameworld || __instance.isPuzzleRoom)
                    return;

                if (__instance.startedFromCheckpoint)
                    return;

                if (UsedMobileThisAttempt || UsedCustomThisAttempt)
                {
                    string clearText;
                    if (UsedMobileThisAttempt && UsedCustomThisAttempt)
                    {
                        clearText = Loc.Get("stamp.mobile") + " / " + Loc.Get("stamp.custom");
                    }
                    else if (UsedMobileThisAttempt)
                    {
                        clearText = Loc.Get("stamp.mobile");
                    }
                    else
                    {
                        clearText = Loc.Get("stamp.custom");
                    }

                    UnityEngine.UI.Text txt = __instance.txtAllStrictClear;
                    if (txt == null && scrUIController.instance != null)
                    {
                        txt = scrUIController.instance.txtAllStrictClear;
                    }

                    if (txt != null)
                    {
                        txt.text = clearText;
                        txt.gameObject.SetActive(true);
                    }
                }
            }
        }
    }
}
