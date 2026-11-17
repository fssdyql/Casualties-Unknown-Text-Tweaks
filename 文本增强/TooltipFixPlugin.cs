using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace TooltipFixMod
{
    [BepInPlugin("com.yourname.tooltipfix", "Tooltip Display Fix", "0.3.0")]
    public class TooltipFixPlugin : BaseUnityPlugin
    {
        public static string ConfigPath;

        void Awake()
        {
            ConfigPath = Path.Combine(Application.dataPath, "Lang", "LiquidEffects.xml");
            LiquidEffectManager.LoadConfig();

            Harmony harmony = new Harmony("com.yourname.tooltipfix");
            harmony.PatchAll();

            Logger.LogInfo("TooltipFixMod v0.3.0 Loaded.");
        }
    }

    [HarmonyPatch(typeof(UIUtil), "GetUITooltip")]
    public class UIUtilPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref ValueTuple<string, string> __result)
        {
            UIUtilPatchHelper.MangleTooltip(ref __result);
        }
    }

    [HarmonyPatch(typeof(GlobalDark), "SetTooltip")]
    public class GlobalDarkSetTooltipPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref ValueTuple<string, string> newText)
        {
            UIUtilPatchHelper.MangleTooltip(ref newText);
        }
    }

    [HarmonyPatch(typeof(PlayerCamera), "HandleBlockHover", new Type[] { typeof(Collider2D) })]
    public class HandleBlockHoverPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (WorldGeneration.world == null || !WorldGeneration.world.worldExists || Camera.main == null || GlobalDark.main == null)
                return;

            ushort blockId = WorldGeneration.world.GetBlock(Camera.main.ScreenToWorldPoint(Input.mousePosition));
            if (blockId == 0) return;

            BlockInfo info = WorldGeneration.world.GetBlockInfo(blockId);
            if (info == null) return;

            string extra = BlockTooltipHelper.GetBlockSpecialDesc(blockId, info);
            if (string.IsNullOrEmpty(extra)) return;

            ValueTuple<string, string> cur = GlobalDark.main.tooltipText;
            if (string.IsNullOrEmpty(cur.Item2))
                cur.Item2 = extra;
            else
                cur.Item2 = cur.Item2 + "\n\n" + extra;

            GlobalDark.main.tooltipText = cur;
        }
    }

    public static class BlockTooltipHelper
    {
        public static string GetBlockSpecialDesc(ushort blockId, BlockInfo info)
        {
            List<string> lines = new List<string>();

            switch (info.sleep)
            {
                case Body.SleepQuality.Good:
                    lines.Add("<color=#00FF00>睡眠质量：优质 (恢复速率 1.25x，醒来附带振奋)</color>");
                    break;
                case Body.SleepQuality.Okay:
                    lines.Add("<color=#FFFFFF>睡眠质量：良好 (恢复速率 1.0x)</color>");
                    break;
                case Body.SleepQuality.Mediocre:
                    lines.Add("<color=#FFFF00>睡眠质量：一般 (恢复速率 0.85x，睡醒后意识下降)</color>");
                    break;
                case Body.SleepQuality.Bad:
                    lines.Add("<color=#FF3333>睡眠质量：糟糕 (恢复速率 0.7x，睡醒后长期意识下降)</color>");
                    break;
            }

            if (info.toxicity > 0f)
            {
                lines.Add($"<color=#FF3333>危险 - 辐射源：站立其上每秒辐射 +{info.toxicity:0.#}</color>");
            }

            if (info.hot)
            {
                lines.Add("<color=#FF3333>危险 - 高温灼足：站立每秒双脚皮肤 -20 / 疼痛 +20 (受护甲减免)，体温 +0.1℃/s</color>");
            }

            if (info.slippery)
            {
                lines.Add("<color=#FFFF00>警告 - 表面湿滑：移动推力降至 20% 且失去地面摩擦减速</color>");
            }

            if (info.health <= 1f && blockId != 0)
            {
                lines.Add("<color=#FFFF00>警告 - 极易破碎：踩踏后会立即碎裂坍塌</color>");
            }

            if (info.health >= 400000000f)
            {
                lines.Add("<color=#FF3333>不可破坏：无法被常规工具或爆炸摧毁</color>");
            }

            if (info.metallic)
            {
                lines.Add("<color=#FFFFFF>金属材质：受等离子切割器 10倍 伤害</color>");
            }

            switch (blockId)
            {
                case 3:
                    lines.Add("<color=#00FF00>破坏掉落：50% 掉落 金属废料 (5~20% 耐久)</color>");
                    break;
                case 6:
                case 10:
                    lines.Add("<color=#00FF00>破坏掉落：75% 掉落 金属废料 (50~100% 耐久)</color>");
                    break;
                case 7:
                    lines.Add("<color=#FFFF00>破坏掉落：40% 掉落 玻璃碎片</color>");
                    break;
                case 8:
                case 9:
                    lines.Add("<color=#00FF00>破坏掉落：50% 掉落 塑料块</color>");
                    break;
                case 11:
                case 24:
                    lines.Add("<color=#00FF00>破坏掉落：100% 掉落 木屑</color>");
                    break;
                case 25:
                    lines.Add("<color=#00FF00>破坏掉落：7.5% 掉落 植物纤维 (50~100% 耐久)</color>");
                    break;
                case 27:
                    lines.Add("<color=#FFFFFF>破坏转化：50% 融化为 1 格地下水</color>");
                    break;
                case 34:
                    lines.Add("<color=#00FF00>破坏掉落：100% 掉落 锈铜矿块</color>");
                    break;
                case 35:
                    lines.Add("<color=#00FF00>破坏掉落：100% 掉落 钛铁矿块</color>");
                    break;
            }

            return string.Join("\n", lines);
        }
    }

    public static class UIUtilPatchHelper
    {
        private static readonly Regex LiquidHeaderRegex = new Regex(
            @"^[ \t]*([^\n(（]+)[(（]\s*(\d+(?:\.\d+)?)\s*mL[)）][^\n]*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string lastRawInputDesc = "";
        private static string lastRawInputName = "";
        private static bool lastShiftState = false;
        private static int currentPageIndex = -1;
        private static int lastScrollFrame = -1;

        private static string lastModdedSourceName = "";
        private static string lastModdedSourceDesc = "";
        private static string lastModdedOutput = "";
        private static float bridgeUntilTime = -1f;

        private static List<LiquidSection> cachedLiquids = new List<LiquidSection>();
        private static string cachedContainerHeader = "";
        private static string cachedContainerFooter = "";
        private static List<string> activeOverviewModes = new List<string>();
        private static bool useSplitOverview = false;
        private static bool useLiquidListPage = false;

        private static string lastLiquidStructure = "";

        public class LiquidSection
        {
            public string Name;
            public string ConfigKey;
            public double AmountML;
            public string headerLine;
            public List<string> bodyLines = new List<string>();
        }

        private static bool IsExpandActive()
        {
            return PlayerCamera.alwaysExpandDescriptions || Input.GetKey(KeyBinds.GetBind("expanddesc"));
        }

        public static void MangleTooltip(ref ValueTuple<string, string> tooltip)
        {
            string rawName = tooltip.Item1;
            string rawDesc = tooltip.Item2;

            if (string.IsNullOrEmpty(rawName) || string.IsNullOrEmpty(rawDesc))
            {
                return;
            }

            // 防止同一帧二次传入已被改写过的文本导致缓存损坏与二次翻页
            if (!string.IsNullOrEmpty(lastModdedOutput) && rawName == lastModdedSourceName && rawDesc == lastModdedOutput)
            {
                return;
            }

            bool isShiftHeld = IsExpandActive();
            float scrollDelta = (Time.frameCount != lastScrollFrame) ? Input.mouseScrollDelta.y : 0f;

            bool descChanged = (rawDesc != lastRawInputDesc || rawName != lastRawInputName);

            if (descChanged)
            {
                lastRawInputDesc = rawDesc;
                lastRawInputName = rawName;
                ParseLiquids(rawDesc, out cachedLiquids, out cachedContainerHeader, out cachedContainerFooter);
                EvaluateOverviewStrategy(rawName);

                StringBuilder structSb = new StringBuilder(rawName);
                foreach (var l in cachedLiquids) structSb.Append("_").Append(l.ConfigKey);
                string currentStructure = structSb.ToString();

                if (currentStructure != lastLiquidStructure)
                {
                    lastLiquidStructure = currentStructure;
                    int newMinPage = 0;
                    if (activeOverviewModes.Count > 0)
                    {
                        newMinPage = useSplitOverview ? -activeOverviewModes.Count : -1;
                        if (useLiquidListPage) newMinPage = -activeOverviewModes.Count - 1;
                    }
                    currentPageIndex = newMinPage;
                }
            }

            int minPage = 0;
            if (activeOverviewModes.Count > 0)
            {
                minPage = useSplitOverview ? -activeOverviewModes.Count : -1;
                if (useLiquidListPage) minPage = -activeOverviewModes.Count - 1;
            }

            if (!isShiftHeld)
            {
                currentPageIndex = minPage;

                if (lastShiftState)
                {
                    bridgeUntilTime = Time.unscaledTime + 0.15f;
                }

                bool inBridgeWindow = Time.unscaledTime < bridgeUntilTime;
                if (inBridgeWindow
                    && rawName == lastModdedSourceName && rawDesc == lastModdedSourceDesc
                    && !string.IsNullOrEmpty(lastModdedOutput))
                {
                    tooltip.Item2 = lastModdedOutput;
                }
                else
                {
                    bridgeUntilTime = -1f;
                    lastModdedSourceName = "";
                    lastModdedSourceDesc = "";
                    lastModdedOutput = "";
                }
            }
            else
            {
                if (cachedLiquids.Count > 0)
                {
                    if (currentPageIndex < minPage) currentPageIndex = minPage;
                    if (currentPageIndex >= cachedLiquids.Count) currentPageIndex = cachedLiquids.Count - 1;

                    bool pageChanged = false;
                    if (Mathf.Abs(scrollDelta) > 0.01f)
                    {
                        lastScrollFrame = Time.frameCount;
                        int totalStates = cachedLiquids.Count - minPage;
                        int offset = (scrollDelta > 0f) ? -1 : 1;
                        currentPageIndex = ((currentPageIndex - minPage + offset) % totalStates + totalStates) % totalStates + minPage;
                        pageChanged = true;
                    }

                    bool needRebuild = descChanged || !lastShiftState || pageChanged
                        || string.IsNullOrEmpty(lastModdedOutput);
                    if (needRebuild)
                    {
                        lastModdedOutput = BuildModdedText(cachedLiquids, cachedContainerHeader, cachedContainerFooter, rawName, currentPageIndex);
                        lastModdedSourceName = rawName;
                        lastModdedSourceDesc = rawDesc;
                    }
                    tooltip.Item2 = lastModdedOutput;
                }
                else
                {
                    lastModdedSourceName = "";
                    lastModdedSourceDesc = "";
                    lastModdedOutput = "";
                }
            }

            lastShiftState = isShiftHeld;
        }

        private static void ParseLiquids(string rawDesc, out List<LiquidSection> liquids, out string containerHeader, out string containerFooter)
        {
            liquids = new List<LiquidSection>();
            containerHeader = "";
            containerFooter = "";
            bool foundFirstLiquid = false;
            bool inFooter = false;

            string[] lines = rawDesc.Split(new string[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            LiquidSection currentLiquid = null;

            foreach (string line in lines)
            {
                if (inFooter)
                {
                    containerFooter += line + "\n";
                    continue;
                }

                if (foundFirstLiquid && line.StartsWith("<color=#ffffff><sprite index=0 tint=1>", StringComparison.Ordinal))
                {
                    inFooter = true;
                    containerFooter += line + "\n";
                    continue;
                }

                Match m = LiquidHeaderRegex.Match(line);
                if (m.Success)
                {
                    foundFirstLiquid = true;
                    double.TryParse(m.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double amt);
                    string cleanName = LiquidEffectManager.CleanLiquidName(m.Groups[1].Value);
                    currentLiquid = new LiquidSection
                    {
                        headerLine = line,
                        Name = cleanName,
                        ConfigKey = cleanName,
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

            foreach (var l in liquids)
            {
                string joinedBody = string.Join("\n", l.bodyLines);
                if (l.Name == "过滤水" && joinedBody.Contains("放射性"))
                {
                    l.ConfigKey = "放射水";
                }
                else if (l.Name == "柠檬水" && joinedBody.Contains("尿"))
                {
                    l.ConfigKey = "尿液";
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

        private static void EvaluateOverviewStrategy(string containerName)
        {
            activeOverviewModes.Clear();
            useSplitOverview = false;
            useLiquidListPage = false;

            bool hasOral = false;
            bool hasInj = false;
            bool hasTop = false;

            foreach (var l in cachedLiquids)
            {
                if (LiquidEffectManager.TryGetConfig(l.ConfigKey, out var data))
                {
                    if (data.Oral != null && (data.Oral.Stats?.Count > 0 || data.Oral.Buffs?.Count > 0 || data.Oral.Specials?.Count > 0)) hasOral = true;
                    if (data.Injection != null && (data.Injection.Stats?.Count > 0 || data.Injection.Buffs?.Count > 0 || data.Injection.Specials?.Count > 0)) hasInj = true;
                    if (data.Topical != null && (data.Topical.Stats?.Count > 0 || data.Topical.Buffs?.Count > 0 || data.Topical.Specials?.Count > 0)) hasTop = true;
                }
            }

            if (hasOral) activeOverviewModes.Add("Oral");
            if (hasInj) activeOverviewModes.Add("Injection");
            if (hasTop) activeOverviewModes.Add("Topical");

            int safeLineLimit = Mathf.Max(12, (int)(Screen.height * 0.70f / 22f));

            if (activeOverviewModes.Count > 0)
            {
                string combinedText = BuildFullCombinedText(cachedLiquids, cachedContainerHeader, cachedContainerFooter, containerName);
                int lines = GetLineCount(combinedText);

                if (lines > safeLineLimit)
                {
                    useSplitOverview = true;
                }
            }

            if (useSplitOverview)
            {
                int listLines = GetLineCount(cachedContainerHeader.TrimEnd()) + cachedLiquids.Count;
                int itemFooterLines = string.IsNullOrEmpty(cachedContainerFooter) ? 0 : GetLineCount(cachedContainerFooter.TrimEnd());
                int navFooterLines = 3 + activeOverviewModes.Count;

                foreach (var mode in activeOverviewModes)
                {
                    string merged = CalculateSingleMergedEffect(cachedLiquids, containerName, mode);
                    int mergedLines = GetLineCount(merged);

                    int totalExpectedLines = listLines + 1 + mergedLines + itemFooterLines + navFooterLines;
                    if (totalExpectedLines > safeLineLimit)
                    {
                        useLiquidListPage = true;
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

        private static bool MatchesMethod(DosePromptEntry dp, string methodTitle, string modeEn)
        {
            if (string.IsNullOrEmpty(dp.Method)) return true;
            return dp.Method.Equals(methodTitle, StringComparison.OrdinalIgnoreCase)
                || dp.Method.Equals(modeEn, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildFullCombinedText(List<LiquidSection> liquids, string containerHeader, string containerFooter, string containerName)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(containerHeader.TrimEnd());
            foreach (var l in liquids) sb.AppendLine($"  <color=#CCCCCC>{l.headerLine}</color>");

            sb.AppendLine("<color=#555555>──────────────────────────</color>");
            string merged = CalculateAllMergedEffects(liquids, containerName);
            if (!string.IsNullOrEmpty(merged)) sb.AppendLine(merged.TrimEnd());

            if (!string.IsNullOrEmpty(containerFooter))
            {
                sb.AppendLine("<color=#555555>──────────────────────────</color>");
                sb.AppendLine(containerFooter.TrimEnd());
            }

            sb.AppendLine("<color=#555555>──────────────────────────</color>");
            sb.AppendLine("<color=#888888><size=80%> 滚动鼠标滚轮翻页查看详细成分 </size></color>");
            return sb.ToString();
        }

        private static string BuildModdedText(List<LiquidSection> liquids, string containerHeader, string containerFooter, string containerName, int pageIndex)
        {
            StringBuilder sb = new StringBuilder();

            if (pageIndex < 0)
            {
                if (useSplitOverview)
                {
                    bool isLiquidListPage = useLiquidListPage && (pageIndex == -activeOverviewModes.Count - 1);

                    if (isLiquidListPage)
                    {
                        sb.AppendLine(containerHeader.TrimEnd());
                        foreach (var l in liquids) sb.AppendLine($"  <color=#CCCCCC>{l.headerLine}</color>");

                        if (!string.IsNullOrEmpty(containerFooter))
                        {
                            sb.AppendLine("<color=#555555>──────────────────────────</color>");
                            sb.AppendLine(containerFooter.TrimEnd());
                        }

                        sb.AppendLine("<color=#555555>──────────────────────────</color>");
                        sb.AppendLine($"<color=#888888><size=80%>[ 概览 1 / {activeOverviewModes.Count + 1} ] 滚动鼠标滚轮切换预估模式/成分页</size></color>");
                        sb.AppendLine("<color=#666666> ────────────────────</color>");
                        sb.AppendLine("<color=#00FFFF>▶ 【成分列表】</color>");
                        for (int i = 0; i < activeOverviewModes.Count; i++)
                        {
                            string mName = GetChineseModeName(activeOverviewModes[i]);
                            sb.AppendLine($"  <color=#888888>【{mName}预估】</color>");
                        }
                    }
                    else
                    {
                        sb.AppendLine(containerHeader.TrimEnd());

                        if (!useLiquidListPage)
                        {
                            foreach (var l in liquids) sb.AppendLine($"  <color=#CCCCCC>{l.headerLine}</color>");
                        }

                        int overviewIdx = pageIndex + activeOverviewModes.Count;
                        if (overviewIdx >= 0 && overviewIdx < activeOverviewModes.Count)
                        {
                            string mode = activeOverviewModes[overviewIdx];
                            sb.AppendLine("<color=#555555>──────────────────────────</color>");
                            string mergedEffectString = CalculateSingleMergedEffect(liquids, containerName, mode);
                            if (!string.IsNullOrEmpty(mergedEffectString)) sb.AppendLine(mergedEffectString.TrimEnd());
                        }

                        if (!string.IsNullOrEmpty(containerFooter))
                        {
                            sb.AppendLine("<color=#555555>──────────────────────────</color>");
                            sb.AppendLine(containerFooter.TrimEnd());
                        }

                        sb.AppendLine("<color=#555555>──────────────────────────</color>");

                        if (useLiquidListPage)
                        {
                            int totalPages = activeOverviewModes.Count + 1;
                            int currentPageNum = overviewIdx + 2;
                            sb.AppendLine($"<color=#888888><size=80%>[ 概览 {currentPageNum} / {totalPages} ] 滚动鼠标滚轮切换预估模式/成分页</size></color>");
                            sb.AppendLine("<color=#666666> ────────────────────</color>");
                            sb.AppendLine("  <color=#888888>【成分列表】</color>");
                            for (int i = 0; i < activeOverviewModes.Count; i++)
                            {
                                string mName = GetChineseModeName(activeOverviewModes[i]);
                                int targetPage = i - activeOverviewModes.Count;
                                if (targetPage == pageIndex) sb.AppendLine($"<color=#00FFFF>▶ 【{mName}预估】</color>");
                                else sb.AppendLine($"  <color=#888888>【{mName}预估】</color>");
                            }
                        }
                        else
                        {
                            sb.AppendLine($"<color=#888888><size=80%>[ 概览 {overviewIdx + 1} / {activeOverviewModes.Count} ] 滚动鼠标滚轮切换预估模式/成分页</size></color>");
                            sb.AppendLine("<color=#666666> ────────────────────</color>");
                            for (int i = 0; i < activeOverviewModes.Count; i++)
                            {
                                string mName = GetChineseModeName(activeOverviewModes[i]);
                                int targetPage = i - activeOverviewModes.Count;
                                if (targetPage == pageIndex) sb.AppendLine($"<color=#00FFFF>▶ 【{mName}预估】</color>");
                                else sb.AppendLine($"  <color=#888888>【{mName}预估】</color>");
                            }
                        }
                    }
                }
                else
                {
                    sb.Append(BuildFullCombinedText(liquids, containerHeader, containerFooter, containerName));
                }
            }
            else
            {
                sb.AppendLine(containerHeader.TrimEnd());
                var currentLiq = liquids[pageIndex];
                sb.AppendLine($"<color=#FFFFFF><b>{currentLiq.headerLine}</b></color>");
                foreach (string bl in currentLiq.bodyLines) sb.AppendLine(bl);

                if (!string.IsNullOrEmpty(containerFooter))
                {
                    sb.AppendLine("<color=#555555>──────────────────────────</color>");
                    sb.AppendLine(containerFooter.TrimEnd());
                }

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

        private static string CalculateSingleMergedEffect(List<LiquidSection> liquids, string containerName, string mode)
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
                    if (LiquidEffectManager.TryGetConfig(liq.ConfigKey, out var liquidData) && liquidData.DosePrompts != null)
                    {
                        var usageData = selector(liquidData);
                        if (usageData == null) continue;

                        double intakeML = liq.AmountML * maxConsumeRatio;
                        foreach (var dp in liquidData.DosePrompts)
                        {
                            if (!MatchesMethod(dp, methodTitle, mode)) continue;

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
                                promptSb.AppendLine($"  <color=#FF5555>• [{liq.ConfigKey}]单次预估超标 ({Math.Round(intakeML, 1)}ml): {currentMessage}</color>");
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
                double oralRatio = hasOral ? Math.Min(totalVolume, GetMaxDose("口服", containerName)) / totalVolume : 0;
                double injRatio = hasInj ? Math.Min(totalVolume, GetMaxDose("静脉注射", containerName)) / totalVolume : 0;
                double topRatio = hasTop ? Math.Min(totalVolume, GetMaxDose("外敷", containerName)) / totalVolume : 0;
                double grandRatio = Math.Min(totalVolume, Math.Max(GetMaxDose("口服", containerName), Math.Max(GetMaxDose("静脉注射", containerName), GetMaxDose("外敷", containerName)))) / totalVolume;

                foreach (var liq in liquids)
                {
                    if (LiquidEffectManager.TryGetConfig(liq.ConfigKey, out var liquidData) && liquidData.DosePrompts != null)
                    {
                        foreach (var dp in liquidData.DosePrompts)
                        {
                            void CheckPromptForRoute(double routeRatio, Dictionary<string, double> routeStats)
                            {
                                double intakeML = liq.AmountML * routeRatio;
                                double currentThreshold = dp.Threshold;
                                string currentMessage = dp.Message;

                                if (dp.Interactions != null)
                                {
                                    foreach (var inter in dp.Interactions)
                                    {
                                        double condVal = routeStats.ContainsKey(inter.ConditionStat) ? routeStats[inter.ConditionStat] : 0;
                                        if (EvaluateCondition(condVal, inter.Operator, inter.ConditionValue))
                                        {
                                            if (!string.IsNullOrEmpty(inter.Formula))
                                                currentThreshold = MathEvaluator.Evaluate(inter.Formula, currentThreshold, intakeML, routeStats);
                                            if (!string.IsNullOrEmpty(inter.CustomMessage))
                                                currentMessage = inter.CustomMessage;
                                        }
                                    }
                                }

                                if (intakeML > 0 && intakeML >= currentThreshold)
                                    promptSb.AppendLine($"  <color=#FF5555>• [{liq.ConfigKey}]单次预估超标 ({Math.Round(intakeML, 1)}ml): {currentMessage}</color>");
                            }

                            if (string.IsNullOrEmpty(dp.Method))
                            {
                                CheckPromptForRoute(grandRatio, grandMaxStats);
                            }
                            else
                            {
                                if (hasOral && liquidData.Oral != null && MatchesMethod(dp, "口服", "Oral")) CheckPromptForRoute(oralRatio, oralStats);
                                if (hasInj && liquidData.Injection != null && MatchesMethod(dp, "静脉注射", "Injection")) CheckPromptForRoute(injRatio, injectionStats);
                                if (hasTop && liquidData.Topical != null && MatchesMethod(dp, "外敷", "Topical")) CheckPromptForRoute(topRatio, topicalStats);
                            }
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
                if (LiquidEffectManager.TryGetConfig(liq.ConfigKey, out var liquidData))
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

                            string buffKey = $"{liq.ConfigKey}_{buff.Name}";
                            bool isFirstInit = !totalBuffs.ContainsKey(buffKey);

                            string displayName = liquids.Count > 1 ? $"[{liq.ConfigKey}] {buff.Name}" : buff.Name;

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

        private static double GetMaxDose(string methodTitle, string containerName)
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

                exp = Regex.Replace(exp, @"\[(.*?)\]", match =>
                {
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
        [XmlAttribute] public string Method = "";
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