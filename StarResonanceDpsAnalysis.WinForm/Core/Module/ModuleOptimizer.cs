using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StarResonanceDpsAnalysis.WinForm.Core.Module
{
    public class ModuleOptimizer
    {
        // Overshoot tolerance: allows exceeding the target level by this many levels before penalties apply (default 1 level)
        private const int OvershootToleranceLevels = 1;
        // Penalty applied for each additional level once the tolerance is exceeded
        private const double OvershootHardPenaltyPerLevel = 50;


        // Target level map: attribute name -> desired level (1-6, 0 or missing means no constraint)
        private readonly Dictionary<string, int> _desiredLevels = new(StringComparer.Ordinal);

        public void SetDesiredLevels(Dictionary<string, int> desiredLevels)
        {
            _desiredLevels.Clear();
            if (desiredLevels == null) return;
            foreach (var kv in desiredLevels)
            {
                // Keep only targets greater than zero
                if (kv.Value > 0) _desiredLevels[kv.Key] = kv.Value;
            }
        }
        // Level closeness: the smaller the gap, the better; gap 0 scores 6, gap 1 scores 5 ... gap >= 6 scores 0
        private double ComputeCloseness(Dictionary<string, int> breakdown)
        {
            if (_desiredLevels.Count == 0) return 0.0;

            double closeness = 0.0;
            const int MaxPerAttr = 6; // Maximum score per attribute

            foreach (var kv in _desiredLevels)
            {
                var name = kv.Key;
                var desired = kv.Value;
                if (desired <= 0) continue;

                if (!breakdown.TryGetValue(name, out var v)) continue;

                int level = ToLevel(v); // Convert current value to its level

                if (level >= desired)
                {
                    // Meeting or exceeding the desired level: award full score without penalties
                    closeness += MaxPerAttr;
                }
                else
                {
                    // Below target: deduct based on the gap (gap 1→5 points, gap 2→4 points, minimum 0)
                    int diff = desired - level;
                    int score = MaxPerAttr - diff;
                    if (score < 0) score = 0;
                    closeness += score;
                }
            }

            return closeness;
        }



        // ModuleOptimizer.cs top-level fields
        private readonly HashSet<string> _priorityAttrs;
        // ====== Configuration / dependencies (provided via constructor) ======
        public enum ModuleCategory { ATTACK, DEFENSE, SUPPORT, ALL }

        public interface IModulePart
        {
            string Name { get; }
            int Id { get; }
            int Value { get; }
        }
        /// <summary>
        /// Ranking mode
        /// </summary>
        public enum SortMode
        {
            /// <summary>
            /// Sort by combat power
            /// </summary>
            ByScore,
            /// <summary>
            /// Sort by attribute breakdown
            /// </summary>
            ByTotalAttr    // Total attribute value
        }

        public interface IModuleInfo
        {
            string Uuid { get; }      // Unique identifier used for de-duplication
            string Name { get; }
            int Quality { get; }
            int ConfigId { get; }
            IReadOnlyList<IModulePart> Parts { get; }
        }

        private readonly IReadOnlyDictionary<int, ModuleCategory> _moduleCategoryMap;
        private readonly IReadOnlyList<int> _attrThresholds;
        private readonly IReadOnlyDictionary<int, int> _basicAttrPowerMap;
        private readonly IReadOnlyDictionary<int, int> _specialAttrPowerMap;
        private readonly IReadOnlyDictionary<int, int> _totalAttrPowerMap;
        private readonly ISet<int> _basicAttrIds;
        private readonly ISet<int> _specialAttrIds;
        private readonly IReadOnlyDictionary<string, string> _attrNameTypeMap; // "basic"/"special"

        // ====== Parameters / runtime state ======
        private readonly Random _rand = new();
        private string? _resultLogFile = null;

        public int LocalSearchIterations { get; set; } = 30;
        public int MaxSolutions { get; set; } = 60;


        // Attribute level weights
        private readonly Dictionary<int, double> _levelWeights = new()
    {
        {1, 1.0},
        {2, 4.0},
        {3, 8.0},
        {4, 12.0},
        {5, 16.0},
        {6, 20.0},
    };

        public ModuleOptimizer(
            IReadOnlyDictionary<int, ModuleCategory> moduleCategoryMap,
            IReadOnlyList<int> attrThresholds,
            IReadOnlyDictionary<int, int> basicAttrPowerMap,
            IReadOnlyDictionary<int, int> specialAttrPowerMap,
            IReadOnlyDictionary<int, int> totalAttrPowerMap,
            ISet<int> basicAttrIds,
            ISet<int> specialAttrIds,
            IReadOnlyDictionary<string, string> attrNameTypeMap,
            IEnumerable<string>? priorityAttrNames = null,
            string? resultLogFile = null
        )
        {
            _moduleCategoryMap = moduleCategoryMap;
            _attrThresholds = attrThresholds;
            _basicAttrPowerMap = basicAttrPowerMap;
            _specialAttrPowerMap = specialAttrPowerMap;
            _totalAttrPowerMap = totalAttrPowerMap;
            _basicAttrIds = basicAttrIds;
            _specialAttrIds = specialAttrIds;
            _attrNameTypeMap = attrNameTypeMap;
            _resultLogFile = resultLogFile;
            _priorityAttrs = priorityAttrNames != null
                ? new HashSet<string>(priorityAttrNames, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
        }

        // ====== Solution definition ======
        public sealed class ModuleSolution
        {
            public List<IModuleInfo> Modules { get; }
            public double Score { get; }
            public Dictionary<string, int> AttrBreakdown { get; }
            public int PriorityLevel { get; }            // Highest level among the selected attributes (used for ordering/comparison)

            // Total attribute value cache
            public int TotalAttrValue { get; }
            public ModuleSolution(List<IModuleInfo> modules, double score,
                  Dictionary<string, int> attrBreakdown, int priorityLevel = 0)
            {
                Modules = modules;
                Score = score;
                AttrBreakdown = attrBreakdown;
                PriorityLevel = priorityLevel;
                TotalAttrValue = attrBreakdown?.Values.Sum() ?? 0;
            }
        }

        // ====== Utility helpers ======
        private void LogResult(string message)
        {
            if (string.IsNullOrEmpty(_resultLogFile)) return;
            try
            {
                File.AppendAllText(_resultLogFile, message + Environment.NewLine);
            }
            catch { /* Ignore logging failures */ }
        }

        private ModuleCategory GetModuleCategory(IModuleInfo module)
            => _moduleCategoryMap.TryGetValue(module.ConfigId, out var cat) ? cat : ModuleCategory.ATTACK;

        // attr_name -> "basic"/"special"
        private string GetAttrTypeByName(string attrName, IEnumerable<IModuleInfo> modules)
        {
            if (_attrNameTypeMap.TryGetValue(attrName, out var t)) return t;

            foreach (var m in modules)
            {
                foreach (var p in m.Parts)
                {
                    if (p.Name == attrName)
                    {
                        if (_basicAttrIds.Contains(p.Id)) return "basic";
                        if (_specialAttrIds.Contains(p.Id)) return "special";
                        return "basic";
                    }
                }
            }
            return "basic";
        }

        private int ToLevel(int value)
        {
            int level = 0;
            for (int i = 0; i < _attrThresholds.Count; i++)
            {
                if (value >= _attrThresholds[i]) level = i + 1;
                else break;
            }
            return level;
        }

        private (int priorityLevel, int combatPower, Dictionary<string, int> breakdown) Evaluate(IReadOnlyList<IModuleInfo> modules)
        {
            var breakdown = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var m in modules)
                foreach (var p in m.Parts)
                    breakdown[p.Name] = breakdown.TryGetValue(p.Name, out var cur) ? cur + p.Value : p.Value;

            // Highest level among selected attributes (0 when none selected)
            int priorityLevel = 0;
            if (_priorityAttrs != null && _priorityAttrs.Count > 0)
            {
                foreach (var name in _priorityAttrs)
                    if (breakdown.TryGetValue(name, out var v))
                        priorityLevel = Math.Max(priorityLevel, ToLevel(v));
            }

            var (combatPower, _) = CalculateCombatPower(modules);  // Reuse the existing combat power calculation
            return (priorityLevel, combatPower, breakdown);
        }

        // ====== Scoring function: prioritize whitelisted attributes ======
        // Priority order: whitelisted attributes with targets >> whitelisted without targets >> non-whitelisted >> combat power
        // Attributes with explicit targets are penalized heavily when they overshoot (never better than hitting the target exactly)
        private (double score, Dictionary<string, int> breakdown, int priorityMaxLevel)
            CalculatePriorityAwareScore(IReadOnlyList<IModuleInfo> modules)
        {
            // 1) Basic statistics
            var (combatPower, breakdown) = CalculateCombatPower(modules);

            // 2) Compute the effective level (1..6) for each attribute
            int priorityMaxLevel = 0;
            var levelByAttr = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var kv in breakdown)
            {
                string attr = kv.Key;
                int lvl = 0;
                for (int i = 0; i < _attrThresholds.Count; i++)
                {
                    if (kv.Value >= _attrThresholds[i]) lvl = i + 1;
                    else break;
                }
                levelByAttr[attr] = lvl;
                if (lvl > priorityMaxLevel) priorityMaxLevel = lvl;
            }

            var whitelist = _priorityAttrs ?? new HashSet<string>(StringComparer.Ordinal);
            var desired = _desiredLevels ?? new Dictionary<string, int>(StringComparer.Ordinal);

            // 3) Three scoring tiers
            double tier1 = 0.0; // Whitelisted with a desired level (highest priority)
            double tier2 = 0.0; // Whitelisted without a desired level
            double tier3 = 0.0; // Not whitelisted

            int globalCloseness = (int)ComputeCloseness(breakdown); // Used only for tie-breaking

            foreach (var (attr, lvl) in levelByAttr)
            {
                _levelWeights.TryGetValue(lvl, out var w); // 1→1, 2→4, …, 6→20

                bool isWhite = whitelist.Contains(attr);
                bool hasTarget = desired.TryGetValue(attr, out var targetLvl);

                if (isWhite && hasTarget)
                {
                    // New logic: always aim for level 6; allow small overshoots without penalties
                    const int AimLevel = 6;

                    if (lvl >= AimLevel)
                    {
                        // Hit level 6: award the highest bonus (ideal outcome)
                        tier1 += 2000.0;
                    }
                    else if (lvl >= targetLvl)
                    {
                        // Meets or exceeds the target but is below level 6
                        int overshoot = lvl - targetLvl; // >= 0

                        if (overshoot <= OvershootToleranceLevels)
                        {
                            // Small overshoot: no penalty (treated as acceptable overflow)
                            tier1 += 2000.0;
                        }
                        else
                        {
                            // Apply penalties only after exceeding the tolerance
                            int hard = overshoot - OvershootToleranceLevels;
                            tier1 += 2000.0 - hard * OvershootHardPenaltyPerLevel;
                        }
                    }
                    else
                    {
                        // Below target: score based on closeness (same weighting as before)
                        int diff = Math.Min(6, targetLvl - lvl);
                        int closenessLocal = 6 - diff;      // 0..6
                        tier1 += closenessLocal * 20.0;     // Moderate weight (preserves original feel)
                    }

                    // Note: attributes with desired levels no longer accumulate weight `w` to avoid rewarding overshoots
                }
                else if (isWhite)
                {
                    // Whitelisted without desired level: higher levels are better (use weight directly)
                    tier2 += w;
                }
                else
                {
                    // Non-whitelisted: lowest priority
                    tier3 += w;
                }
            }

            // 4) Final score combination: Tier1 >> Tier2 >> Tier3 >> closeness / combat power for tie-breaking
            double score =
                  tier1 * 1_000_000_000.0
                + tier2 * 1_000_000.0
                + tier3 * 10_000.0
                + globalCloseness * 1_000.0
                + combatPower;

            return (score, breakdown, priorityMaxLevel);
        }




        // ====== Prefilter modules (top 30 per attribute) ======
        public List<IModuleInfo> PrefilterModules(IReadOnlyList<IModuleInfo> modules)
        {
            var attrModules = new Dictionary<string, List<(IModuleInfo m, int v)>>();
            foreach (var module in modules)
            {
                foreach (var part in module.Parts)
                {
                    if (!attrModules.TryGetValue(part.Name, out var list))
                    {
                        list = new List<(IModuleInfo, int)>();
                        attrModules[part.Name] = list;
                    }
                    list.Add((module, part.Value));
                }
            }

            var candidate = new HashSet<IModuleInfo>();
            foreach (var kv in attrModules)
            {
                var top = kv.Value
                    .OrderByDescending(x => x.v)
                    .Take(30)
                    .Select(x => x.m);
                foreach (var m in top) candidate.Add(m);
            }
            return candidate.ToList();
        }

        // ====== Combat power calculation (threshold power + total attribute power) ======
        public (int power, Dictionary<string, int> attrBreakdown) CalculateCombatPower(IReadOnlyList<IModuleInfo> modules)
        {
            var breakdown = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var m in modules)
            {
                foreach (var p in m.Parts)
                {
                    breakdown[p.Name] = breakdown.TryGetValue(p.Name, out var cur) ? cur + p.Value : p.Value;
                }
            }

            int thresholdPower = 0;
            foreach (var (attrName, attrValue) in breakdown)
            {
                int maxLevel = 0;
                for (int i = 0; i < _attrThresholds.Count; i++)
                {
                    if (attrValue >= _attrThresholds[i]) maxLevel = i + 1;
                    else break;
                }

                var attrType = _attrNameTypeMap.TryGetValue(attrName, out var t) ? t : "basic";
                var map = (attrType == "special") ? _specialAttrPowerMap : _basicAttrPowerMap;
                if (maxLevel > 0 && map.TryGetValue(maxLevel, out var add))
                    thresholdPower += add;
            }

            int totalAttrValue = breakdown.Values.Sum();
            int totalAttrPower = _totalAttrPowerMap.TryGetValue(totalAttrValue, out var tap) ? tap : 0;
            int totalPower = thresholdPower + totalAttrPower;
            return (totalPower, breakdown);
        }

        // ====== Alternative scoring function (skip if combat power alone is sufficient) ======
        public (double score, Dictionary<string, int> attrBreakdown) CalculateSolutionScore(IReadOnlyList<IModuleInfo> modules)
        {
            var breakdown = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var m in modules)
                foreach (var p in m.Parts)
                    breakdown[p.Name] = breakdown.TryGetValue(p.Name, out var cur) ? cur + p.Value : p.Value;

            double highLevelScore = 0.0;
            foreach (var (_, attrValue) in breakdown)
            {
                int level = 0;
                for (int i = 0; i < _attrThresholds.Count; i++)
                {
                    if (attrValue >= _attrThresholds[i]) level = i + 1;
                    else break;
                }
                if (level > 0 && _levelWeights.TryGetValue(level, out var w)) highLevelScore += w;
            }

            int totalValue = breakdown.Values.Sum();

            double totalWaste = 0.0;
            foreach (var (_, value) in breakdown)
            {
                int maxThreshold = 0;
                foreach (var th in _attrThresholds)
                {
                    if (value >= th) maxThreshold = th;
                    else break;
                }
                totalWaste += (value - maxThreshold);
            }

            double score = 0.9 * highLevelScore + 0.05 * totalValue - 0.05 * totalWaste;
            return (score, breakdown);
        }

        // ====== Greedy construction of the initial solution (4 modules) ======
        public ModuleSolution GreedyConstructSolution(IReadOnlyList<IModuleInfo> modules)
        {
            if (modules.Count < 4) return null;

            var current = new List<IModuleInfo> { modules[_rand.Next(modules.Count)] };

            for (int k = 0; k < 3; k++)
            {
                IModuleInfo pick = null;
                double bestScore = double.NegativeInfinity;

                foreach (var m in modules)
                {
                    if (current.Contains(m)) continue;

                    var test = current.Concat(new[] { m }).ToList();
                    var (sc, _, _) = CalculatePriorityAwareScore(test); // Use the unified scoring function

                    if (sc > bestScore)
                    {
                        bestScore = sc;
                        pick = m;
                    }
                }

                if (pick == null) break;
                current.Add(pick);
            }

            // Populate priority level and breakdown for UI/struct consumers
            var (pri, pow, bd) = Evaluate(current); // Reuse the existing Evaluate helper
            return new ModuleSolution(current, pow, bd, pri);
        }

        // ====== Local search (single-point replacement improvements) ======
        public ModuleSolution LocalSearchImprove(ModuleSolution solution, IReadOnlyList<IModuleInfo> allModules)
        {
            if (solution == null) return null;
            var best = new ModuleSolution(
                new List<IModuleInfo>(solution.Modules),
                solution.Score,
                new Dictionary<string, int>(solution.AttrBreakdown),
                solution.PriorityLevel
            );

            // Current best score (using the unified scoring function)
            var (bestScoreUnified, _, _) = CalculatePriorityAwareScore(best.Modules);

            for (int iter = 0; iter < LocalSearchIterations; iter++)
            {
                bool improved = false;

                for (int i = 0; i < best.Modules.Count; i++)
                {
                    int take = Math.Min(20, allModules.Count);
                    var sample = allModules.OrderBy(_ => _rand.Next()).Take(take);

                    foreach (var nm in sample)
                    {
                        if (best.Modules.Contains(nm)) continue;

                        var newModules = new List<IModuleInfo>(best.Modules);
                        newModules[i] = nm;

                        var (sc, bd, _) = CalculatePriorityAwareScore(newModules);
                        if (sc > bestScoreUnified)
                        {
                            var (pri, pow, _) = Evaluate(newModules);
                            best = new ModuleSolution(newModules, pow, bd, pri);
                            bestScoreUnified = sc;
                            improved = true;
                            break;
                        }
                    }
                    if (improved) break;
                }
                if (!improved && iter > LocalSearchIterations / 2) break;
            }
            return best;
        }



        // ====== Main optimization flow ======
        public List<ModuleSolution> OptimizeModules(
            IReadOnlyList<IModuleInfo> modules,
            ModuleCategory category,
            int topN = 40,
                SortMode sortMode = SortMode.ByTotalAttr   // Default to sorting by attribute totals

          )
        {
            // Filter by requested category
            List<IModuleInfo> filtered = (category == ModuleCategory.ALL)
                ? modules.ToList()
                : modules.Where(m => GetModuleCategory(m) == category).ToList();

            if (filtered.Count < 4) return new List<ModuleSolution>();

            // Prefilter the candidates
            var candidates = PrefilterModules(filtered);

            var solutions = new List<ModuleSolution>();
            var seen = new HashSet<string>(); // Deduplicate by module UUID combinations

            int attempts = 0;
            int maxAttempts = MaxSolutions * 20;

            while (solutions.Count < MaxSolutions && attempts < maxAttempts)
            {
                attempts++;

                var init = GreedyConstructSolution(candidates);
                if (init == null) continue;

                var improved = LocalSearchImprove(init, candidates);

                var ids = string.Join("|", improved.Modules.Select(m => m.Uuid).OrderBy(s => s));
                if (seen.Add(ids))
                {
                    solutions.Add(improved);
                }
            }
            // Final sorting based on the requested mode
            List<ModuleSolution> ordered;
            if (sortMode == SortMode.ByTotalAttr)
            {
                // Sort by highest priority attribute level → total attribute value → combat power
                ordered = solutions
                    .OrderByDescending(s => s.PriorityLevel)
                    .ThenByDescending(s => s.TotalAttrValue)
                    .ThenByDescending(s => s.Score)
                    .ToList();
            }
            else // SortMode.ByScore
            {
                // Sort by overall score (combat power) → highest priority attribute level → total attribute value
                ordered = solutions
                    .OrderByDescending(s => s.Score)
                    .ThenByDescending(s => s.PriorityLevel)
                    .ThenByDescending(s => s.TotalAttrValue)
                    .ToList();
            }

            return ordered.Take(topN).ToList();



        }

        // ====== Printing / display helpers ======
        public void PrintSolutionDetails(ModuleSolution solution, int rank)
        {
            if (solution == null) return;

            Console.WriteLine($"\n=== Combination #{rank} ===");
            LogResult($"\n=== Combination #{rank} ===");

            int totalValue = solution.AttrBreakdown.Values.Sum();
            Console.WriteLine($"Total attribute value: {totalValue}");
            LogResult($"Total attribute value: {totalValue}");

            Console.WriteLine($"Combat power: {solution.Score:F2}");
            LogResult($"Combat power: {solution.Score:F2}");

            Console.WriteLine("\nModule list:");
            LogResult("\nModule list:");
            for (int i = 0; i < solution.Modules.Count; i++)
            {
                var m = solution.Modules[i];
                var partsStr = string.Join(", ", m.Parts.Select(p => $"{p.Name}+{p.Value}"));
                Console.WriteLine($"  {i + 1}. {m.Name} (Quality {m.Quality}) - {partsStr}");
                LogResult($"  {i + 1}. {m.Name} (Quality {m.Quality}) - {partsStr}");
            }

            Console.WriteLine("\nAttribute distribution:");
            LogResult("\nAttribute distribution:");
            foreach (var kv in solution.AttrBreakdown.OrderBy(k => k.Key))
            {
                Console.WriteLine($"  {kv.Key}: +{kv.Value}");
                LogResult($"  {kv.Key}: +{kv.Value}");
            }
        }

        public void OptimizeAndDisplay(
            IReadOnlyList<IModuleInfo> modules,
            ModuleCategory category = ModuleCategory.ALL,
            int topN = 40)
        {
            Console.WriteLine(new string('=', 50));
            LogResult(new string('=', 50));

            Console.WriteLine($"Module combination optimization - {category}");
            LogResult($"Module combination optimization - {category}");

            Console.WriteLine(new string('=', 50));
            LogResult(new string('=', 50));

            var optimal = OptimizeModules(modules, category, topN);

            if (optimal.Count == 0)
            {
                Console.WriteLine($"No valid combinations found for {category}");
                LogResult($"No valid combinations found for {category}");
                return;
            }

            Console.WriteLine($"\nFound {optimal.Count} optimal combinations:");
            LogResult($"\nFound {optimal.Count} optimal combinations:");

            for (int i = 0; i < optimal.Count; i++)
                PrintSolutionDetails(optimal[i], i + 1);

            Console.WriteLine($"\n{new string('=', 50)}");
            LogResult($"\n{new string('=', 50)}");

            Console.WriteLine("Statistics:");
            LogResult("Statistics:");

            Console.WriteLine($"Total modules: {modules.Count}");
            LogResult($"Total modules: {modules.Count}");

            int typeCount = modules.Count(m => GetModuleCategory(m) == category);
            Console.WriteLine($"{category} modules: {typeCount}");
            LogResult($"{category} modules: {typeCount}");

            Console.WriteLine($"Highest combat power: {optimal[0].Score:F2}");
            LogResult($"Highest combat power: {optimal[0].Score:F2}");

            Console.WriteLine(new string('=', 50));
            LogResult(new string('=', 50));
        }
    }
}
