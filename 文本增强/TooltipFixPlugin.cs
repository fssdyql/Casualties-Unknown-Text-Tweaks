using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace TooltipFixMod
{
    [BepInPlugin("com.yourname.tooltipfix", "Tooltip Display Fix", "1.6.11")]
    public class TooltipFixPlugin : BaseUnityPlugin
    {
        public static string ConfigPath;

        void Awake()
        {
            ConfigPath = Path.Combine(Application.dataPath, "Lang", "LiquidEffects.xml");
            LiquidEffectManager.LoadConfig();
            Harmony.CreateAndPatchAll(typeof(UIUtilPatch));
            Logger.LogInfo($"TooltipFixMod v1.6.11 Loaded - Data Layer Interception Enabled!");
        }
    }

    [HarmonyPatch(typeof(UIUtil), "GetUITooltip")]
    public class UIUtilPatch
    {
        private static readonly Regex LiquidHeaderRegex = new Regex(
            @"^[ \t]*([^\n(（]+)[(（]\s*(\d+(?:\.\d+)?)\s*mL[)）][^\n]*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string lastRawInputDesc = "";
        private static string lastRawInputName = "";
        private static bool lastShiftState = false;
        private static int currentPageIndex = -1;
        private static string lastOutputDesc = "";

        private static List<LiquidSection> cachedLiquids = new List<LiquidSection>();
        private static string cachedContainerHeader = "";

        [HarmonyPostfix]
        public static void Postfix(ref ValueTuple<string, string> __result)
        {
            string rawName = __result.Item1;
            string rawDesc = __result.Item2;

            if (string.IsNullOrEmpty(rawName) || string.IsNullOrEmpty(rawDesc))
            {
                return;
            }

            bool isShiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            float scrollDelta = Input.mouseScrollDelta.y;

            bool descChanged = (rawDesc != lastRawInputDesc || rawName != lastRawInputName);

            if (descChanged)
            {
                lastRawInputDesc = rawDesc;
                lastRawInputName = rawName;
                ParseLiquids(rawDesc, out cachedLiquids, out cachedContainerHeader);
            }

            bool hasLiquidConfig = false;
            foreach (var l in cachedLiquids)
            {
                if (LiquidEffectManager.LiquidDict.ContainsKey(l.Name)) { hasLiquidConfig = true; break; }
            }

            int minPage = hasLiquidConfig ? -1 : 0;

            if (!isShiftHeld)
            {
                currentPageIndex = minPage;
                lastOutputDesc = rawDesc;
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
                        int totalStates = cachedLiquids.Count - minPage;
                        int offset = (scrollDelta > 0f) ? -1 : 1;
                        currentPageIndex = ((currentPageIndex - minPage + offset) % totalStates + totalStates) % totalStates + minPage;
                        pageChanged = true;
                    }

                    if (!descChanged && isShiftHeld == lastShiftState && !pageChanged)
                    {
                        __result.Item2 = lastOutputDesc;
                        return;
                    }

                    lastOutputDesc = BuildModdedText(cachedLiquids, cachedContainerHeader, rawName, currentPageIndex, hasLiquidConfig);
                }
                else
                {
                    lastOutputDesc = rawDesc;
                }
            }

            lastShiftState = isShiftHeld;
            __result.Item2 = lastOutputDesc;
        }

        private class LiquidSection
        {
            public string Name;
            public double AmountML;
            public string headerLine;
            public List<string> bodyLines = new List<string>();
        }

        private static void ParseLiquids(string rawDesc, out List<LiquidSection> liquids, out string containerHeader)
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

        private static string BuildModdedText(List<LiquidSection> liquids, string containerHeader, string containerName, int pageIndex, bool hasLiquidConfig)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(containerHeader.TrimEnd());

            if (pageIndex == -1)
            {
                foreach (var l in liquids) sb.AppendLine($"  <color=#CCCCCC>{l.headerLine}</color>");

                if (hasLiquidConfig)
                {
                    sb.AppendLine("<color=#555555>──────────────────────────</color>");
                    string mergedEffectString = CalculateMergedEffects(liquids, containerName);
                    if (!string.IsNullOrEmpty(mergedEffectString)) sb.AppendLine(mergedEffectString);
                }

                sb.AppendLine("<color=#555555>──────────────────────────</color>");
                sb.AppendLine("<color=#888888><size=80%> 滚动鼠标滚轮翻页查看详细成分 </size></color>");
            }
            else
            {
                var currentLiq = liquids[pageIndex];
                sb.AppendLine($"<color=#FFFFFF><b>{currentLiq.headerLine}</b></color>");
                foreach (string bl in currentLiq.bodyLines) sb.AppendLine(bl);

                sb.AppendLine("<color=#555555>──────────────────────────</color>");
                sb.AppendLine($"<color=#888888><size=80%>[第 {pageIndex + 1} / {liquids.Count} 页]</size></color>");
                sb.AppendLine("<color=#666666> ────────────────────</color>");
                for (int i = 0; i < liquids.Count; i++)
                {
                    if (i == pageIndex) sb.AppendLine($"<color=#00FFFF>▶ {liquids[i].headerLine}</color>");
                    else sb.AppendLine($"  <color=#888888>{liquids[i].headerLine}</color>");
                }
            }
            return sb.ToString();
        }

        private static string CalculateMergedEffects(List<LiquidSection> liquids, string containerName)
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
                            if (intakeML >= dp.Threshold)
                                promptSb.AppendLine($"  <color=#FF5555>• [{liq.Name}]单次预估超标 ({Math.Round(intakeML, 1)}ml): {dp.Message}</color>");
                        }
                    }
                }
            }

            if (LiquidEffectManager.GlobalEffectPrompts != null)
            {
                foreach (var ep in LiquidEffectManager.GlobalEffectPrompts)
                {
                    if (grandMaxStats.TryGetValue(ep.Field, out double maxVal) && maxVal >= ep.Threshold)
                        promptSb.AppendLine($"  <color=#FF5555>• {ep.Field}单次最高预估达到 {Math.Round(maxVal, 2)}: {ep.Message}</color>");
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
            var totalBuffs = new Dictionary<string, (double Duration, double TickMultiplier, BuffEntry RefDef)>();

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
                            double addedVal = stat.ValuePerML * effectiveML;
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
                            if (!totalBuffs.ContainsKey(buff.Name)) totalBuffs[buff.Name] = (buff.BaseDuration, 0, buff);

                            var current = totalBuffs[buff.Name];
                            double addedDuration = 0, addedTickMulti = 0;
                            string mode = buff.StackMode?.ToUpper() ?? "DURATION";

                            if (mode == "DURATION") { addedDuration = buff.DurationPerML * effectiveML; addedTickMulti = (current.TickMultiplier == 0) ? 1.0 : 0; }
                            else if (mode == "EFFECT") { addedTickMulti = effectiveML; }
                            else { addedDuration = buff.DurationPerML * effectiveML; addedTickMulti = effectiveML; }

                            totalBuffs[buff.Name] = (current.Duration + addedDuration, current.TickMultiplier + addedTickMulti, buff);
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

                List<string> tickStrings = new List<string>();
                if (buffDef.TickStats != null)
                {
                    foreach (var tick in buffDef.TickStats)
                    {
                        if (string.IsNullOrEmpty(tick.Field)) continue;
                        double tickVal = tick.ValuePerML * multiplier;
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
                mainSb.AppendLine($"  <color={nColor}>{buffDef.Name}</color> <color={vColor}>持续 {dur}秒 每一秒：{tickJoined}</color>");
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

    [XmlRoot("LiquidEffects")]
    public class LiquidEffectsRoot { [XmlElement("Liquid")] public List<LiquidEntry> Liquids = new List<LiquidEntry>(); [XmlArray("GlobalEffectPrompts"), XmlArrayItem("EffectPrompt")] public List<EffectPromptEntry> GlobalEffectPrompts = new List<EffectPromptEntry>(); [XmlArray("ContainerMaxDoses"), XmlArrayItem("ContainerMaxDose")] public List<ContainerMaxDosesEntry> ContainerMaxDoses = new List<ContainerMaxDosesEntry>(); }
    public class ContainerMaxDosesEntry { [XmlAttribute("Keyword")] public string Keyword; [XmlAttribute("Method")] public string Method; [XmlAttribute("MaxDose")] public double MaxDose; }
    public class LiquidEntry { [XmlAttribute("Name")] public string Name; public UsageMethodEntry Oral; public UsageMethodEntry Injection; public UsageMethodEntry Topical; [XmlArray("DosePrompts"), XmlArrayItem("DosePrompt")] public List<DosePromptEntry> DosePrompts = new List<DosePromptEntry>(); }
    public class UsageMethodEntry { [XmlArray("Stats"), XmlArrayItem("Stat")] public List<StatEntry> Stats = new List<StatEntry>(); [XmlArray("Specials"), XmlArrayItem("Special")] public List<string> Specials = new List<string>(); [XmlArray("Buffs"), XmlArrayItem("Buff")] public List<BuffEntry> Buffs = new List<BuffEntry>(); }
    public class StatEntry { [XmlAttribute] public string Field; [XmlAttribute] public double ValuePerML; [XmlAttribute] public string ValueColor = "#FFFFFF"; [XmlAttribute] public string Prefix = ""; [XmlAttribute] public string Suffix = ""; }
    public class BuffEntry { [XmlAttribute] public string Name; [XmlAttribute] public string NameColor = "#b7b7b7"; [XmlAttribute] public string ValueColor = "#00FF00"; [XmlAttribute] public string StackMode = "Duration"; [XmlAttribute] public double BaseDuration; [XmlAttribute] public double DurationPerML; [XmlArray("TickStats"), XmlArrayItem("TickStat")] public List<StatEntry> TickStats = new List<StatEntry>(); }
    public class DosePromptEntry { [XmlAttribute] public double Threshold; [XmlAttribute] public string Message; }
    public class EffectPromptEntry { [XmlAttribute] public string Field; [XmlAttribute] public double Threshold; [XmlAttribute] public string Message; }

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