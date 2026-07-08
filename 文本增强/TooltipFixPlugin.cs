using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace TooltipFixMod
{
    [BepInPlugin("com.yourname.tooltipfix", "Tooltip Display Fix", "1.2.1")]
    public class TooltipFixPlugin : BaseUnityPlugin
    {
        public static string ConfigPath;

        void Awake()
        {
            ConfigPath = Path.Combine(Application.dataPath, "Lang", "LiquidEffects.xml");
            LiquidEffectManager.LoadConfig();

            Harmony harmony = new Harmony("com.yourname.tooltipfix");
            harmony.PatchAll();

            Logger.LogInfo($"TooltipFixMod v1.2.1 Loaded (Flicker-Free Engine).");
        }
    }

    [HarmonyPatch(typeof(GlobalDark), "HandleTooltip")]
    public class TooltipRenderPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(GlobalDark __instance)
        {
            __instance.tooltipRect.anchoredPosition = Input.mousePosition;

            var uitooltip = UIUtil.GetUITooltip(UIUtil.GetEventSystemRaycastResults(null));
            if (!string.IsNullOrEmpty(uitooltip.Item1))
            {
                __instance.tooltipText = uitooltip;
            }

            __instance.tooltipRect.gameObject.SetActive(__instance.showTooltip);

            if (__instance.showTooltip)
            {
                // 1. 同步缩放
                __instance.tooltipRect.localScale = Vector3.Lerp(__instance.tooltipRect.localScale, Vector3.one * (float)Screen.height / 1080f, Time.unscaledDeltaTime * 24f);

                if (__instance.distortionAmount > 0f)
                {
                    __instance.tooltipText.Item1 = __instance.DistortStr(__instance.tooltipText.Item1);
                    __instance.tooltipText.Item2 = __instance.DistortStr(__instance.tooltipText.Item2);
                }

                __instance.tooltipNameObj.text = __instance.tooltipText.Item1;

                // 2. 挂载遮罩，防止溢出
                if (__instance.tooltipDescObj.transform.parent != __instance.hoverDescriptionBackground.transform)
                {
                    __instance.tooltipDescObj.transform.SetParent(__instance.hoverDescriptionBackground.transform, true);
                }

                if (!__instance.hoverDescriptionBackground.TryGetComponent<RectMask2D>(out _))
                {
                    __instance.hoverDescriptionBackground.gameObject.AddComponent<RectMask2D>();
                }

                __instance.tooltipDescObj.rectTransform.anchorMin = new Vector2(0f, 1f);
                __instance.tooltipDescObj.rectTransform.anchorMax = new Vector2(1f, 1f);
                __instance.tooltipDescObj.rectTransform.pivot = new Vector2(0f, 1f);

                string rawName = __instance.tooltipText.Item1;
                string rawDesc = __instance.tooltipText.Item2;
                UIUtilPatchHelper.cachedMaxHeight = (float)Screen.height - 150f * __instance.uiScale;

                bool isShiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                float scrollInput = Input.mouseScrollDelta.y;

                bool descChanged = (rawDesc != UIUtilPatchHelper.lastRawInputDesc || rawName != UIUtilPatchHelper.lastRawInputName);

                // 3. 数据源变更时，重新解析液体段落
                if (descChanged)
                {
                    UIUtilPatchHelper.lastRawInputDesc = rawDesc;
                    UIUtilPatchHelper.lastRawInputName = rawName;
                    UIUtilPatchHelper.ParseLiquids(rawDesc, out UIUtilPatchHelper.cachedLiquids, out UIUtilPatchHelper.cachedContainerHeader);
                    UIUtilPatchHelper.EvaluateOverviewStrategy(rawName, UIUtilPatchHelper.cachedLiquids, out UIUtilPatchHelper.activeOverviewModes, out UIUtilPatchHelper.useSplitOverview, out UIUtilPatchHelper.useLiquidListPage);

                    StringBuilder structSb = new StringBuilder(rawName);
                    foreach (var l in UIUtilPatchHelper.cachedLiquids) structSb.Append("_").Append(l.Name);
                    string currentStructure = structSb.ToString();

                    if (currentStructure != UIUtilPatchHelper.lastLiquidStructure)
                    {
                        UIUtilPatchHelper.lastLiquidStructure = currentStructure;
                        int newMinPage = 0;
                        if (UIUtilPatchHelper.activeOverviewModes.Count > 0)
                        {
                            newMinPage = UIUtilPatchHelper.useSplitOverview ? -UIUtilPatchHelper.activeOverviewModes.Count : -1;
                            if (UIUtilPatchHelper.useLiquidListPage) newMinPage = -UIUtilPatchHelper.activeOverviewModes.Count - 1;
                        }
                        UIUtilPatchHelper.currentLiquidPageIndex = newMinPage;
                    }
                }

                int minPage = 0;
                if (UIUtilPatchHelper.activeOverviewModes.Count > 0)
                {
                    minPage = UIUtilPatchHelper.useSplitOverview ? -UIUtilPatchHelper.activeOverviewModes.Count : -1;
                    if (UIUtilPatchHelper.useLiquidListPage) minPage = -UIUtilPatchHelper.activeOverviewModes.Count - 1;
                }

                // 4. 计算当前文本原始大小
                __instance.tooltipDescObj.text = rawDesc;
                __instance.tooltipDescObj.ForceMeshUpdate();
                float originalFullHeight = __instance.tooltipDescObj.preferredHeight;

                UIUtilPatchHelper.isCollapsibleMode = (originalFullHeight > UIUtilPatchHelper.cachedMaxHeight && UIUtilPatchHelper.cachedLiquids.Count > 0);

                // 5. 根据 Shift 状态分支渲染，避免产生原版折叠测量的过渡帧
                if (!isShiftHeld)
                {
                    UIUtilPatchHelper.currentLiquidPageIndex = minPage;
                    UIUtilPatchHelper.scrollOffset = 0f;

                    if (UIUtilPatchHelper.isCollapsibleMode)
                    {
                        // 渲染自定义折叠布局，防止原版 1 帧测绘延迟
                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine(UIUtilPatchHelper.cachedContainerHeader.TrimEnd());
                        sb.AppendLine("<color=#666666>──────────────────────────</color>");
                        foreach (var l in UIUtilPatchHelper.cachedLiquids)
                        {
                            sb.AppendLine($"  <color=#CCCCCC>{l.headerLine}</color>");
                        }
                        sb.AppendLine("<color=#666666>──────────────────────────</color>");
                        sb.AppendLine("<color=#00FF00>◆ 按住 Shift 展开预测与详细信息</color>");
                        __instance.tooltipDescObj.text = sb.ToString();
                    }
                    else
                    {
                        __instance.tooltipDescObj.text = rawDesc;
                    }
                }
                else
                {
                    if (UIUtilPatchHelper.cachedLiquids.Count > 0)
                    {
                        if (UIUtilPatchHelper.currentLiquidPageIndex < minPage) UIUtilPatchHelper.currentLiquidPageIndex = minPage;
                        if (UIUtilPatchHelper.currentLiquidPageIndex >= UIUtilPatchHelper.cachedLiquids.Count) UIUtilPatchHelper.currentLiquidPageIndex = UIUtilPatchHelper.cachedLiquids.Count - 1;

                        if (Mathf.Abs(scrollInput) > 0.01f)
                        {
                            int totalStates = UIUtilPatchHelper.cachedLiquids.Count - minPage;
                            int offset = (scrollInput > 0f) ? -1 : 1;
                            UIUtilPatchHelper.currentLiquidPageIndex = ((UIUtilPatchHelper.currentLiquidPageIndex - minPage + offset) % totalStates + totalStates) % totalStates + minPage;
                        }

                        __instance.tooltipDescObj.text = UIUtilPatchHelper.BuildModdedText(UIUtilPatchHelper.cachedLiquids, UIUtilPatchHelper.cachedContainerHeader, rawName, UIUtilPatchHelper.currentLiquidPageIndex, UIUtilPatchHelper.activeOverviewModes, UIUtilPatchHelper.useSplitOverview, UIUtilPatchHelper.useLiquidListPage);
                    }
                    else
                    {
                        __instance.tooltipDescObj.text = rawDesc;
                    }
                }

                // 强制几何体更新获取精确尺寸
                __instance.tooltipDescObj.ForceMeshUpdate();

                // 6. 处理非折叠下的原生滚动
                if (!UIUtilPatchHelper.isCollapsibleMode && Mathf.Abs(scrollInput) > 0.01f && __instance.tooltipDescObj.preferredHeight > UIUtilPatchHelper.cachedMaxHeight)
                {
                    UIUtilPatchHelper.scrollOffset += scrollInput * 40f * __instance.uiScale;
                    UIUtilPatchHelper.scrollOffset = Mathf.Clamp(UIUtilPatchHelper.scrollOffset, 0f, Mathf.Max(0f, __instance.tooltipDescObj.preferredHeight - UIUtilPatchHelper.cachedMaxHeight));
                }
                if (UIUtilPatchHelper.isCollapsibleMode) UIUtilPatchHelper.scrollOffset = 0f;

                __instance.tooltipDescObj.rectTransform.anchoredPosition = new Vector2(__instance.tooltipDescObj.rectTransform.anchoredPosition.x, UIUtilPatchHelper.scrollOffset);

                // 7. 自适应背景宽度及高度
                float adjustedBgHeight = (__instance.tooltipDescObj.preferredHeight > UIUtilPatchHelper.cachedMaxHeight) ? UIUtilPatchHelper.cachedMaxHeight : __instance.tooltipDescObj.preferredHeight;
                __instance.hoverNameBackground.sizeDelta = new Vector2(__instance.tooltipNameObj.preferredWidth, __instance.hoverNameBackground.sizeDelta.y);
                __instance.hoverDescriptionBackground.sizeDelta = new Vector2(
                    Mathf.Min(__instance.tooltipDescObj.preferredWidth, __instance.tooltipDescObj.rectTransform.sizeDelta.x),
                    adjustedBgHeight
                );

                // 8. 屏幕边界碰撞检测
                Vector2 vector = new Vector2(
                    Mathf.Max(__instance.hoverNameBackground.position.x + __instance.hoverNameBackground.sizeDelta.x * __instance.uiScale, __instance.hoverDescriptionBackground.position.x + __instance.hoverDescriptionBackground.sizeDelta.x * __instance.uiScale),
                    Mathf.Min(__instance.hoverDescriptionBackground.position.y, __instance.hoverNameBackground.position.y - __instance.hoverDescriptionBackground.sizeDelta.y * __instance.uiScale)
                );
                Vector2 vector2 = new Vector2(
                    Mathf.Clamp(vector.x, 30f * __instance.uiScale, (float)Screen.width - 30f * __instance.uiScale),
                    Mathf.Clamp(vector.y, 30f * __instance.uiScale, (float)Screen.height - 30f * __instance.uiScale)
                );

                if (vector != vector2)
                {
                    Vector2 v = vector2 - vector;
                    __instance.tooltipRect.position += (Vector3)v;
                }
            }
            else
            {
                __instance.tooltipRect.localScale = new Vector3(0.1f, 0.1f, 1f);
            }

            return false; // 跳过原游戏渲染流程，避免出现双重运算导致闪烁
        }
    }

    public static class UIUtilPatchHelper
    {
        public static readonly Regex LiquidHeaderRegex = new Regex(
            @"^[ \t]*([^\n(（]+)[(（]\s*(\d+(?:\.\d+)?)\s*mL[)）][^\n]*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // 统一声明所有跨类访问的状态变量
        public static string lastRawInputDesc = "";
        public static string lastRawInputName = "";
        public static int currentLiquidPageIndex = 0;
        public static float scrollOffset = 0f;
        public static bool isCollapsibleMode = false;
        public static float cachedMaxHeight = 0f;

        public static List<LiquidSection> cachedLiquids = new List<LiquidSection>();
        public static string cachedContainerHeader = "";
        public static List<string> activeOverviewModes = new List<string>();
        public static bool useSplitOverview = false;
        public static bool useLiquidListPage = false;
        public static string lastLiquidStructure = "";

        public class LiquidSection
        {
            public string Name;
            public double AmountML;
            public string headerLine;
            public List<string> bodyLines = new List<string>();
        }

        public static void ParseLiquids(string rawDesc, out List<LiquidSection> liquids, out string containerHeader)
        {
            liquids = new List<LiquidSection>();
            containerHeader = "";
            bool foundFirstLiquid = false;

            string[] lines = rawDesc.Split(new string[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            LiquidSection currentLiquid = null;

            foreach (string line in lines)
            {
                Match m = LiquidHeaderRegex.Match(line);
                if (m.Success)
                {
                    foundFirstLiquid = true;
                    double.TryParse(m.Groups[2].Value, out double amt);
                    currentLiquid = new LiquidSection
                    {
                        headerLine = line,
                        Name = LiquidEffectManager.CleanLiquidName(m.Groups[1].Value),
                        AmountML = amt
                    };
                    liquids.Add(currentLiquid);
                }
                else
                {
                    if (!foundFirstLiquid) containerHeader += line + "\n";
                    else currentLiquid?.bodyLines.Add(line);
                }
            }
        }

        private static int GetLineCount(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int count = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n') count++;
            }
            return count;
        }

        public static void EvaluateOverviewStrategy(string containerName, List<LiquidSection> liquids, out List<string> activeModes, out bool splitOverview, out bool liquidListPage)
        {
            activeModes = new List<string>();
            splitOverview = false;
            liquidListPage = false;

            bool hasOral = false;
            bool hasInj = false;
            bool hasTop = false;

            foreach (var l in liquids)
            {
                if (LiquidEffectManager.TryGetConfig(l.Name, out var data))
                {
                    if (data.Oral != null && (data.Oral.Stats?.Count > 0 || data.Oral.Buffs?.Count > 0 || data.Oral.Specials?.Count > 0)) hasOral = true;
                    if (data.Injection != null && (data.Injection.Stats?.Count > 0 || data.Injection.Buffs?.Count > 0 || data.Injection.Specials?.Count > 0)) hasInj = true;
                    if (data.Topical != null && (data.Topical.Stats?.Count > 0 || data.Topical.Buffs?.Count > 0 || data.Topical.Specials?.Count > 0)) hasTop = true;
                }
            }

            if (hasOral) activeModes.Add("Oral");
            if (hasInj) activeModes.Add("Injection");
            if (hasTop) activeModes.Add("Topical");

            int safeLineLimit = Mathf.Max(12, (int)(Screen.height * 0.70f / 22f));

            if (activeModes.Count > 0)
            {
                string combinedText = BuildFullCombinedText(liquids, cachedContainerHeader, containerName);
                int lines = GetLineCount(combinedText);

                if (lines > safeLineLimit)
                {
                    splitOverview = true;
                }
            }

            if (splitOverview)
            {
                int listLines = GetLineCount(cachedContainerHeader.TrimEnd()) + liquids.Count;
                int footerLines = 3 + activeModes.Count;

                foreach (var mode in activeModes)
                {
                    string merged = CalculateSingleMergedEffect(liquids, containerName, mode, activeModes);
                    int mergedLines = GetLineCount(merged);

                    int totalExpectedLines = listLines + 1 + mergedLines + footerLines;
                    if (totalExpectedLines > safeLineLimit)
                    {
                        liquidListPage = true;
                        break;
                    }
                }
            }
        }

        private static string GetChineseModeName(string mode)
        {
            switch (mode)
            {
                case "Oral": return "口服";
                case "Injection": return "静脉注射";
                case "Topical": return "外敷";
                default: return mode;
            }
        }

        public static string BuildFullCombinedText(List<LiquidSection> liquids, string containerHeader, string containerName)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(containerHeader.TrimEnd());
            foreach (var l in liquids) sb.AppendLine($"  <color=#CCCCCC>{l.headerLine}</color>");

            sb.AppendLine("<color=#555555>──────────────────────────</color>");
            string merged = CalculateAllMergedEffects(liquids, containerName);
            if (!string.IsNullOrEmpty(merged)) sb.AppendLine(merged);

            sb.AppendLine("<color=#555555>──────────────────────────</color>");
            sb.AppendLine("<color=#888888><size=80%> 滚动鼠标滚轮翻页查看详细成分 </size></color>");
            return sb.ToString();
        }

        public static string BuildModdedText(List<LiquidSection> liquids, string containerHeader, string containerName, int pageIndex, List<string> activeModes, bool splitOverview, bool liquidListPage)
        {
            StringBuilder sb = new StringBuilder();

            if (pageIndex < 0)
            {
                if (splitOverview)
                {
                    bool isLiquidListPage = liquidListPage && (pageIndex == -activeModes.Count - 1);

                    if (isLiquidListPage)
                    {
                        sb.AppendLine(containerHeader.TrimEnd());
                        foreach (var l in liquids) sb.AppendLine($"  <color=#CCCCCC>{l.headerLine}</color>");

                        sb.AppendLine("<color=#555555>──────────────────────────</color>");
                        sb.AppendLine($"<color=#888888><size=80%>[ 概览 1 / {activeModes.Count + 1} ] 滚动鼠标滚轮切换预估模式/成分页</size></color>");
                        sb.AppendLine("<color=#666666> ────────────────────</color>");
                        sb.AppendLine($"<color=#00FFFF>▶ 【成分列表】</color>");
                        for (int i = 0; i < activeModes.Count; i++)
                        {
                            string mName = GetChineseModeName(activeModes[i]);
                            sb.AppendLine($"  <color=#888888>【{mName}预估】</color>");
                        }
                    }
                    else
                    {
                        sb.AppendLine(containerHeader.TrimEnd());

                        if (!liquidListPage)
                        {
                            foreach (var l in liquids) sb.AppendLine($"  <color=#CCCCCC>{l.headerLine}</color>");
                        }

                        int overviewIdx = pageIndex + activeModes.Count;
                        if (overviewIdx >= 0 && overviewIdx < activeModes.Count)
                        {
                            string mode = activeModes[overviewIdx];
                            sb.AppendLine("<color=#555555>──────────────────────────</color>");
                            string mergedEffectString = CalculateSingleMergedEffect(liquids, containerName, mode, activeModes);
                            if (!string.IsNullOrEmpty(mergedEffectString)) sb.AppendLine(mergedEffectString);
                        }

                        sb.AppendLine("<color=#555555>──────────────────────────</color>");

                        if (liquidListPage)
                        {
                            int totalPages = activeModes.Count + 1;
                            int currentPageNum = overviewIdx + 2;
                            sb.AppendLine($"<color=#888888><size=80%>[ 概览 {currentPageNum} / {totalPages} ] 滚动鼠标滚轮切换预估模式/成分页</size></color>");
                            sb.AppendLine("<color=#666666> ────────────────────</color>");
                            sb.AppendLine($"  <color=#888888>【成分列表】</color>");
                            for (int i = 0; i < activeModes.Count; i++)
                            {
                                string mName = GetChineseModeName(activeModes[i]);
                                int targetPage = i - activeModes.Count;
                                if (targetPage == pageIndex) sb.AppendLine($"<color=#00FFFF>▶ 【{mName}预估】</color>");
                                else sb.AppendLine($"  <color=#888888>【{mName}预估】</color>");
                            }
                        }
                        else
                        {
                            sb.AppendLine($"<color=#888888><size=80%>[ 概览 {overviewIdx + 1} / {activeModes.Count} ] 滚动鼠标滚轮切换预估模式/成分页</size></color>");
                            sb.AppendLine("<color=#666666> ────────────────────</color>");
                            for (int i = 0; i < activeModes.Count; i++)
                            {
                                string mName = GetChineseModeName(activeModes[i]);
                                int targetPage = i - activeModes.Count;
                                if (targetPage == pageIndex) sb.AppendLine($"<color=#00FFFF>▶ 【{mName}预估】</color>");
                                else sb.AppendLine($"  <color=#888888>【{mName}预估】</color>");
                            }
                        }
                    }
                }
                else
                {
                    sb.Append(BuildFullCombinedText(liquids, containerHeader, containerName));
                }
            }
            else
            {
                sb.AppendLine(containerHeader.TrimEnd());
                var currentLiq = liquids[pageIndex];
                sb.AppendLine($"<color=#FFFFFF><b>{currentLiq.headerLine}</b></color>");
                foreach (string bl in currentLiq.bodyLines) sb.AppendLine(bl);

                sb.AppendLine("<color=#555555>──────────────────────────</color>");
                sb.AppendLine($"<color=#888888><size=80%>[成分第 {pageIndex + 1} / {liquids.Count} 页]</size></color>");
                sb.AppendLine("<color=#666666> ────────────────────</color>");
                for (int i = 0; i < liquids.Count; i++)
                {
                    if (i == pageIndex) sb.AppendLine($"<color=#00FFFF>▶ {liquids[i].headerLine}</color>");
                    else sb.AppendLine($"  <color=#888888>{liquids[i].headerLine}</color>");
                }
            }
            return sb.ToString();
        }

        private static bool EvaluateCondition(double val, string op, double target)
        {
            switch (op)
            {
                case ">": return val > target;
                case ">=": return val >= target;
                case "<": return val < target;
                case "<=": return val <= target;
                case "==":
                case "=": return Math.Abs(val - target) < 0.0001;
                case "!=": return Math.Abs(val - target) > 0.0001;
                default: return false;
            }
        }

        private static string CalculateSingleMergedEffect(List<LiquidSection> liquids, string containerName, string mode, List<string> activeModes)
        {
            if (liquids == null || liquids.Count == 0) return "";
            StringBuilder sb = new StringBuilder();
            var methodStats = new Dictionary<string, double>();

            string methodTitle = "";
            Func<LiquidEntry, UsageMethodEntry> selector = null;

            if (mode == "Oral") { methodTitle = "口服"; selector = data => data.Oral; }
            else if (mode == "Injection") { methodTitle = "静脉注射"; selector = data => data.Injection; }
            else if (mode == "Topical") { methodTitle = "外敷"; selector = data => data.Topical; }

            if (selector == null) return "";

            bool hasData = ProcessUsageMethod(methodTitle, liquids, selector, sb, methodStats, containerName);
            if (!hasData) return "";

            StringBuilder promptSb = new StringBuilder();
            double totalVolume = 0;
            foreach (var liq in liquids) totalVolume += liq.AmountML;

            if (totalVolume > 0)
            {
                double maxPossibleIntake = GetMaxDose(methodTitle, containerName);
                double maxConsumeRatio = Math.Min(totalVolume, maxPossibleIntake) / totalVolume;

                foreach (var liq in liquids)
                {
                    if (LiquidEffectManager.TryGetConfig(liq.Name, out var liquidData) && liquidData.DosePrompts != null)
                    {
                        var usageData = selector(liquidData);
                        if (usageData == null) continue;

                        double intakeML = liq.AmountML * maxConsumeRatio;
                        foreach (var dp in liquidData.DosePrompts)
                        {
                            double currentThreshold = dp.Threshold;
                            string currentMessage = dp.Message;

                            if (dp.Interactions != null)
                            {
                                foreach (var inter in dp.Interactions)
                                {
                                    double condVal = methodStats.ContainsKey(inter.ConditionStat) ? methodStats[inter.ConditionStat] : 0;
                                    if (EvaluateCondition(condVal, inter.Operator, inter.ConditionValue))
                                    {
                                        if (!string.IsNullOrEmpty(inter.Formula))
                                            currentThreshold = MathEvaluator.Evaluate(inter.Formula, currentThreshold, intakeML, methodStats);
                                        if (!string.IsNullOrEmpty(inter.CustomMessage))
                                            currentMessage = inter.CustomMessage;
                                    }
                                }
                            }

                            if (intakeML > 0 && intakeML >= currentThreshold)
                                promptSb.AppendLine($"  <color=#FF5555>• [{liq.Name}]单次预估超标 ({Math.Round(intakeML, 1)}ml): {currentMessage}</color>");
                        }
                    }
                }
            }

            if (LiquidEffectManager.GlobalEffectPrompts != null)
            {
                foreach (var ep in LiquidEffectManager.GlobalEffectPrompts)
                {
                    double maxVal = methodStats.ContainsKey(ep.Field) ? methodStats[ep.Field] : 0;
                    double currentThreshold = ep.Threshold;
                    string currentMessage = ep.Message;

                    if (ep.Interactions != null)
                    {
                        foreach (var inter in ep.Interactions)
                        {
                            double condVal = methodStats.ContainsKey(inter.ConditionStat) ? methodStats[inter.ConditionStat] : 0;
                            if (EvaluateCondition(condVal, inter.Operator, inter.ConditionValue))
                            {
                                if (!string.IsNullOrEmpty(inter.Formula))
                                    currentThreshold = MathEvaluator.Evaluate(inter.Formula, currentThreshold, maxVal, methodStats);
                                if (!string.IsNullOrEmpty(inter.CustomMessage))
                                    currentMessage = inter.CustomMessage;
                            }
                        }
                    }

                    if (maxVal > 0 && maxVal >= currentThreshold)
                        promptSb.AppendLine($"  <color=#FF5555>• {ep.Field}单次最高预估达到 {Math.Round(maxVal, 2)}: {currentMessage}</color>");
                }
            }

            if (promptSb.Length > 0)
            {
                sb.AppendLine("<color=#FF3333>──────────────────────────</color>");
                sb.Append(promptSb.ToString());
            }

            return sb.ToString();
        }

        private static string CalculateAllMergedEffects(List<LiquidSection> liquids, string containerName)
        {
            if (liquids == null || liquids.Count == 0) return "";
            StringBuilder sb = new StringBuilder();
            var oralStats = new Dictionary<string, double>();
            var injectionStats = new Dictionary<string, double>();
            var topicalStats = new Dictionary<string, double>();

            bool hasOral = ProcessUsageMethod("口服", liquids, data => data.Oral, sb, oralStats, containerName);
            bool hasInj = ProcessUsageMethod("静脉注射", liquids, data => data.Injection, sb, injectionStats, containerName);
            bool hasTop = ProcessUsageMethod("外敷", liquids, data => data.Topical, sb, topicalStats, containerName);

            if (!hasOral && !hasInj && !hasTop) return "";

            var grandMaxStats = new Dictionary<string, double>();
            void UpdateMax(Dictionary<string, double> methodStats)
            {
                foreach (var kvp in methodStats)
                {
                    if (!grandMaxStats.ContainsKey(kvp.Key) || kvp.Value > grandMaxStats[kvp.Key])
                        grandMaxStats[kvp.Key] = kvp.Value;
                }
            }
            UpdateMax(oralStats); UpdateMax(injectionStats); UpdateMax(topicalStats);

            StringBuilder promptSb = new StringBuilder();
            double totalVolume = 0;
            foreach (var liq in liquids) totalVolume += liq.AmountML;

            if (totalVolume > 0)
            {
                double maxPossibleIntake = Math.Max(GetMaxDose("口服", containerName), Math.Max(GetMaxDose("静脉注射", containerName), GetMaxDose("外敷", containerName)));
                double maxConsumeRatio = Math.Min(totalVolume, maxPossibleIntake) / totalVolume;

                foreach (var liq in liquids)
                {
                    if (LiquidEffectManager.TryGetConfig(liq.Name, out var liquidData) && liquidData.DosePrompts != null)
                    {
                        double intakeML = liq.AmountML * maxConsumeRatio;
                        foreach (var dp in liquidData.DosePrompts)
                        {
                            double currentThreshold = dp.Threshold;
                            string currentMessage = dp.Message;

                            if (dp.Interactions != null)
                            {
                                foreach (var inter in dp.Interactions)
                                {
                                    double condVal = grandMaxStats.ContainsKey(inter.ConditionStat) ? grandMaxStats[inter.ConditionStat] : 0;
                                    if (EvaluateCondition(condVal, inter.Operator, inter.ConditionValue))
                                    {
                                        if (!string.IsNullOrEmpty(inter.Formula))
                                            currentThreshold = MathEvaluator.Evaluate(inter.Formula, currentThreshold, intakeML, grandMaxStats);
                                        if (!string.IsNullOrEmpty(inter.CustomMessage))
                                            currentMessage = inter.CustomMessage;
                                    }
                                }
                            }

                            if (intakeML > 0 && intakeML >= currentThreshold)
                                promptSb.AppendLine($"  <color=#FF5555>• [{liq.Name}]单次预估超标 ({Math.Round(intakeML, 1)}ml): {currentMessage}</color>");
                        }
                    }
                }
            }

            if (LiquidEffectManager.GlobalEffectPrompts != null)
            {
                foreach (var ep in LiquidEffectManager.GlobalEffectPrompts)
                {
                    double maxVal = grandMaxStats.ContainsKey(ep.Field) ? grandMaxStats[ep.Field] : 0;
                    double currentThreshold = ep.Threshold;
                    string currentMessage = ep.Message;

                    if (ep.Interactions != null)
                    {
                        foreach (var inter in ep.Interactions)
                        {
                            double condVal = grandMaxStats.ContainsKey(inter.ConditionStat) ? grandMaxStats[inter.ConditionStat] : 0;
                            if (EvaluateCondition(condVal, inter.Operator, inter.ConditionValue))
                            {
                                if (!string.IsNullOrEmpty(inter.Formula))
                                    currentThreshold = MathEvaluator.Evaluate(inter.Formula, currentThreshold, maxVal, grandMaxStats);
                                if (!string.IsNullOrEmpty(inter.CustomMessage))
                                    currentMessage = inter.CustomMessage;
                            }
                        }
                    }

                    if (maxVal > 0 && maxVal >= currentThreshold)
                        promptSb.AppendLine($"  <color=#FF5555>• {ep.Field}单次最高预估达到 {Math.Round(maxVal, 2)}: {currentMessage}</color>");
                }
            }

            if (promptSb.Length > 0)
            {
                sb.AppendLine("<color=#FF3333>──────────────────────────</color>");
                sb.Append(promptSb.ToString());
            }

            return sb.ToString();
        }

        private static bool ProcessUsageMethod(string methodTitle, List<LiquidSection> liquids, Func<LiquidEntry, UsageMethodEntry> selector, StringBuilder mainSb, Dictionary<string, double> methodTotalStats, string containerName)
        {
            var totalStats = new Dictionary<string, (double TotalVal, string Color, string Prefix, string Suffix)>();
            var specialEffects = new HashSet<string>();

            var totalBuffs = new Dictionary<string, (double Duration, double TickMultiplier, BuffEntry RefDef, string DisplayName)>();

            double totalVolume = 0;
            foreach (var liq in liquids) totalVolume += liq.AmountML;
            if (totalVolume <= 0) return false;

            double simulatedDose = Math.Min(totalVolume, GetMaxDose(methodTitle, containerName));
            double consumeRatio = simulatedDose / totalVolume;
            bool hasData = false;

            foreach (var liq in liquids)
            {
                if (LiquidEffectManager.TryGetConfig(liq.Name, out var liquidData))
                {
                    UsageMethodEntry useData = selector(liquidData);
                    if (useData == null) continue;

                    double effectiveML = liq.AmountML * consumeRatio;
                    if (effectiveML <= 0) continue;

                    hasData = true;

                    if (useData.Stats != null)
                    {
                        foreach (var stat in useData.Stats)
                        {
                            if (string.IsNullOrEmpty(stat.Field)) continue;
                            if (!totalStats.ContainsKey(stat.Field))
                                totalStats[stat.Field] = (0, stat.ValueColor ?? "#FFFFFF", stat.Prefix ?? "", stat.Suffix ?? "");

                            var current = totalStats[stat.Field];
                            double addedVal = stat.Calculate(effectiveML);
                            totalStats[stat.Field] = (current.TotalVal + addedVal, current.Color, current.Prefix, current.Suffix);

                            if (!methodTotalStats.ContainsKey(stat.Field)) methodTotalStats[stat.Field] = 0;
                            methodTotalStats[stat.Field] += addedVal;
                        }
                    }

                    if (useData.Specials != null) foreach (var sp in useData.Specials) if (!string.IsNullOrEmpty(sp)) specialEffects.Add(sp);

                    if (useData.Buffs != null)
                    {
                        foreach (var buff in useData.Buffs)
                        {
                            if (string.IsNullOrEmpty(buff.Name)) continue;

                            string buffKey = $"{liq.Name}_{buff.Name}";
                            bool isFirstInit = !totalBuffs.ContainsKey(buffKey);

                            string displayName = liquids.Count > 1 ? $"[{liq.Name}] {buff.Name}" : buff.Name;

                            if (isFirstInit) totalBuffs[buffKey] = (0, 0, buff, displayName);

                            var current = totalBuffs[buffKey];
                            double addedDuration = 0, addedTickMulti = 0;
                            string mode = buff.StackMode?.ToUpper() ?? "DURATION";

                            double calcDur = buff.CalculateDuration(effectiveML);

                            if (mode == "DURATION")
                            {
                                addedDuration = calcDur;
                                addedTickMulti = (current.TickMultiplier == 0) ? 1.0 : 0;
                            }
                            else if (mode == "EFFECT")
                            {
                                addedTickMulti = effectiveML;
                                if (isFirstInit) addedDuration = calcDur;
                            }
                            else
                            {
                                addedDuration = calcDur;
                                addedTickMulti = effectiveML;
                            }

                            totalBuffs[buffKey] = (current.Duration + addedDuration, current.TickMultiplier + addedTickMulti, buff, current.DisplayName);
                        }
                    }
                }
            }

            if (!hasData) return false;

            mainSb.AppendLine($"<color=#ffffff>{methodTitle}预估 (单次摄入 {Math.Round(simulatedDose, 1)}ml)：</color>");
            foreach (var kvp in totalStats)
            {
                string valStr = Math.Round(kvp.Value.TotalVal, 2).ToString("0.##");
                string finalPrefix = kvp.Value.Prefix;
                if (string.IsNullOrEmpty(finalPrefix) && kvp.Value.TotalVal > 0) finalPrefix = "+";
                mainSb.AppendLine($"  <color=#b7b7b7>{kvp.Key}</color> <color={kvp.Value.Color}>{finalPrefix}{valStr}{kvp.Value.Suffix}</color>");
            }
            foreach (var kvp in totalBuffs)
            {
                var dur = Math.Round(kvp.Value.Duration, 1).ToString("0.#");
                var buffDef = kvp.Value.RefDef;
                var multiplier = kvp.Value.TickMultiplier;
                var displayName = kvp.Value.DisplayName;

                List<string> tickStrings = new List<string>();
                if (buffDef.TickStats != null)
                {
                    foreach (var tick in buffDef.TickStats)
                    {
                        if (string.IsNullOrEmpty(tick.Field)) continue;

                        double tickVal = tick.Calculate(multiplier);

                        string tValStr = Math.Round(tickVal, 2).ToString("0.##");
                        string tPrefix = string.IsNullOrEmpty(tick.Prefix) && tickVal > 0 ? "+" : tick.Prefix;
                        tickStrings.Add($"{tick.Field} {tPrefix}{tValStr}{tick.Suffix}");
                        if (!methodTotalStats.ContainsKey(tick.Field)) methodTotalStats[tick.Field] = 0;
                        methodTotalStats[tick.Field] += tickVal;
                    }
                }
                string tickJoined = string.Join(" | ", tickStrings);
                string nColor = string.IsNullOrEmpty(buffDef.NameColor) ? "#b7b7b7" : buffDef.NameColor;
                string vColor = string.IsNullOrEmpty(buffDef.ValueColor) ? "#00FF00" : buffDef.ValueColor;
                mainSb.AppendLine($"  <color={nColor}>{displayName}</color> <color={vColor}>持续 {dur}秒 每一秒：{tickJoined}</color>");
            }
            foreach (var sp in specialEffects) mainSb.AppendLine($"  {sp}");
            return true;
        }

        public static double GetMaxDose(string methodTitle, string containerName)
        {
            double maxDose = (methodTitle == "静脉注射") ? 20.0 : 100.0;
            if (LiquidEffectManager.ContainerMaxDoses != null && !string.IsNullOrEmpty(containerName))
            {
                foreach (var cmd in LiquidEffectManager.ContainerMaxDoses)
                {
                    if (cmd.Method == methodTitle && containerName.IndexOf(cmd.Keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                        return cmd.MaxDose;
                }
            }
            return maxDose;
        }
    }

    public class MathEvaluator
    {
        private int pos = -1;
        private int ch;
        private string str;

        public static double Evaluate(string formula, double x, double ml, Dictionary<string, double> contextStats)
        {
            try
            {
                string exp = formula.Replace("x", x.ToString(CultureInfo.InvariantCulture))
                                    .Replace("ML", ml.ToString(CultureInfo.InvariantCulture));

                exp = Regex.Replace(exp, @"\[(.*?)\]", match => {
                    string key = match.Groups[1].Value;
                    double val = contextStats.ContainsKey(key) ? contextStats[key] : 0;
                    return val.ToString(CultureInfo.InvariantCulture);
                });

                exp = exp.Replace(" ", "");
                return new MathEvaluator().Parse(exp);
            }
            catch (Exception ex)
            {
                Debug.LogError("[TooltipFix] Formula Evaluation Error: " + ex.Message + " in formula: " + formula);
                return x;
            }
        }

        private void NextChar() { ch = (++pos < str.Length) ? str[pos] : -1; }
        private bool Eat(int charToEat)
        {
            while (ch == ' ') NextChar();
            if (ch == charToEat) { NextChar(); return true; }
            return false;
        }

        private double Parse(string s)
        {
            str = s; pos = -1; NextChar();
            double x = ParseExpression();
            if (pos < str.Length) throw new Exception("Unexpected character: " + (char)ch);
            return x;
        }

        private double ParseExpression()
        {
            double x = ParseTerm();
            for (; ; )
            {
                if (Eat('+')) x += ParseTerm();
                else if (Eat('-')) x -= ParseTerm();
                else return x;
            }
        }

        private double ParseTerm()
        {
            double x = ParseFactor();
            for (; ; )
            {
                if (Eat('*')) x *= ParseFactor();
                else if (Eat('/')) x /= ParseFactor();
                else return x;
            }
        }

        private double ParseFactor()
        {
            if (Eat('+')) return ParseFactor();
            if (Eat('-')) return -ParseFactor();

            double x;
            int startPos = this.pos;

            if (Eat('('))
            {
                x = ParseExpression();
                Eat(')');
            }
            else if ((ch >= '0' && ch <= '9') || ch == '.')
            {
                while ((ch >= '0' && ch <= '9') || ch == '.') NextChar();
                x = double.Parse(str.Substring(startPos, this.pos - startPos), CultureInfo.InvariantCulture);
            }
            else
            {
                throw new Exception("Unexpected character: " + (char)ch);
            }
            return x;
        }
    }


    [XmlRoot("LiquidEffects")]
    public class LiquidEffectsRoot { [XmlElement("Liquid")] public List<LiquidEntry> Liquids = new List<LiquidEntry>(); [XmlArray("GlobalEffectPrompts"), XmlArrayItem("EffectPrompt")] public List<EffectPromptEntry> GlobalEffectPrompts = new List<EffectPromptEntry>(); [XmlArray("ContainerMaxDoses"), XmlArrayItem("ContainerMaxDose")] public List<ContainerMaxDosesEntry> ContainerMaxDoses = new List<ContainerMaxDosesEntry>(); }
    public class ContainerMaxDosesEntry { [XmlAttribute("Keyword")] public string Keyword; [XmlAttribute("Method")] public string Method; [XmlAttribute("MaxDose")] public double MaxDose; }
    public class LiquidEntry { [XmlAttribute("Name")] public string Name; public UsageMethodEntry Oral; public UsageMethodEntry Injection; public UsageMethodEntry Topical; [XmlArray("DosePrompts"), XmlArrayItem("DosePrompt")] public List<DosePromptEntry> DosePrompts = new List<DosePromptEntry>(); }
    public class UsageMethodEntry { [XmlArray("Stats"), XmlArrayItem("Stat")] public List<StatEntry> Stats = new List<StatEntry>(); [XmlArray("Specials"), XmlArrayItem("Special")] public List<string> Specials = new List<string>(); [XmlArray("Buffs"), XmlArrayItem("Buff")] public List<BuffEntry> Buffs = new List<BuffEntry>(); }

    public class InteractionRule
    {
        [XmlAttribute] public string ConditionStat = "";
        [XmlAttribute] public string Operator = ">";
        [XmlAttribute] public double ConditionValue = 0;

        [XmlAttribute] public string Formula = "";
        [XmlAttribute] public string CustomMessage = "";
    }

    public class StatRule
    {
        [XmlAttribute] public double MinML = 0;
        [XmlAttribute] public double MaxML = 999999;
        [XmlAttribute] public string Mode = "Linear";
        [XmlAttribute] public double BaseValue = 0;
        [XmlAttribute] public double Multiplier = 0;
        [XmlAttribute] public bool UseMaxLimit = false;
        [XmlAttribute] public double MaxLimit = 0;
        [XmlAttribute] public bool UseMinLimit = false;
        [XmlAttribute] public double MinLimit = 0;
    }

    public class StatEntry
    {
        [XmlAttribute] public string Field;
        [XmlAttribute] public double BaseValue = 0;
        [XmlAttribute] public double ValuePerML = 0;
        [XmlAttribute] public string ValueColor = "#FFFFFF";
        [XmlAttribute] public string Prefix = "";
        [XmlAttribute] public string Suffix = "";

        [XmlElement("Rule")]
        public List<StatRule> Rules = new List<StatRule>();

        public double Calculate(double x)
        {
            if (Rules != null && Rules.Count > 0)
            {
                foreach (var rule in Rules)
                {
                    if (x >= rule.MinML && x <= rule.MaxML)
                    {
                        double val = rule.BaseValue;
                        if (rule.Mode != null && rule.Mode.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
                        {
                            double denom = (x == 0) ? 0.0001 : x;
                            val += (rule.Multiplier / denom);
                        }
                        else
                        {
                            val += (rule.Multiplier * x);
                        }

                        if (rule.UseMaxLimit) val = Math.Min(val, rule.MaxLimit);
                        if (rule.UseMinLimit) val = Math.Max(val, rule.MinLimit);
                        return val;
                    }
                }
            }
            return BaseValue + (ValuePerML * x);
        }
    }

    public class BuffEntry
    {
        [XmlAttribute] public string Name;
        [XmlAttribute] public string NameColor = "#b7b7b7";
        [XmlAttribute] public string ValueColor = "#00FF00";
        [XmlAttribute] public string StackMode = "Duration";
        [XmlAttribute] public double BaseDuration = 0;
        [XmlAttribute] public double DurationPerML = 0;

        [XmlElement("DurationRule")]
        public List<StatRule> DurationRules = new List<StatRule>();

        [XmlArray("TickStats"), XmlArrayItem("TickStat")]
        public List<StatEntry> TickStats = new List<StatEntry>();

        public double CalculateDuration(double x)
        {
            if (DurationRules != null && DurationRules.Count > 0)
            {
                foreach (var rule in DurationRules)
                {
                    if (x >= rule.MinML && x <= rule.MaxML)
                    {
                        double val = rule.BaseValue;
                        if (rule.Mode != null && rule.Mode.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
                        {
                            double denom = (x == 0) ? 0.0001 : x;
                            val += (rule.Multiplier / denom);
                        }
                        else
                        {
                            val += (rule.Multiplier * x);
                        }

                        if (rule.UseMaxLimit) val = Math.Min(val, rule.MaxLimit);
                        if (rule.UseMinLimit) val = Math.Max(val, rule.MinLimit);
                        return val;
                    }
                }
            }
            return BaseDuration + (DurationPerML * x);
        }
    }

    public class DosePromptEntry
    {
        [XmlAttribute] public double Threshold;
        [XmlAttribute] public string Message;
        [XmlElement("Interaction")] public List<InteractionRule> Interactions = new List<InteractionRule>();
    }

    public class EffectPromptEntry
    {
        [XmlAttribute] public string Field;
        [XmlAttribute] public double Threshold;
        [XmlAttribute] public string Message;
        [XmlElement("Interaction")] public List<InteractionRule> Interactions = new List<InteractionRule>();
    }

    public static class LiquidEffectManager
    {
        public static Dictionary<string, LiquidEntry> LiquidDict = new Dictionary<string, LiquidEntry>();
        public static List<EffectPromptEntry> GlobalEffectPrompts = new List<EffectPromptEntry>();
        public static List<ContainerMaxDosesEntry> ContainerMaxDoses = new List<ContainerMaxDosesEntry>();

        public static string CleanLiquidName(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return Regex.Replace(input, @"<[^>]*>", "").Replace("\u00A0", " ").Trim();
        }

        public static void LoadConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(TooltipFixPlugin.ConfigPath);
                if (string.IsNullOrEmpty(dir)) return;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string[] files = Directory.GetFiles(dir, "*.xml");
                if (files.Length == 0) return;

                LiquidDict.Clear(); GlobalEffectPrompts.Clear(); ContainerMaxDoses.Clear();
                XmlSerializer serializer = new XmlSerializer(typeof(LiquidEffectsRoot));
                foreach (string file in files)
                {
                    using (StreamReader reader = new StreamReader(file, Encoding.UTF8))
                    {
                        LiquidEffectsRoot root = (LiquidEffectsRoot)serializer.Deserialize(reader);
                        if (root != null)
                        {
                            if (root.Liquids != null) foreach (var liq in root.Liquids) if (!string.IsNullOrEmpty(liq.Name)) LiquidDict[CleanLiquidName(liq.Name)] = liq;
                            if (root.GlobalEffectPrompts != null) GlobalEffectPrompts.AddRange(root.GlobalEffectPrompts);
                            if (root.ContainerMaxDoses != null) ContainerMaxDoses.AddRange(root.ContainerMaxDoses);
                        }
                    }
                }
            }
            catch (Exception e) { Debug.LogError("[TooltipFix] Config Error: " + e.Message); }
        }

        public static bool TryGetConfig(string liquidName, out LiquidEntry data)
        {
            data = null;
            return !string.IsNullOrEmpty(liquidName) && LiquidDict.TryGetValue(CleanLiquidName(liquidName), out data);
        }
    }
}