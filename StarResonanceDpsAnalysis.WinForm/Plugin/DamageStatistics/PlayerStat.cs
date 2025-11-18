using System;
using System.Collections.Concurrent;
using System.Timers;
using System.Xml.Linq;

using StarResonanceDpsAnalysis.Core.Extends.Data;
using StarResonanceDpsAnalysis.Core.Models;
using StarResonanceDpsAnalysis.WinForm.Core;

using static StarResonanceDpsAnalysis.WinForm.Plugin.DamageStatistics.PlayerDataManager;

namespace StarResonanceDpsAnalysis.WinForm.Plugin.DamageStatistics
{
    /// <summary>
    /// General-purpose statistic accumulator used for damage or healing totals, count tracking,
    /// realtime windows, and overall DPS/HPS computation.
    /// Design notes:
    /// 1. Write path: use <see cref="AddRecord"/> exclusively so totals/counters/extrema/realtime windows/time ranges advance together.
    /// 2. Realtime: maintains a fixed-width window (default 1s) to produce RealtimeValue for instantaneous DPS/HPS displays.
    /// 3. Averages: derive total averages from the first/last record timestamps instead of relying on external clocks.
    /// 4. Threading: this type is not internally synchronized; callers should serialize access if writing from multiple threads.
    /// </summary>
    public class StatisticData
    {
        #region Constants
        // Lock object for realtime window updates
        private readonly object _realtimeLock = new();

        /// <summary>
        /// Realtime statistics window (seconds) used to calculate live values and peaks.
        /// Note: shorter windows are more responsive but noisier; longer windows are smoother but introduce lag.
        /// </summary>
        private const double RealtimeWindowSeconds = 1.0;

        #endregion

        #region Static members

        /// <summary>
        /// Global player data manager (kept consistent with the original code).
        /// Handles cross-player aggregation, periodic refreshes, snapshots, and battle timers.
        /// </summary>
        public static readonly PlayerDataManager _manager = new PlayerDataManager();

        /// <summary>
        /// Global NPC manager: tracks NPC taken damage and attacker rankings.
        /// </summary>
        public static readonly NpcManager _npcManager = new NpcManager(_manager);
        #endregion

        #region Aggregate values (read-only, internally incremented)

        /// <summary>
        /// Miss count (number of misses)
        /// - Counts how many times the skill registers as a miss in taken-damage tracking
        /// - Misses do not add to damage totals; they are counted for hit-rate analysis
        /// </summary>
        public int CountMiss { get; private set; }

        /// <summary>
        /// Kill count (number of target deaths)
        /// - Tracks how many times the skill causes a target to die in taken-damage statistics
        /// - Useful for highlighting how often the skill delivered killing blows
        /// - Deaths are a result of taken damage; the damage still accumulates normally
        /// </summary>
        public int CountDead { get; private set; }





        /// <summary>
        /// Cause-lucky total damage
        /// - Sum of hits that were both lucky (isLucky=true) and caused by a “cause lucky” effect (isCauseLucky=true)
        /// - Allows us to distinguish between normal lucky damage and cause-triggered lucky damage
        /// </summary>
        public ulong CauseLucky { get; private set; }

        /// <summary>
        /// Cause-lucky hit count
        /// - Number of hits that triggered the cause-lucky effect
        /// - Compare with CountLucky to see how many lucky hits stemmed from cause triggers
        /// </summary>
        public int CountCauseLucky { get; private set; }


        /// <summary>Total amount of non-critical, non-lucky hits.</summary>
        public ulong Normal { get; private set; }

        /// <summary>Total amount from critical hits.</summary>
        public ulong Critical { get; private set; }

        /// <summary>Total amount from lucky hits.</summary>
        public ulong Lucky { get; private set; }

        public ulong LuckyAndCritical { get; private set; }      // Combined lucky damage (= Lucky + CritLucky)


        /// <summary>Total amount from hits that were both critical and lucky.</summary>
        public ulong CritLucky { get; private set; }

        /// <summary>Total HP reduction (damage-side only). Used to log real HP loss when tracking taken damage, which may differ from <see cref="Total"/>.</summary>
        public ulong HpLessen { get; private set; }

        /// <summary>Total amount across all hits (baseline for DPS/HPS calculations).</summary>
        public ulong Total { get; private set; }

        /// <summary>Maximum single-hit value (updated at write time).</summary>
        public ulong MaxSingleHit { get; private set; }

        /// <summary>Minimum single-hit value (non-zero; initialized to <see cref="ulong.MaxValue"/> so Min works correctly).</summary>
        public ulong MinSingleHit { get; private set; } = ulong.MaxValue;

        #endregion

        #region Count statistics (read-only, internally incremented)

        /// <summary>Number of normal hits.</summary>
        public int CountNormal { get; private set; }

        /// <summary>Number of critical hits.</summary>
        public int CountCritical { get; private set; }

        /// <summary>Number of lucky hits.</summary>
        public int CountLucky { get; private set; }

        /// <summary>Total hit count (sum of normal, critical, and lucky hits).</summary>
        public int CountTotal { get; private set; }

        #endregion

        #region Realtime window tracking

        /// <summary>
        /// Recent records inside the realtime window (used for instantaneous DPS/HPS).
        /// Each tuple stores (timestamp, value).
        /// </summary>
        private readonly List<(DateTime Time, ulong Value)> _realtimeWindow = new();

        /// <summary>Realtime total inside the window (e.g., for instantaneous DPS/HPS display).</summary>
        public ulong RealtimeValue { get; private set; }

        /// <summary>Historical peak within the window (for the “highest instantaneous value” display).</summary>
        public ulong RealtimeMax { get; private set; }

        #endregion

        #region Time range tracking (for overall per-second averages)
        // Set on first AddRecord invocation
        private DateTime? _startTime;

        // Timestamp of the most recent AddRecord
        private DateTime? _endTime;

        /// <summary>Read-only accessor for the last record time.</summary>
        public DateTime? LastRecordTime => _endTime;

        #endregion



        #region Public API
        public void RegisterMiss() { CountMiss++; }
        public void RegisterKill() { CountDead++; }



        /// <summary>
        /// Adds a new statistic entry (damage or healing). This is the single write path and updates all derived metrics.
        /// </summary>
        /// <param name="value">Recorded amount (damage or healing). Zero values do not affect the minimum.</param>
        /// <param name="isCrit">Flag indicating a critical hit; affects <see cref="Critical"/> or <see cref="CritLucky"/> totals and counters.</param>
        /// <param name="isLucky">Flag indicating a lucky hit; affects <see cref="Lucky"/> or <see cref="CritLucky"/> totals and counters.</param>
        /// <param name="hpLessenValue">
        /// HP reduction (for damage/taken scenarios).
        /// Distinguishes real HP loss from <see cref="Total"/> when tracking taken damage (e.g., overkill, shields, mitigation).
        /// </param>
        public void AddRecord(ulong value, bool isCrit, bool isLucky, ulong hpLessenValue = 0, bool isCauseLucky = false) // ★ New parameter: flags whether the hit was cause-lucky
        {
            var now = DateTime.Now;

            // Preserve the existing total/counter/extrema logic
            if (isCrit && isLucky)
            {
                CritLucky += value;
                LuckyAndCritical += value;
            }
            else if (isCrit) Critical += value;
            else if (isLucky)
            {
                Lucky += value;
                LuckyAndCritical += value;
            }
            else Normal += value;


            Total += value;
            HpLessen += hpLessenValue;

            if (isCrit) CountCritical++;
            if (isLucky) CountLucky++;
            if (!isCrit && !isLucky) CountNormal++;
            CountTotal++;

            if (value > 0)
            {
                if (value > MaxSingleHit) MaxSingleHit = value;
                if (value < MinSingleHit) MinSingleHit = value;
            }




            if (isLucky && isCauseLucky)
            {
                CauseLucky += value;
                CountCauseLucky++;
            }


            // —— Only this section needs locking ——
            lock (_realtimeLock)
            {
                _realtimeWindow.Add((now, value));
            }

            _startTime ??= now;
            _endTime = now;
        }

        // Inside PlayerData.AddTakenDamage(...)



        /// <summary>
        /// Refreshes realtime statistics by removing data outside the window and recomputing current and peak values.
        /// Recommend calling periodically from an external timer (1s tick) or on key render points.
        /// </summary>
        public void UpdateRealtimeStats()
        {
            var now = DateTime.Now;

            List<(DateTime Time, ulong Value)> snapshot = null;

            // 1) 锁内：剔除过期 + 复制快照
            lock (_realtimeLock)
            {
                _realtimeWindow.RemoveAll(e => (now - e.Time).TotalSeconds > RealtimeWindowSeconds);

                if (_realtimeWindow.Count > 0)
                    snapshot = new List<(DateTime, ulong)>(_realtimeWindow);
            }

            // 2) 锁外：计算实时累计
            ulong sum = 0;
            if (snapshot != null)
            {
                for (int i = 0; i < snapshot.Count; i++)
                    sum += snapshot[i].Value;
            }

            RealtimeValue = sum;
            if (RealtimeValue > RealtimeMax) RealtimeMax = RealtimeValue;
        }



        /// <summary>
        /// Gets the overall per-second average (Total ÷ total duration) for DPS or HPS.
        /// Duration comes from the difference between the first and last record timestamps.
        /// </summary>
        /// <returns>Returns 0 when no valid time range exists.</returns>
        public double GetTotalPerSecond()
        {
            if (_startTime == null || _endTime == null || _startTime == _endTime) return 0;
            var seconds = (_endTime.Value - _startTime.Value).TotalSeconds;
            return seconds > 0 ? Total / seconds : 0;
        }

        /// <summary>Average value per hit (Total ÷ CountTotal).</summary>
        /// <returns>Returns 0 when <see cref="CountTotal"/> is 0.</returns>
        public double GetAveragePerHit() => CountTotal > 0 ? (double)Total / CountTotal : 0.0;

        /// <summary>Critical rate as a percentage (0–100, rounded to two decimals).</summary>
        /// <returns>Returns 0 when <see cref="CountTotal"/> is 0.</returns>
        public double GetCritRate() =>
            CountTotal > 0 ? Math.Round((double)CountCritical / CountTotal * 100.0, 2) : 0.0;

        /// <summary>Lucky rate as a percentage (0–100, rounded to two decimals).</summary>
        /// <returns>Returns 0 when <see cref="CountTotal"/> is 0.</returns>
        public double GetLuckyRate() =>
            CountTotal > 0 ? Math.Round((double)CountLucky / CountTotal * 100.0, 2) : 0.0;


        /// <summary>
        /// Resets all statistics and state.
        /// Note: only clears the data; external containers (such as PlayerData references) remain intact.
        /// </summary>
        public void Reset()
        {
            Normal = Critical = Lucky = CritLucky = HpLessen = Total = 0;
            CountNormal = CountCritical = CountLucky = CountTotal = 0;

            MaxSingleHit = 0;
            MinSingleHit = ulong.MaxValue;

            _realtimeWindow.Clear();
            _startTime = _endTime = null;

            RealtimeValue = RealtimeMax = 0;
        }

        #endregion
    }

    #region Player Data Manager
    // ------------------------------------------------------------
    // # Section: Skill metadata (static information such as name/icon/DoT flags)
    // ------------------------------------------------------------

    /// <summary>Skill metadata (static). Used by UI, exports, and summary statistics.</summary>
    public sealed class SkillMeta
    {
        /// <summary>Skill ID.</summary>
        public long Id { get; init; }

        /// <summary>Skill name (can be injected from resources/protocol).</summary>
        public string Name { get; init; } = "Unknown Skill";

        /// <summary>School/element line (optional).</summary>
        public string School { get; init; } = "";

        /// <summary>Icon path (optional).</summary>
        public string IconPath { get; init; } = "";

        // 新增
        /// <summary>Skill type (damage/healing/unknown, from <see cref="Core.SkillType"/>).</summary>
        public Core.SkillType Type { get; init; } =
            Core.SkillType.Unknown;

        /// <summary>Element type (from <see cref="Core.ElementType"/>).</summary>
        public Core.ElementType Element { get; init; } =
            Core.ElementType.Unknown;

        /// <summary>Flags whether the skill is a DoT (optional).</summary>
        public bool IsDoT { get; init; }

        /// <summary>Flags whether the skill is an ultimate/finisher (optional).</summary>
        public bool IsUltimate { get; init; }
    }

    /// <summary>
    /// Skill registry (process-level cache): look up metadata by ID and update while parsing telemetry.
    /// Note: the cache is a static dictionary with no internal locking; callers must synchronize concurrent writes.
    /// </summary>
    public static class SkillBook
    {
        private static readonly Dictionary<long, SkillMeta> _metas = new();

        /// <summary>
        /// Overwrite or add a full skill metadata entry.
        /// </summary>
        /// <param name="meta">Complete metadata payload.</param>
        public static void SetOrUpdate(SkillMeta meta) => _metas[meta.Id] = meta;

        /// <summary>
        /// Update or set just the skill name (fast path).
        /// </summary>
        /// <param name="id">Skill ID.</param>
        /// <param name="name">Skill name.</param>
        public static void SetName(long id, string name)
        {
            if (_metas.TryGetValue(id, out var m))
                _metas[id] = new SkillMeta
                {
                    Id = id,
                    Name = name,
                    School = m.School,
                    IconPath = m.IconPath,
                    IsDoT = m.IsDoT,
                    IsUltimate = m.IsUltimate
                };
            else
                _metas[id] = new SkillMeta { Id = id, Name = name };
        }

        /// <summary>
        /// Retrieve skill metadata; returns a placeholder without writing back when missing.
        /// </summary>
        /// <param name="id">Skill ID.</param>
        /// <returns>Returns placeholder “Skill[id]” when not cached.</returns>
        public static SkillMeta Get(long id) =>
            _metas.TryGetValue(id, out var m) ? m : new SkillMeta { Id = id, Name = $"Skill[{id}]" };

        /// <summary>
        /// Try to get skill metadata.
        /// </summary>
        /// <param name="id">Skill ID.</param>
        /// <param name="meta">Output: metadata when present in the cache.</param>
        /// <returns>True when the cache contains the ID.</returns>
        public static bool TryGet(long id, out SkillMeta meta) => _metas.TryGetValue(id, out meta);
    }

    // ------------------------------------------------------------
    // # Section: Skill summary DTOs (for UI/export usage)
    // ------------------------------------------------------------

    /// <summary>
    /// Skill summary for a single player (statistics merged with metadata).
    /// Used for list views, exports, charts, etc.; contains no write logic.
    /// </summary>
    public sealed class SkillSummary
    {
        /// <summary>Skill ID (unique identifier for joining with database records).</summary>
        public long SkillId { get; init; }

        /// <summary>Skill name (defaults to “Unknown Skill”).</summary>
        public string SkillName { get; init; } = "Unknown Skill";

        /// <summary>Total damage or healing for the skill (depends on the source collection).</summary>
        public ulong Total { get; init; }

        /// <summary>Number of hits (times damaged/healed/dealt).</summary>
        public int HitCount { get; init; }

        /// <summary>Average value per hit.</summary>
        public double AvgPerHit { get; init; }

        /// <summary>Critical rate (0–100 or 0–1 depending on generator; this implementation uses percentage 0–100).</summary>
        public double CritRate { get; init; }

        /// <summary>Lucky rate (0–100).</summary>
        public double LuckyRate { get; init; }

        /// <summary>Highest single-hit value.</summary>
        public ulong MaxSingleHit { get; init; }

        /// <summary>Lowest single-hit value (0 when no records).</summary>
        public ulong MinSingleHit { get; init; }

        /// <summary>Realtime window total (current instantaneous value for the skill).</summary>
        public ulong RealtimeValue { get; init; }

        /// <summary>Realtime window peak value.</summary>
        public ulong RealtimeMax { get; init; }

        /// <summary>Per-second average for this skill (using its own time range).</summary>
        public double TotalDps { get; init; }

        /// <summary>Timestamp of the last hit for this skill.</summary>
        public DateTime? LastTime { get; init; }

        /// <summary>Historical share of total (0–1). Multiply by 100 and round for UI percentages.</summary>
        public double ShareOfTotal { get; init; }

        public ulong LuckyDamage { get; init; }        // Lucky damage = Lucky + CritLucky
        public ulong CritLuckyDamage { get; init; }    // Pure “critical + lucky” portion
        public ulong CauseLuckyDamage { get; init; }   // Cause-lucky component only
        public int CountLucky { get; init; }         // Number of lucky hits
    }

    /// <summary>Team-wide aggregated skill summary (across all players).</summary>
    public sealed class TeamSkillSummary
    {
        /// <summary>Skill ID.</summary>
        public long SkillId { get; init; }

        /// <summary>Skill name.</summary>
        public string SkillName { get; init; } = "Unknown Skill";

        /// <summary>Team total for the skill (damage sum).</summary>
        public ulong Total { get; init; }

        /// <summary>Aggregate hit count across the team.</summary>
        public int HitCount { get; init; }
    }

    // ------------------------------------------------------------
    // # Section: Player data (damage/healing/taken and per-skill grouping)
    // ------------------------------------------------------------

    /// <summary>
    /// Player data record: contains damage, healing, taken damage, and per-skill statistics.
    /// Notes:
    /// - <see cref="DamageStats"/>, <see cref="HealingStats"/>, and <see cref="TakenStats"/> aggregate totals (skill-agnostic).
    /// - <see cref="SkillUsage"/>, <see cref="HealingBySkill"/>, and <see cref="TakenDamageBySkill"/> hold per-skill breakdown dictionaries.
    /// - All writes go through AddDamage/AddHealing/AddTakenDamage to keep aggregate and grouped stats in sync.
    /// </summary>
    public class PlayerData
    {
        #region Basic metadata

        /// <summary>Player unique UID.</summary>
        public long Uid { get; }

        /// <summary>Player nickname.</summary>
        public string Nickname { get; set; } = "Unknown";

        /// <summary>Combat power.</summary>
        public int CombatPower { get; set; } = 0;

        /// <summary>Profession.</summary>
        public string Profession { get; set; } = "Unknown";

        public string SubProfession { get; set; } = null;
        public ClassSpec Spec { get; set; } = ClassSpec.Unknown;

        #endregion

        #region Statistic holders and lookups

        /// <summary>Player custom attributes (key=value). Can be used for extensions or linked displays.</summary>
        public Dictionary<string, object> Attributes { get; } = new();

        /// <summary>Aggregate player damage statistics.</summary>
        public StatisticData DamageStats { get; } = new();

        /// <summary>Aggregate player healing statistics.</summary>
        public StatisticData HealingStats { get; } = new();

        /// <summary>Total taken damage (fast path). May differ from <see cref="TakenStats.Total"/> if hpLessen adjusts.</summary>
        public ulong TakenDamage { get; private set; }

        /// <summary>Per-skill damage/healing statistics (key = skill ID).</summary>
        public Dictionary<long, StatisticData> SkillUsage { get; } = new();

        /// <summary>
        /// Per-skill damage/healing statistics (key = skill ID) broken down by element.
        /// </summary>

        public Dictionary<long, Dictionary<string, StatisticData>> SkillUsageByElement = new();

        /// <summary>
        /// Per-skill healing statistics (key = skill ID) grouped by target.
        /// </summary>
        public Dictionary<long, Dictionary<ulong, StatisticData>> HealingBySkillTarget = new();



        /// <summary>Per-skill taken-damage statistics (key = skill ID).</summary>
        public Dictionary<long, StatisticData> TakenDamageBySkill { get; } = new();

        /// <summary>Per-skill healing statistics (key = skill ID).</summary>
        public Dictionary<long, StatisticData> HealingBySkill { get; } = new();

        /// <summary>Aggregate taken-damage statistics (player/monster combined, not per skill).</summary>
        public StatisticData TakenStats { get; } = new();

        #endregion

        #region Construction

        /// <summary>
        /// Create an instance with the player UID.
        /// </summary>
        /// <param name="uid">Player unique identifier.</param>
        public PlayerData(long uid) => Uid = uid;

        #endregion

        #region Record ingestion (damage/healing/taken)

        /// <summary>
        /// Add a damage record, updating per-skill statistics and the full timeline.
        /// </summary>
        /// <param name="skillId">Skill ID.</param>
        /// <param name="damage">Damage amount.</param>
        /// <param name="isCrit">Whether the hit was critical.</param>
        /// <param name="isLucky">Whether the hit was lucky.</param>
        /// <param name="hpLessen">HP reduction (optional). Typically matches damage for dealing, more relevant when taking damage.</param>
        public void AddDamage(
            long skillId, ulong damage, bool isCrit, bool isLucky, ulong hpLessen = 0,
            string? damageElement = null, bool isCauseLucky = false)
        {
            DamageStats.AddRecord(damage, isCrit, isLucky, hpLessen, isCauseLucky);

            if (!SkillUsage.TryGetValue(skillId, out var stat))
            {
                stat = new StatisticData();
                SkillUsage[skillId] = stat;
            }
            stat.AddRecord(damage, isCrit, isLucky, hpLessen, isCauseLucky);
            if (string.IsNullOrEmpty(SubProfession))
            {
                var sp = skillId.GetSubProfessionBySkillId();
                var spec = skillId.GetClassSpecBySkillId();
                if (!string.IsNullOrEmpty(sp)) SubProfession = sp;
                Spec = spec;
            }

            // Write the new fields into the full timeline record (requires FullRecord.RecordDamage signature to match)
            FullRecord.RecordDamage(
                Uid, skillId, damage, isCrit, isLucky, hpLessen,
                Nickname, CombatPower, Profession,
                damageElement, isCauseLucky, SubProfession);
        }

        /// <summary>
        /// Add a healing record (per skill) and append to the full timeline.
        /// </summary>
        /// <param name="skillId">Skill ID.</param>
        /// <param name="healing">Healing amount.</param>
        /// <param name="isCrit">Whether the heal was critical.</param>
        /// <param name="isLucky">Whether the heal was lucky.</param>
        /// <param name="damageElement"></param>
        /// <param name="isCauseLucky"></param>
        /// <param name="targetUuid"></param>
        public void AddHealing(
            long skillId, ulong healing, bool isCrit, bool isLucky,
            string? damageElement = null, bool isCauseLucky = false, ulong targetUuid = 0)
        {
            HealingStats.AddRecord(healing, isCrit, isLucky, 0, isCauseLucky);

            if (!HealingBySkill.TryGetValue(skillId, out var stat))
            {
                stat = new StatisticData();
                HealingBySkill[skillId] = stat;
            }
            stat.AddRecord(healing, isCrit, isLucky, 0, isCauseLucky);
            if (string.IsNullOrEmpty(SubProfession))
            {
                var sp = skillId.GetSubProfessionBySkillId();
                var spec = skillId.GetClassSpecBySkillId();
                if (!string.IsNullOrEmpty(sp)) SubProfession = sp;
                Spec = spec;
            }

            FullRecord.RecordHealing(
                Uid, skillId, healing, isCrit, isLucky,
                Nickname, CombatPower, Profession,
                damageElement, isCauseLucky, targetUuid, SubProfession);

        }



        /// <summary>
        /// Add a taken-damage record (supports crit/lucky flags), aggregating totals and per-skill stats while logging the full timeline.
        /// </summary>
        /// <param name="skillId">Origin skill ID.</param>
        /// <param name="damage">Taken-damage amount (usually the hit value).</param>
        /// <param name="isCrit">Whether the hit was critical (if the protocol distinguishes it).</param>
        /// <param name="isLucky">Whether the hit was lucky (if the protocol distinguishes it).</param>
        /// <param name="hpLessen">Actual HP loss; when 0 uses <paramref name="damage"/>.</param>
        /// <param name="damageSource">Damage source (0 = player, 1 = monster, 2 = spell, 3 = other).</param>
        /// <param name="isMiss">Whether the hit missed.</param>
        /// <param name="isDead">Whether the hit caused death.</param>
        public void AddTakenDamage(
            long skillId, ulong damage, bool isCrit, bool isLucky, ulong hpLessen = 0,
            int damageSource = 0, bool isMiss = false, bool isDead = false)
        {
            if (!TakenDamageBySkill.TryGetValue(skillId, out var stat))
            {
                stat = new StatisticData();
                TakenDamageBySkill[skillId] = stat;
            }

            // 1) Miss: only count occurrences—do not call AddRecord (no value to aggregate)
            if (isMiss)
            {
                stat.RegisterMiss();   // ✅ Increment directly
                return;
            }

            // 2) Death: count occurrences; still record damage when present
            if (isDead)
            {
                stat.RegisterKill();   // ✅ Increment directly
            }

            var lessen = hpLessen > 0 ? hpLessen : damage;

            // Accumulate player total taken damage
            TakenDamage += lessen;

            // Aggregate (overall taken damage)
            TakenStats.AddRecord(damage, isCrit, isLucky, lessen);

            // Per-skill accumulation
            stat.AddRecord(damage, isCrit, isLucky, lessen /* optional isCauseLucky flag */);

            // Full timeline logging (if FullRecord.RecordTakenDamage is extended)
            FullRecord.RecordTakenDamage(Uid, skillId, damage, isCrit, isLucky, lessen,
                Nickname, CombatPower, Profession, damageSource, isMiss, isDead);
        }




        /// <summary>
        /// Set the player's profession.
        /// </summary>
        /// <param name="profession">Profession name.</param>
        public void SetProfession(string profession) => Profession = profession;

        #endregion

        #region Attribute accessors

        /// <summary>
        /// Set a custom player attribute.
        /// </summary>
        /// <param name="key">Attribute key.</param>
        /// <param name="value">Attribute value.</param>
        public void SetAttrKV(string key, object value)
        {
            Attributes[key] = value;
        }

        /// <summary>
        /// Get a custom player attribute (returns null when missing).
        /// </summary>
        /// <param name="key">Attribute key.</param>
        /// <returns>Attribute value or null.</returns>
        public object? GetAttrKV(string key)
        {
            return Attributes.TryGetValue(key, out var val) ? val : null;
        }
        #endregion

        #region Realtime refresh and aggregation output

        /// <summary>
        /// Check whether the player has meaningful combat data (any damage/healing/taken value counts).
        /// </summary>
        /// <returns>True when data exists; otherwise false.</returns>
        public bool HasCombatData()
        {
            return DamageStats.Total > 0 || HealingStats.Total > 0 || TakenDamage > 0;
        }

        /// <summary>Refresh realtime DPS/HPS windows.</summary>
        public void UpdateRealtimeStats()
        {
            DamageStats.UpdateRealtimeStats();
            HealingStats.UpdateRealtimeStats();
            TakenStats.UpdateRealtimeStats(); // ★ Fix: include taken-damage realtime refresh
        }

        /// <summary>Get total DPS (average over the entire duration).</summary>
        /// <returns>Per-second double value.</returns>
        public double GetTotalDps() => DamageStats.GetTotalPerSecond();

        /// <summary>Get total HPS (average over the entire duration).</summary>
        /// <returns>Per-second double value.</returns>
        public double GetTotalHps() => HealingStats.GetTotalPerSecond();

        /// <summary>
        /// Get combined hit counts (damage + healing).
        /// </summary>
        /// <returns>Tuple of normal/critical/lucky/total counts.</returns>
        public (int Normal, int Critical, int Lucky, int Total) GetTotalCount()
            => (
                DamageStats.CountNormal + HealingStats.CountNormal,
                DamageStats.CountCritical + HealingStats.CountCritical,
                DamageStats.CountLucky + HealingStats.CountLucky,
                DamageStats.CountTotal + HealingStats.CountTotal
            );

        /// <summary>
        /// Get skill statistics summaries with optional sorting and limits.
        /// </summary>
        /// <param name="topN">
        /// Return only the top N entries (sorted by total damage/healing). Null or &lt;= 0 returns all results.
        /// </param>
        /// <param name="orderByTotalDesc">Whether to order totals in descending order.</param>
        /// <param name="filterType">
        /// Filter by skill type: <see cref="Core.SkillType.Damage"/> captures damage only;
        /// <see cref="Core.SkillType.Heal"/> captures healing only; null currently behaves the same as Damage.
        /// </param>
        /// <returns>List of skill summaries.</returns>
        public List<SkillSummary> GetSkillSummaries(
            int? topN = null,
            bool orderByTotalDesc = true,
            Core.SkillType? filterType = Core.SkillType.Damage)
        {
            // 1) Pick the data source
            IEnumerable<KeyValuePair<long, StatisticData>> source;
            if (filterType == Core.SkillType.Damage)
                source = SkillUsage;                  // Damage per skill
            else if (filterType == Core.SkillType.Heal)
                source = HealingBySkill;              // Healing per skill (dictionary already populated)
            else
                source = SkillUsage;                  // Default to damage; add a Merge variant if combined view is needed

            // 2) Denominator: sum totals directly from the chosen source to avoid mixing collections
            ulong denom = 0;
            foreach (var kv in source) denom += kv.Value.Total;
            if (denom == 0) denom = 1; // Prevent divide-by-zero

            // 3) Build the list
            var list = new List<SkillSummary>();
            foreach (var kv in source)
            {
                var id = kv.Key;
                var s = kv.Value;
                var meta = SkillBook.Get(id);

                list.Add(new SkillSummary
                {
                    SkillId = id,
                    SkillName = meta.Name,
                    Total = s.Total,
                    HitCount = s.CountTotal,
                    AvgPerHit = s.GetAveragePerHit(),
                    CritRate = s.GetCritRate(),
                    LuckyRate = s.GetLuckyRate(),
                    MaxSingleHit = s.MaxSingleHit,
                    MinSingleHit = s.MinSingleHit == ulong.MaxValue ? 0 : s.MinSingleHit,
                    RealtimeValue = s.RealtimeValue,
                    RealtimeMax = s.RealtimeMax,
                    TotalDps = s.GetTotalPerSecond(),
                    LastTime = s.LastRecordTime,     // Remove if LastRecordTime is unavailable
                    ShareOfTotal = (double)s.Total / denom,  // 0–1 share aligned with the source
                    LuckyDamage = s.Lucky + s.CritLucky,   // ★ Combined lucky contribution
                    CritLuckyDamage = s.CritLucky,
                    CauseLuckyDamage = s.CauseLucky,
                    CountLucky = s.CountLucky,

                });
            }

            // 4) Sort/trim
            if (orderByTotalDesc) list = list.OrderByDescending(x => x.Total).ToList();
            if (topN.HasValue && topN.Value > 0 && list.Count > topN.Value)
                list = list.Take(topN.Value).ToList();

            return list;
        }


        /// <summary>
        /// Skill share within the realtime window: returns Top N plus an optional “Others” bucket for charts.
        /// </summary>
        /// <param name="topN">Number of top skills to include.</param>
        /// <param name="includeOthers">Whether to include the collected “Others” share.</param>
        /// <returns>Tuple list: (SkillId, SkillName, Realtime, Percent).</returns>
        public List<(long SkillId, string SkillName, ulong Realtime, int Percent)> GetSkillDamageShareRealtime(int topN = 10, bool includeOthers = true)
        {
            if (SkillUsage.Count == 0) return new List<(long, string, ulong, int)>();

            // Denominator: realtime window damage
            ulong denom = 0;
            foreach (var kv in SkillUsage) denom += kv.Value.RealtimeValue;
            if (denom == 0) return new List<(long, string, ulong, int)>();

            var top = SkillUsage
                .Select(kv => new { kv.Key, Val = kv.Value.RealtimeValue })
                .OrderByDescending(x => x.Val)
                .ToList();

            var chosen = top.Take(topN).ToList();
            ulong chosenSum = 0;
            foreach (var c in chosen) chosenSum += c.Val;

            var result = new List<(long, string, ulong, int)>(chosen.Count + 1);
            foreach (var c in chosen)
            {
                double r = (double)c.Val / denom;
                int p = (int)Math.Round(r * 100.0);
                var name = SkillBook.Get(c.Key).Name;
                result.Add((c.Key, name, c.Val, p));
            }

            if (includeOthers && top.Count > chosen.Count)
            {
                ulong others = denom - chosenSum;
                int p = (int)Math.Round((double)others / denom * 100.0);
                result.Add((0, "Others", others, p));
            }

            return result;
        }

        /// <summary>Reset all player statistics and state (including taken-damage aggregates / per-skill).</summary>
        public void Reset()
        {
            DamageStats.Reset();
            HealingStats.Reset();
            TakenStats.Reset();          // ★ New
            TakenDamage = 0;
            Profession = "Unknown";
            SkillUsage.Clear();
            TakenDamageBySkill.Clear();
            HealingBySkill.Clear();
        }

        #endregion

        #region Skill share (full encounter / single player)

        /// <summary>
        /// Skill share for the entire encounter — single player perspective.
        /// </summary>
        /// <param name="topN">Number of top skills to include.</param>
        /// <param name="includeOthers">Whether to append the “Others” bucket.</param>
        /// <returns>List of (SkillId, SkillName, Total, Percent).</returns>
        public List<(long SkillId, string SkillName, ulong Total, int Percent)>
            GetSkillDamageShareTotal(int topN = 10, bool includeOthers = true)
        {
            if (SkillUsage.Count == 0) return new();

            // 1) Denominator: sum total damage across all skills for the player
            ulong denom = 0;
            foreach (var kv in SkillUsage) denom += kv.Value.Total;
            if (denom == 0) return new();

            // 2) Pick Top N by total damage
            var top = SkillUsage
                .Select(kv => new { kv.Key, Val = kv.Value.Total })
                .OrderByDescending(x => x.Val)
                .ToList();

            var chosen = top.Take(topN).ToList();
            ulong chosenSum = 0;
            foreach (var c in chosen) chosenSum += c.Val;

            // 3) Assemble the result (percentages rounded to integers)
            var result = new List<(long SkillId, string SkillName, ulong Total, int Percent)>(chosen.Count + 1);
            foreach (var c in chosen)
            {
                double r = (double)c.Val / denom;
                int p = (int)Math.Round(r * 100.0);
                var name = SkillBook.Get(c.Key).Name;
                result.Add((c.Key, name, c.Val, p));
            }

            // 4) Aggregate the rest as “Others”
            if (includeOthers && top.Count > chosen.Count)
            {
                ulong others = denom - chosenSum;
                int p = (int)Math.Round((double)others / denom * 100.0);
                result.Add((0, "Others", others, p));
            }

            return result;
        }

        #endregion

        #region Taken-damage queries (single player: aggregate/per-skill)

        /// <summary>
        /// Get the player's taken-damage skill summary list (full encounter).
        /// </summary>
        /// <param name="topN">Return only the top N entries by taken total. Null or &lt;= 0 returns all.</param>
        /// <param name="orderByTotalDesc">Whether to sort by taken total descending.</param>
        /// <returns><see cref="SkillSummary"/> list.</returns>
        public List<SkillSummary> GetTakenDamageSummaries(int? topN = null, bool orderByTotalDesc = true)
        {
            // No taken-damage entries → return empty list
            if (TakenDamageBySkill.Count == 0) return new();

            // Denominator: player's total taken damage (for share calculations)
            ulong denom = 0;
            foreach (var kv in TakenDamageBySkill) denom += kv.Value.Total;
            if (denom == 0) denom = 1; // Prevent divide-by-zero

            var list = new List<SkillSummary>(TakenDamageBySkill.Count);
            foreach (var kv in TakenDamageBySkill)
            {
                var id = kv.Key;        // Skill ID
                var s = kv.Value;       // Skill statistics
                var meta = SkillBook.Get(id); // Skill metadata (name, etc.)

                list.Add(new SkillSummary
                {
                    SkillId = id,
                    SkillName = meta.Name,
                    Total = s.Total,                 // Total taken damage
                    HitCount = s.CountTotal,         // Times hit
                    AvgPerHit = s.GetAveragePerHit(),// Average taken per hit
                    CritRate = 0,                    // Taken damage does not differentiate crits; fixed at 0
                    LuckyRate = 0,                   // Taken damage does not differentiate lucky; fixed at 0
                    MaxSingleHit = s.MaxSingleHit,   // Max taken hit
                    MinSingleHit = s.MinSingleHit == ulong.MaxValue ? 0 : s.MinSingleHit, // Min taken hit
                    RealtimeValue = s.RealtimeValue, // Realtime taken total
                    RealtimeMax = s.RealtimeMax,     // Realtime taken peak
                    TotalDps = s.GetTotalPerSecond(),// Better described as average taken per second
                    LastTime = s.LastRecordTime,     // Remove if LastRecordTime is not present
                    ShareOfTotal = (double)s.Total / denom // 0–1 share aligned with the source
                });
            }

            // Sort
            if (orderByTotalDesc)
                list = list.OrderByDescending(x => x.Total).ToList();

            // Trim
            if (topN.HasValue && topN.Value > 0 && list.Count > topN.Value)
                list = list.Take(topN.Value).ToList();

            return list;
        }

        /// <summary>
        /// Get detailed taken-damage statistics for a specific skill against this player.
        /// </summary>
        /// <param name="skillId">Skill ID.</param>
        /// <returns><see cref="SkillSummary"/> when present; otherwise null.</returns>
        public SkillSummary? GetTakenDamageDetail(long skillId)
        {
            // No taken-damage records for the skill
            if (!TakenDamageBySkill.TryGetValue(skillId, out var stat))
                return null;

            var meta = SkillBook.Get(skillId);
            return new SkillSummary
            {
                SkillId = skillId,
                SkillName = meta.Name,
                Total = stat.Total,
                HitCount = stat.CountTotal,
                AvgPerHit = stat.GetAveragePerHit(),
                CritRate = 0,
                LuckyRate = 0,
                MaxSingleHit = stat.MaxSingleHit,
                MinSingleHit = stat.MinSingleHit == ulong.MaxValue ? 0 : stat.MinSingleHit,
                RealtimeValue = stat.RealtimeValue,
                RealtimeMax = stat.RealtimeMax,
                TotalDps = stat.GetTotalPerSecond(),
                LastTime = stat.LastRecordTime
            };
        }

        #endregion



    }

    // ------------------------------------------------------------
    // # Section: Player data manager (caching/timers/combat clock/snapshots/queries)
    // ------------------------------------------------------------

    /// <summary>
    /// Player data manager: responsible for creating/caching player objects, refreshing realtime stats in bulk,
    /// syncing external attributes, maintaining the combat clock, persisting snapshots, and exposing query helpers.
    /// Threading note: intended to run on the same context as capture/parsing; lock externally or use concurrent collections when multithreading.
    /// </summary>
    public class PlayerDataManager
    {
        #region Storage

        private readonly object _playersLock = new object(); // ★ Unified lock (added 2025-08-19)


        /// <summary>
        /// Snapshot history for battle data.
        /// </summary>
        private readonly List<BattleSnapshot> _history = new();

        /// <summary>
        /// Read-only access to the battle snapshot history.
        /// </summary>
        public IReadOnlyList<BattleSnapshot> History => _history;

        /// <summary>UID → Player data.</summary>
        private readonly Dictionary<long, PlayerData> _players = new();

        /// <summary>UID → Nickname (external sync cache).</summary>
        private static readonly ConcurrentDictionary<long, string> _nicknameRequestedUids = new();

        /// <summary>UID → Combat power (external sync cache).</summary>
        private static readonly ConcurrentDictionary<long, int> _combatPowerByUid = new();

        /// <summary>UID → Profession (external sync cache).</summary>
        private static readonly ConcurrentDictionary<long, string> _professionByUid = new();


        /// <summary>Encounter start time (set upon the first combat event).</summary>
        private DateTime? _combatStart;

        /// <summary>Encounter end time (set when manually ended; null while ongoing).</summary>
        private DateTime? _combatEnd;

        /// <summary>Whether combat is currently active.</summary>
        public bool IsInCombat => _combatStart.HasValue && !_combatEnd.HasValue;

        /// <summary>Inactivity timeout before auto-clear (seconds). 0 disables auto-ending.</summary>
        private static readonly TimeSpan InactivityTimeout = TimeSpan.FromSeconds(AppConfig.CombatTimeClearDelaySeconds);

        /// <summary>Timestamp of the last team-wide activity.</summary>
        private DateTime _lastCombatActivity = DateTime.MinValue;


        #endregion

        #region Timers

        /// <summary>Timer used to refresh realtime stats periodically (defaults to 1 second).</summary>
        private readonly System.Timers.Timer _checkTimer;

        /// <summary>Timestamp of the most recent player creation (to skip idle scenarios quickly).</summary>
        private DateTime _lastAddTime = DateTime.MinValue;

        /// <summary>Flag: timed out and waiting to clear previous combat when the next one starts.</summary>
        private bool _pendingClearOnNextCombat = false;

        #endregion

        // Place anywhere inside PlayerDataManager (method area)
        private void UpsertCacheProfile(long uid) // ★ New helper
        {
            // Pull the latest trio from PlayerData to ensure complete fields (avoid overwriting with defaults)
            var p = GetOrCreate(uid);
            _userCache.UpsertIfChanged(new UserProfile
            {
                Uid = uid,
                Nickname = p.Nickname ?? string.Empty,
                Profession = p.Profession ?? string.Empty,
                Power = p.CombatPower
            }, caseInsensitiveName: true, trimName: true);
        }


        // ------------------------------------------------------------
        // # Section: Global combat timing (start/end/duration/formatting)
        // ------------------------------------------------------------
        #region Global combat timing

        /// <summary>
        /// Mark a combat activity (invoked whenever damage/healing/taken events arrive):
        /// - Not started yet: set the start time to now.
        /// - Already ended: treat as a new encounter, reset, and start again.
        /// - Previous encounter timed out but not cleared: clear previous data now (data and clock only, keep cached metadata).
        /// </summary>
        private void MarkCombatActivity()
        {
            throw new NotImplementedException("This functionality now lives in SRDA.Core.Data.DataStorage.");

            //var now = DateTime.Now;
            //// —— 新增：如果上一场已超时结束但未清空，则在此刻（新战斗的首个事件）清空上一场 —— 
            //if (_pendingClearOnNextCombat)
            //{

            //    // 只清玩家数据与战斗时钟；缓存（昵称/战力/职业）保留
            //    ClearAll(false);
            //    FormManager.DpsStatistics.HandleClearData(true);
            //    _pendingClearOnNextCombat = false;
            //}

            //// 原逻辑：未开始或已结束 => 开新场
            //if (!_combatStart.HasValue || _combatEnd.HasValue)
            //{
            //    _combatStart = now;
            //    _combatEnd = null;
            //}

            //_lastCombatActivity = now;
        }

        /// <summary>
        /// Manually end the current combat encounter (sets the end time).
        /// </summary>
        public void EndCombat()
        {
            if (_combatStart.HasValue && !_combatEnd.HasValue)
                _combatEnd = DateTime.Now;
        }

        /// <summary>
        /// Reset combat timing (timer only, player data remains).
        /// </summary>
        public void ResetCombatClock()
        {
            _combatStart = null;
            _combatEnd = null;


        }

        /// <summary>
        /// Get total combat duration:
        /// - Not started: 00:00:00
        /// - In progress: now - start
        /// - Ended: end - start
        /// </summary>
        /// <returns><see cref="TimeSpan"/> duration.</returns>
        public TimeSpan GetCombatDuration()
        {
            if (!_combatStart.HasValue) return TimeSpan.Zero;
            if (_combatEnd.HasValue) return _combatEnd.Value - _combatStart.Value;
            return DateTime.Now - _combatStart.Value;
        }

        /// <summary>
        /// Return a formatted string for the combat duration:
        /// hh:mm:ss when >= 1 hour; otherwise mm:ss.
        /// </summary>
        /// <returns>Formatted duration.</returns>
        public string GetFormattedCombatDuration()
        {
            var ts = GetCombatDuration();
            if (ts < TimeSpan.Zero) ts = TimeSpan.Zero; // Clamp in pathological cases

            return ts.TotalHours >= 1
                ? ts.ToString(@"hh\:mm\:ss")
                : ts.ToString(@"mm\:ss");
        }


        #endregion

        #region Construction

        // Added to the PlayerDataManager field area:
        private readonly UserLocalCache _userCache; // ★ New field

        /// <summary>
        /// Constructor: start the timer to refresh realtime stats at a fixed cadence.
        /// </summary>
        public PlayerDataManager()
        {
            _userCache = new UserLocalCache(flushDelayMs: 1500); // ★ New: local cache, default 1.5s batched flush

            _checkTimer = new System.Timers.Timer(1000)
            {
                AutoReset = true,
                Enabled = false // Start disabled to avoid callbacks during construction
            };
            _checkTimer.Elapsed += CheckTimerElapsed;

            // Explicit initialization (optional, clarifies intent)
            _lastCombatActivity = DateTime.MinValue;
            _pendingClearOnNextCombat = false;

            // Start after all state is ready
            _checkTimer.Start();
        }


        #endregion

        // ------------------------------------------------------------
        // # Section: Player instance retrieval / attribute KV access
        // ------------------------------------------------------------
        #region Retrieval or creation

        /// <summary>
        /// Manually create a snapshot and return the latest entry (when player data exists).
        /// </summary>
        /// <returns>The snapshot just saved; null when no player data exists.</returns>
        public BattleSnapshot? TakeSnapshotAndGet()
        {
            if (_players.Count == 0) return null;

            //if (_combatStart.HasValue && !_combatEnd.HasValue)
            //    _combatEnd = _lastCombatActivity != DateTime.MinValue ? _lastCombatActivity : DateTime.Now;

            // 调用内部保存逻辑
            SaveCurrentBattleSnapshot();

            // 返回刚保存的那条快照
            return _history.Count > 0 ? _history[^1] : null; // ^1 fetches the last element (C# 8.0)
        }



        /// <summary>
        /// Get or create player data for the specified UID, seeding nickname/combat power/profession from caches on first creation.
        /// </summary>
        /// <param name="uid">Player UID.</param>
        /// <returns><see cref="PlayerData"/> instance.</returns>
        public PlayerData GetOrCreate(long uid)
        {
            lock (_playersLock)
            {
                if (!_players.TryGetValue(uid, out var data))
                {
                    data = new PlayerData(uid);
                    _players[uid] = data;
                    _lastAddTime = DateTime.Now;



                    // ★ Pull from UserLocalCache first (when available)
                    var prof = _userCache.Get(uid.ToString());

                    // ★ Merge priority: dictionary > cache > defaults
                    // Nickname
                    if (_nicknameRequestedUids.TryGetValue(uid, out var cachedName) && !string.IsNullOrWhiteSpace(cachedName))
                        data.Nickname = cachedName;
                    else if (prof != null && !string.IsNullOrWhiteSpace(prof.Nickname))
                    {
                        data.Nickname = prof.Nickname;
                        _nicknameRequestedUids[uid] = prof.Nickname; // Sync back to dictionary cache
                    }

                    // Combat power
                    if (_combatPowerByUid.TryGetValue(uid, out var power) && power > 0)
                        data.CombatPower = power;
                    else if (prof != null && prof.Power > 0)
                    {
                        data.CombatPower = (int)prof.Power;
                        _combatPowerByUid[uid] = data.CombatPower;
                    }

                    // Profession
                    if (_professionByUid.TryGetValue(uid, out var profession) && !string.IsNullOrWhiteSpace(profession))
                        data.Profession = profession;
                    else if (prof != null && !string.IsNullOrWhiteSpace(prof.Profession))
                    {
                        data.Profession = prof.Profession;
                        _professionByUid[uid] = data.Profession;
                    }
                }
                return data;
            }
        }



        /// <summary>
        /// 设置指定玩家的自定义属性（KV）。
        /// </summary>
        /// <param name="uid">Player UID.</param>
        /// <param name="key">Attribute key.</param>
        /// <param name="value">Attribute value.</param>
        public void SetAttrKV(long uid, string key, object value)
        {
            GetOrCreate(uid).SetAttrKV(key, value);
        }

        /// <summary>
        /// Get a custom attribute for the given player (returns null when missing).
        /// </summary>
        /// <param name="uid">Player UID.</param>
        /// <param name="key">Attribute key.</param>
        /// <returns>Attribute value or null.</returns>
        public object? GetAttrKV(long uid, string key)
        {
            return GetOrCreate(uid).GetAttrKV(key);
        }
        #endregion

        // ------------------------------------------------------------
        // # Section: Timer loop / idle detection and auto-stop
        // ------------------------------------------------------------
        #region Timer loop

        /// <summary>
        /// Timer callback: refresh realtime stats for all players and evaluate idle timeout logic.
        /// </summary>
        private async void CheckTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            bool hasPlayers;
            lock (_playersLock) { hasPlayers = _players.Count > 0; }
            if (_lastAddTime == DateTime.MinValue || !hasPlayers) return;

            UpdateAllRealtimeStats();

            if (AppConfig.CombatTimeClearDelaySeconds != 0) // 0 means never auto-end
            {
                if (_lastCombatActivity != DateTime.MinValue &&
                    DateTime.Now - _lastCombatActivity > InactivityTimeout)
                {
                    // —— 不清空 —— 只结束并打标记
                    if (_combatStart.HasValue && !_combatEnd.HasValue)
                        _combatEnd = _lastCombatActivity;

                    _pendingClearOnNextCombat = true;   // Clear once the next combat starts
                    _lastCombatActivity = DateTime.MinValue;
                }
            }

            // 目前无异步工作，仅占位保持签名一致
            await Task.CompletedTask;
        }

        #endregion

        // ------------------------------------------------------------
        // # Section: Global write-through (central entry points forwarding to PlayerData)
        // ------------------------------------------------------------
        #region Global write-through (forward to PlayerData)



        /// <summary>
        /// Add a global damage record (marks combat activity, updates player aggregates/per-skill stats, and logs the timeline).
        /// </summary>
        /// <param name="uid">Player UID.</param>
        /// <param name="skillId">Skill ID.</param>
        /// <param name="damage">Damage amount.</param>
        /// <param name="isCrit">Whether the hit was critical.</param>
        /// <param name="isLucky">Whether the hit was lucky.</param>
        /// <param name="damageSource">Damage source.</param>
        /// <param name="hpLessen">HP deduction (optional).</param>
        public void AddDamage(long uid, long skillId, string damageElement, ulong damage, bool isCrit, bool isLucky, bool isCauseLucky, ulong hpLessen = 0)
        {
            MarkCombatActivity();

            // ✅ Use the actual skillId for bookkeeping; do not merge lucky hits into the previous one
            GetOrCreate(uid).AddDamage(skillId, damage, isCrit, isLucky, hpLessen, damageElement, isCauseLucky);

            // ✅ Logging/skill diary also records the current skillId
            SkillDiaryGate.OnHit(uid, skillId, damage, isCrit, isLucky);

        }





        /// <summary>
        /// Add a global healing record (updates player aggregates/per-skill stats and logs the timeline).
        /// </summary>
        /// <param name="uid">Player UID.</param>
        /// <param name="skillId">Skill ID.</param>
        /// <param name="healing">Healing amount.</param>
        /// <param name="isCrit">Whether the heal was critical.</param>
        /// <param name="isLucky">Whether the heal was lucky.</param>
        /// <param name="hpFull">HP fill amount (optional).</param>
        /// <param name="damageSource">Healing type.</param>
        /// <param name="targetUuid">Healed target ID (optional).</param>
        public void AddHealing(long uid, long skillId, string damageElement, ulong healing, bool isCrit, bool isLucky, bool isCauseLucky, ulong targetUuid)
        {
            if (!_combatStart.HasValue || _combatEnd.HasValue)
                return;

            _lastCombatActivity = DateTime.Now;

            // ✅ Bookkeep using the actual skillId
            GetOrCreate(uid).AddHealing(skillId, healing, isCrit, isLucky, damageElement, isCauseLucky, targetUuid);

            // ✅ Record the hit
            SkillDiaryGate.OnHit(uid, skillId, healing, isCrit, isLucky, true);
        }




        /// <summary>
        /// Add a global taken-damage record (full parameter set recommended).
        /// </summary>
        /// <param name="uid">Player UID (target receiving damage).</param>
        /// <param name="skillId">Source skill ID.</param>
        /// <param name="damage">Taken-damage amount.</param>
        /// <param name="isCrit">Whether the hit was critical.</param>
        /// <param name="isLucky">Whether the hit was lucky.</param>
        /// <param name="hpLessen">Actual HP reduction (defaults to <paramref name="damage"/> when 0).</param>
        /// <param name="damageSource">Damage source.</param>
        /// <param name="isMiss">Whether the hit was dodged.</param>
        /// <param name="isDead">Whether the hit caused death.</param>
        public void AddTakenDamage(long uid, long skillId, ulong damage, int damageSource, bool isMiss, bool isDead, bool isCrit, bool isLucky, ulong hpLessen = 0)
        {
            MarkCombatActivity();
            GetOrCreate(uid).AddTakenDamage(
      skillId, damage, isCrit, isLucky, hpLessen,
      damageSource, isMiss, isDead);
        }

        /// <summary>Set player profession (updates cache and instance).</summary>
        /// <param name="uid">Player UID.</param>
        /// <param name="profession">Profession name.</param>
        public void SetProfession(long uid, string profession)
        {
            _professionByUid[uid] = profession;
            GetOrCreate(uid).SetProfession(profession);
            UpsertCacheProfile(uid); // ★ New: write back to local cache

        }

        /// <summary>Set player combat power (updates cache and instance).</summary>
        /// <param name="uid">Player UID.</param>
        /// <param name="combatPower">Combat power.</param>
        public void SetCombatPower(long uid, int combatPower)
        {
            _combatPowerByUid[uid] = combatPower;
            GetOrCreate(uid).CombatPower = combatPower;
            UpsertCacheProfile(uid); // ★ New: write back to local cache
        }

        /// <summary>Set player nickname (updates cache and instance).</summary>
        /// <param name="uid">Player UID.</param>
        /// <param name="nickname">Nickname.</param>
        public void SetNickname(long uid, string nickname)
        {
            _nicknameRequestedUids[uid] = nickname;
            GetOrCreate(uid).Nickname = nickname;
            UpsertCacheProfile(uid); // ★ New: write back to local cache
        }

        #endregion

        // ------------------------------------------------------------
        // # Section: Bulk operations and queries (player sets / skill share / skill details / team aggregates)
        // ------------------------------------------------------------
        #region Bulk operations and queries


        /// <summary>
        /// Get players with combat data (filters out those without damage, healing, or taken values).
        /// </summary>
        /// <returns>List of players with combat data.</returns>
        public IEnumerable<PlayerData> GetPlayersWithCombatData()
        {
            PlayerData[] snapshot;
            lock (_playersLock)
            {
                if (_players.Count == 0) return Array.Empty<PlayerData>();
                snapshot = _players.Values.ToArray(); // Copy while holding the lock
            }

            // Filter outside the lock: read scalar totals only to avoid enumerating mutable collections inside HasCombatData()
            return snapshot.Where(p =>
                p != null &&
                (
                    (p.DamageStats?.Total ?? 0UL) != 0UL ||
                    (p.HealingStats?.Total ?? 0UL) != 0UL ||
                    p.TakenDamage != 0UL
                )
            );
        }

        /// <summary>Refresh realtime statistics for all players (sliding window).</summary>
        public void UpdateAllRealtimeStats()
        {
            PlayerData[] players;
            lock (_playersLock)
            {
                if (_players.Count == 0) return;
                players = _players.Values.ToArray();
            }
            foreach (var p in players) p?.UpdateRealtimeStats();
        }


        /// <summary>Get all player data instances.</summary>
        /// <returns>Enumerable of player data.</returns>
        public IEnumerable<PlayerData> GetAllPlayers()
        {
            lock (_playersLock) { return _players.Values.ToArray(); }
        }

        bool InitialStart = false;
        /// <summary>
        /// Clear all player data (optionally preserve the combat clock). Saves the current battle snapshot before clearing.
        /// </summary>
        /// <param name="keepCombatTime">true = keep the combat clock; false = reset the combat clock as well.</param>
        public void ClearAll(bool keepCombatTime = true)
        {
            throw new NotImplementedException("This functionality now lives in SRDA.Core.Data.DataStorage.");

            //bool hadPlayers;
            //lock (_playersLock) { hadPlayers = _players.Count > 0; }

            //if (hadPlayers)
            //{
            //    if (_combatStart.HasValue && !_combatEnd.HasValue)
            //        _combatEnd = _lastCombatActivity != DateTime.MinValue ? _lastCombatActivity : DateTime.Now;

            //    // Save the current battle snapshot first (extend SaveCurrentBattleSnapshot to include NPCs if needed)
            //    SaveCurrentBattleSnapshot();
            //}

            //// 清玩家
            //lock (_playersLock) { _players.Clear();}

            //// ✅ 清“当前战斗”的 NPC 统计（与玩家同一生命周期）
            //// Assuming the NpcManager instance lives alongside (e.g., static singleton outside PlayerDataManager)
            //StatisticData._npcManager?.ResetAll();

            //// UI 清理与战斗时钟复位（保持你原有逻辑）
            //FormManager.DpsStatistics.ListClear();
            //ResetCombatClock();
        }

        /// <summary>Get all player UIDs.</summary>
        public IEnumerable<long> GetAllUids()
        {
            lock (_playersLock) { return _players.Keys.ToArray(); }
        }

        /// <summary>
        /// Get team-wide top skills (aggregated by total damage).
        /// </summary>
        /// <param name="topN">Number of skills to return.</param>
        /// <returns>Descending list of team skill summaries.</returns>
        public List<TeamSkillSummary> GetTeamTopSkillsByTotal(int topN = 20)
        {
            PlayerData[] players;
            lock (_playersLock) { players = _players.Values.ToArray(); }

            var agg = new Dictionary<long, (ulong total, int count)>();
            foreach (var p in players)
            {
                // 技能字典也拍快照
                foreach (var kv in p.SkillUsage.ToArray())
                {
                    if (!agg.TryGetValue(kv.Key, out var a))
                        agg[kv.Key] = (kv.Value.Total, kv.Value.CountTotal);
                    else
                        agg[kv.Key] = (a.total + kv.Value.Total, a.count + kv.Value.CountTotal);
                }
            }

            return agg.OrderByDescending(x => x.Value.total)
                      .Take(topN)
                      .Select(x => new TeamSkillSummary
                      {
                          SkillId = x.Key,
                          SkillName = SkillBook.Get(x.Key).Name,
                          Total = x.Value.total,
                          HitCount = x.Value.count
                      })
                      .ToList();
        }

        /// <summary>
        /// Get a player's skill detail list (supports filtering by skill type).
        /// </summary>
        /// <param name="uid">Player UID.</param>
        /// <param name="topN">Optional top N.</param>
        /// <param name="orderByTotalDesc">Whether to sort by total descending.</param>
        /// <param name="filterType">Skill type filter (defaults to Damage).</param>
        /// <returns><see cref="SkillSummary"/> list.</returns>
        public List<SkillSummary> GetPlayerSkillSummaries(
            long uid,
            int? topN = null,
            bool orderByTotalDesc = true,
            Core.SkillType? filterType = Core.SkillType.Damage)
        {
            var p = GetOrCreate(uid);
            return p.GetSkillSummaries(topN, orderByTotalDesc, filterType);
        }


        /// <summary>
        /// Player realtime skill share (Top N plus optional Others bucket).
        /// </summary>
        /// <param name="uid">Player UID.</param>
        /// <param name="topN">Number of top skills.</param>
        /// <param name="includeOthers">Whether to include “Others”.</param>
        /// <returns>Share data (SkillId, SkillName, Realtime, Percent).</returns>
        public List<(long SkillId, string SkillName, ulong Realtime, int Percent)>
            GetPlayerSkillShareRealtime(long uid, int topN = 10, bool includeOthers = true)
        {
            var p = GetOrCreate(uid);
            return p.GetSkillDamageShareRealtime(topN, includeOthers);
        }


        /// <summary>
        /// Player + skill ID detail lookup.
        /// </summary>
        /// <param name="uid">Player UID.</param>
        /// <param name="skillId">Skill ID.</param>
        /// <returns><see cref="SkillSummary"/> when found; otherwise null.</returns>
        public SkillSummary? GetPlayerSkillDetail(long uid, long skillId)
        {
            var p = GetOrCreate(uid);
            if (!p.SkillUsage.TryGetValue(skillId, out var stat))
                return null;

            var meta = SkillBook.Get(skillId);
            return new SkillSummary
            {
                SkillId = skillId,
                SkillName = meta.Name,
                Total = stat.Total,
                HitCount = stat.CountTotal,
                AvgPerHit = stat.GetAveragePerHit(),
                CritRate = stat.GetCritRate(),
                LuckyRate = stat.GetLuckyRate(),
                MaxSingleHit = stat.MaxSingleHit,
                MinSingleHit = stat.MinSingleHit == ulong.MaxValue ? 0 : stat.MinSingleHit,
                RealtimeValue = stat.RealtimeValue,
                RealtimeMax = stat.RealtimeMax,
                TotalDps = stat.GetTotalPerSecond(),
                LastTime = stat.LastRecordTime
            };
        }


        /// <summary>
        /// Skill share (entire encounter) — team aggregate.
        /// </summary>
        /// <param name="topN">Number of skills to include.</param>
        /// <param name="includeOthers">Whether to include “Others”.</param>
        /// <returns>(SkillId, SkillName, Total, Percent) list.</returns>
        // Modified 2025-08-19: same approach for team-wide totals
        public List<(long SkillId, string SkillName, ulong Total, int Percent)>
            GetTeamSkillDamageShareTotal(int topN = 10, bool includeOthers = true)
        {
            PlayerData[] players;
            lock (_playersLock) { players = _players.Values.ToArray(); }

            var agg = new Dictionary<long, ulong>();
            foreach (var p in players)
            {
                foreach (var kv in p.SkillUsage.ToArray())
                {
                    if (kv.Value.Total == 0) continue;
                    agg[kv.Key] = agg.TryGetValue(kv.Key, out var old) ? old + kv.Value.Total : kv.Value.Total;
                }
            }
            if (agg.Count == 0) return new();

            ulong denom = 0; foreach (var v in agg.Values) denom += v; if (denom == 0) return new();

            var top = agg.Select(kv => new { kv.Key, Val = kv.Value })
                         .OrderByDescending(x => x.Val)
                         .Take(topN)
                         .ToList();

            ulong chosenSum = 0; foreach (var c in top) chosenSum += c.Val;

            var result = new List<(long, string, ulong, int)>(top.Count + 1);
            foreach (var c in top)
            {
                int pcent = (int)Math.Round((double)c.Val / denom * 100.0);
                result.Add((c.Key, SkillBook.Get(c.Key).Name, c.Val, pcent));
            }
            if (includeOthers && agg.Count > top.Count)
            {
                ulong others = denom - chosenSum;
                int pcent = (int)Math.Round((double)others / denom * 100.0);
                result.Add((0, "Others", others, pcent));
            }
            return result;
        }


        /// <summary>
        /// Get basic player information by UID: nickname, combat power, profession.
        /// </summary>
        /// <param name="uid">Player UID.</param>
        /// <returns>(Nickname, CombatPower, Profession) tuple.</returns>
        public (string Nickname, int CombatPower, string Profession) GetPlayerBasicInfo(long uid)
        {
            // First check existing PlayerData
            // Access to _players is always locked
            lock (_playersLock)
            {
                if (_players.TryGetValue(uid, out var player))
                {
                    return (player.Nickname, player.CombatPower, player.Profession);
                }
            }

            // Fallback to cache dictionaries when PlayerData is missing
            string nickname = _nicknameRequestedUids.TryGetValue(uid, out var name) ? name : "Unknown";
            int combatPower = _combatPowerByUid.TryGetValue(uid, out var power) ? power : 0;
            string profession = _professionByUid.TryGetValue(uid, out var prof) ? prof : "Unknown";

            return (nickname, combatPower, profession);
        }

        /// <summary>
        /// Get full aggregated statistics for a player by UID.
        /// </summary>
        /// <param name="uid">Player UID.</param>
        /// <returns>Aggregate tuple covering damage/healing/taken/realtime/extrema.</returns>
        public (long Uid, string Nickname, int CombatPower, string Profession,
        ulong TotalDamage, double CritRate, double LuckyRate,
        ulong MaxSingleHit, ulong MinSingleHit,
        ulong RealtimeDps, ulong RealtimeDpsMax,
        double TotalDps, ulong TotalHealing, double TotalHps,
        ulong TakenDamage, DateTime? LastRecordTime)
        GetPlayerFullStats(long uid)
        {
            if (!_players.TryGetValue(uid, out var p))
                return (uid, "Unknown", 0, "Unknown", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null);

            var dmg = p.DamageStats;
            var heal = p.HealingStats;

            return (
                p.Uid,
                p.Nickname,
                p.CombatPower,
                p.Profession,

                dmg.Total,
                dmg.GetCritRate(),
                dmg.GetLuckyRate(),

                dmg.MaxSingleHit,
                dmg.MinSingleHit == ulong.MaxValue ? 0 : dmg.MinSingleHit,

                dmg.RealtimeValue,
                dmg.RealtimeMax,

                p.GetTotalDps(),
                heal.Total,
                p.GetTotalHps(),

                p.TakenDamage,
                dmg.LastRecordTime
            );
        }


        // ------------------------------------------------------------
        // # Section: Taken-damage query interfaces (totals/overview/per-skill)
        // ------------------------------------------------------------
        #region Taken-damage queries
        /// <summary>
        /// Return a player's total taken damage (fast path).
        /// </summary>
        /// <param name="uid">Player UID.</param>
        /// <returns>Total taken damage (<see cref="PlayerData.TakenDamage"/>).</returns>
        public ulong GetPlayerTakenDamageTotal(long uid)
            => GetOrCreate(uid).TakenDamage;

        /// <summary>
        /// Taken-damage overview: total / average per second / realtime total / max single hit / min single hit / last timestamp.
        /// - Average per second = player total taken ÷ combat duration (uses global combat clock).
        /// - Realtime taken = sum of per-skill RealtimeValue within the 1-second window (current setting).
        /// </summary>
        /// <param name="uid">Player UID.</param>
        /// <returns>Six-element overview tuple.</returns>
        public (
            ulong Total,
            double AvgTakenPerSec,
            ulong RealtimeTaken,
            ulong MaxSingleHit,
            ulong MinSingleHit,
            DateTime? LastTime
        ) GetPlayerTakenOverview(long uid)
        {
            var p = GetOrCreate(uid);

            ulong total = p.TakenDamage;
            var dur = GetCombatDuration().TotalSeconds;
            double avgPerSec = (dur > 0) ? total / dur : 0.0;

            var s = p.TakenStats;
            ulong realtime = s.RealtimeValue;
            ulong maxHit = s.MaxSingleHit;
            ulong minHit = s.MinSingleHit == ulong.MaxValue ? 0 : s.MinSingleHit;
            DateTime? last = s.LastRecordTime;

            return (total, avgPerSec, realtime, maxHit, minHit, last);
        }



        /// <summary>
        /// Get the player's taken-damage skill summary list (entire encounter).
        /// </summary>
        /// <param name="uid">Player UID.</param>
        /// <param name="topN">Top N skills (optional; null or &lt;= 0 returns all).</param>
        /// <param name="orderByTotalDesc">Whether to sort by total taken descending.</param>
        /// <returns><see cref="SkillSummary"/> list.</returns>
        public List<SkillSummary> GetPlayerTakenDamageSummaries(long uid, int? topN = null, bool orderByTotalDesc = true)
        {
            var p = GetOrCreate(uid);
            return p.GetTakenDamageSummaries(topN, orderByTotalDesc);
        }


        /// <summary>
        /// Get detailed taken-damage statistics for the specified player and skill combination.
        /// </summary>
        /// <param name="uid">Player UID.</param>
        /// <param name="skillId">Skill ID.</param>
        /// <returns><see cref="SkillSummary"/> when present; otherwise null.</returns>
        public SkillSummary? GetPlayerTakenDamageDetail(long uid, long skillId)
        {
            var p = GetOrCreate(uid);
            return p.GetTakenDamageDetail(skillId);
        }

        #endregion


        #endregion

        // ------------------------------------------------------------
        // # Section: Snapshots (generate/save)
        // ------------------------------------------------------------
        #region Snapshot helpers
        /// <summary>
        /// Generate and persist the current battle snapshot (call before clearing).
        /// Rule: if combat is active with no end time, use the last activity time or now as the end.
        /// </summary>
        // Updated 2025-08-19: capture players under lock, iterate outside
        private void SaveCurrentBattleSnapshot()
        {
            PlayerData[] players;
            lock (_playersLock)
            {
                if (_players.Count == 0) return;
                players = _players.Values.ToArray();
            }

            var endedAt = DateTime.Now;
            var startedAt = _combatStart ?? endedAt;
            var duration = _combatEnd.HasValue ? _combatEnd.Value - startedAt : endedAt - startedAt;
            if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;

            var label = $"End Time: {endedAt:HH:mm:ss}";

            ulong teamDmg = 0, teamHeal = 0, teamTaken = 0;
            var snapPlayers = new Dictionary<long, SnapshotPlayer>(players.Length);

            foreach (var p in players)
            {
                if (p == null || !p.HasCombatData()) continue;

                var dmg = p.DamageStats;
                var heal = p.HealingStats;

                // Capture point-in-time values first
                var totalDmg = dmg.Total;
                var totalHeal = heal.Total;
                var totalTaken = p.TakenDamage;
                // Filter using captured totals
                if (totalDmg == 0 && totalHeal == 0 && totalTaken == 0)
                    continue;

                dmg.UpdateRealtimeStats();
                heal.UpdateRealtimeStats();

                teamDmg += dmg.Total;
                teamHeal += heal.Total;
                teamTaken += p.TakenDamage;

                var damageSkills = p.GetSkillSummaries(null, true, Core.SkillType.Damage);
                var healingSkills = p.GetSkillSummaries(null, true, Core.SkillType.Heal);
                var takenSkills = p.GetTakenDamageSummaries(null, true);

                var sp = new SnapshotPlayer
                {
                    Uid = p.Uid,
                    Nickname = p.Nickname,
                    CombatPower = p.CombatPower,
                    Profession = p.Profession,
                    SubProfession = p.SubProfession,
                    TotalDamage = dmg.Total,
                    TotalDps = p.GetTotalDps(),
                    TotalHealing = heal.Total,
                    TotalHps = p.GetTotalHps(),
                    TakenDamage = p.TakenDamage,
                    LastRecordTime = dmg.LastRecordTime,
                    DamageSkills = damageSkills,
                    HealingSkills = healingSkills,
                    TakenSkills = takenSkills,
                    RealtimeDps = dmg.RealtimeValue,
                    CritRate = dmg.GetCritRate(),
                    LuckyRate = dmg.GetLuckyRate(),
                    CriticalDamage = dmg.Critical,
                    LuckyDamage = dmg.Lucky + dmg.CritLucky,   // ★ Combined
                    CritLuckyDamage = dmg.CritLucky,
                    MaxSingleHit = dmg.MaxSingleHit,
                    HealingCritical = heal.Critical,
                    HealingLucky = heal.Lucky + heal.CritLucky,
                    HealingCritLucky = heal.CritLucky,
                    HealingRealtime = heal.RealtimeValue,
                    HealingRealtimeMax = heal.RealtimeMax,
                    RealtimeDpsMax = dmg.RealtimeMax,
                };

                snapPlayers[p.Uid] = sp;
            }
            if (snapPlayers.Count == 0) return;

            var snapshot = new BattleSnapshot
            {
                Label = label,
                StartedAt = startedAt,
                EndedAt = _combatEnd ?? endedAt,
                Duration = duration,
                TeamTotalDamage = teamDmg,
                TeamTotalHealing = teamHeal,
                TeamTotalTakenDamage = teamTaken,
                Players = snapPlayers
            };

            _history.Add(snapshot);
        }


        /// <summary>
        /// Clear all historical snapshots (does not affect current combat data).
        /// </summary>
        public void ClearSnapshots()
        {
            _history.Clear();
        }
        #endregion

        #region NPC


        /// <summary>
        /// Taken-damage data for a single NPC (aggregated by attacking player, not skill).
        /// </summary>
        public sealed class NpcData
        {
            /// <summary>NPC unique ID (use monster entity ID or template ID as needed).</summary>
            public long NpcId { get; }

            /// <summary>NPC name (optional).</summary>
            public string Name { get; private set; } = "Unknown NPC";

            /// <summary>NPC taken-damage aggregate (total/realtime/peaks/extrema).</summary>
            public StatisticData TakenStats { get; } = new();

            /// <summary>
            /// Attacker UID → aggregate statistics for damage dealt to this NPC (skill-agnostic).
            /// Used for NPC damage ranking only.
            /// </summary>
            public Dictionary<long, StatisticData> DamageByPlayer { get; } = new();

            public NpcData(long npcId, string? name = null)
            {
                NpcId = npcId;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    Name = name!;
                }
                else
                {
                    Name = npcId.ToString();
                }
            }

            public void SetName(string name)
            {
                if (!string.IsNullOrWhiteSpace(name)) Name = name;
            }

            /// <summary>
            /// Record a player → NPC damage event.
            /// Skill is ignored; aggregates attacker and NPC taken stats.
            /// </summary>
            public void AddTakenFrom(
                long attackerUid,
                ulong damage,
                bool isCrit,
                bool isLucky,
                ulong hpLessen = 0,
                bool isMiss = false,
                bool isDead = false)
            {
                // Ranking/taken only: misses skip values but optionally count occurrences
                if (isMiss)
                {
                    var s = GetOrCreate(attackerUid);
                    s.RegisterMiss();
                    return;
                }

                var lessen = hpLessen > 0 ? hpLessen : damage;

                // NPC taken-damage aggregate
                TakenStats.AddRecord(damage, isCrit, isLucky, lessen);

                // Attacker aggregate (damage dealt to this NPC)
                GetOrCreate(attackerUid).AddRecord(damage, isCrit, isLucky, lessen);

                // Optional kill count; only increments, does not affect totals
                if (isDead) TakenStats.RegisterKill();

            }

            private StatisticData GetOrCreate(long uid)
            {
                if (!DamageByPlayer.TryGetValue(uid, out var stat))
                {
                    stat = new StatisticData();
                    DamageByPlayer[uid] = stat;
                }
                return stat;
            }

            /// <summary>Refresh realtime statistics for this NPC (including per-attacker stats).</summary>
            public void UpdateRealtime()
            {
                TakenStats.UpdateRealtimeStats();
                if (DamageByPlayer.Count == 0) return;
                foreach (var s in DamageByPlayer.Values)
                    s.UpdateRealtimeStats();
            }

            /// <summary>Reset all data for this NPC.</summary>
            public void Reset()
            {
                TakenStats.Reset();
                DamageByPlayer.Clear();
            }
        }

        /// <summary>
        /// NPC statistics manager: create/cache NPCs, record taken-damage events, and query NPC damage rankings.
        /// Notes:
        /// - Only covers NPC-side taken/ranking; player DPS still comes from PlayerDataManager.
        /// - When parsing an NPC target hit, call Players.AddDamage(...) (player side) and AddNpcTakenDamage(...) (NPC side) together.
        /// </summary>
        public sealed class NpcManager
        {
            private readonly object _lock = new();
            private readonly Dictionary<long, NpcData> _npcs = new();

            /// <summary>Player data manager (used to fetch nickname/combat power/profession/DPS).</summary>
            public PlayerDataManager Players { get; }

            public NpcManager(PlayerDataManager players)
            {
                Players = players;
            }

            /// <summary>Get or create an NPC.</summary>
            public NpcData GetOrCreate(long npcId, string? name = null)
            {
                lock (_lock)
                {
                    if (!_npcs.TryGetValue(npcId, out var npc))
                    {
                        npc = new NpcData(npcId, name);
                        _npcs[npcId] = npc;
                    }
                    else if (!string.IsNullOrWhiteSpace(name))
                    {
                        npc.SetName(name!);
                    }
                    return npc;
                }
            }

            /// <summary>Set the NPC name (optional).</summary>
            public void SetNpcName(long npcId, string name)
            {
                GetOrCreate(npcId).SetName(name);
                FullRecord.SetNpcName(npcId, name);
            }
            // 1) List all NPC IDs seen in the current combat
            public IReadOnlyList<long> GetAllNpcIds()
            {
                lock (_lock)
                {
                    if (_npcs.Count == 0) return Array.Empty<long>();
                    return _npcs.Keys.ToList();
                }
            }

            // 2) Fetch the NPC name (current combat)
            public string GetNpcName(long npcId)
            {
                lock (_lock)
                {
                    return _npcs.TryGetValue(npcId, out var n) ? (n.Name ?? $"NPC[{npcId}]") : $"NPC[{npcId}]";
                }
            }

            // 3) NPC taken-per-second for the encounter = Total / ActiveSeconds (maintained by StatisticData)
            public double GetNpcTakenPerSecond(long npcId)
            {
                var n = GetOrCreate(npcId);
                return n.TakenStats.GetTotalPerSecond();
            }

            // 4) Player-only DPS against this NPC = Total / ActiveSeconds (NPC-specific view)
            public double GetPlayerNpcOnlyDps(long npcId, long uid)
            {
                var n = GetOrCreate(npcId);
                if (!n.DamageByPlayer.TryGetValue(uid, out var s)) return 0;
                return s.GetTotalPerSecond();
            }
            /// <summary>
            /// Record a player → NPC damage event (skill-agnostic).
            /// Recommendation: also call Players.AddDamage(...) to keep player-side DPS and skill data in sync.
            /// </summary>
            public void AddNpcTakenDamage(
                long npcId,
                long attackerUid,
                long skillId,
                ulong damage,
                bool isCrit,
                bool isLucky,
                ulong hpLessen = 0,
                bool isMiss = false,
                bool isDead = false,
                string? npcName = null)
            {
                var npc = GetOrCreate(npcId, npcName);
                npc.AddTakenFrom(attackerUid, damage, isCrit, isLucky, hpLessen, isMiss, isDead);
                FullRecord.RecordNpcTakenDamage(npcId, attackerUid, damage, isCrit, isLucky, hpLessen, isMiss, isDead);


            }

            /// <summary>
            /// 刷新所有 NPC 的实时统计窗口（1s）。
            /// </summary>
            public void UpdateAllRealtime()
            {
                NpcData[] snapshot;
                lock (_lock)
                {
                    if (_npcs.Count == 0) return;
                    snapshot = _npcs.Values.ToArray();
                }
                foreach (var npc in snapshot) npc.UpdateRealtime();
            }

            /// <summary>
            /// 清空指定 NPC 的统计。
            /// </summary>
            public void ResetNpc(long npcId)
            {
                lock (_lock)
                {
                    if (_npcs.TryGetValue(npcId, out var npc))
                        npc.Reset();
                }
            }

            /// <summary>
            /// 清空所有 NPC 的统计。
            /// </summary>
            public void ResetAll()
            {
                lock (_lock)
                {
                    foreach (var npc in _npcs.Values) npc.Reset();
                    _npcs.Clear();
                }
            }

            // =========================
            // 查询接口
            // =========================

            /// <summary>
            /// Get a summary of an NPC's taken damage: total, realtime, peak, single-hit min/max, and last timestamp.
            /// </summary>
            public (
                ulong TotalTaken,
                ulong RealtimeTaken,
                ulong RealtimeTakenMax,
                ulong MaxSingleHit,
                ulong MinSingleHit,
                DateTime? LastTime
            ) GetNpcOverview(long npcId)
            {
                var npc = GetOrCreate(npcId);
                var s = npc.TakenStats;
                return (
                    s.Total,
                    s.RealtimeValue,
                    s.RealtimeMax,
                    s.MaxSingleHit,
                    s.MinSingleHit == ulong.MaxValue ? 0UL : s.MinSingleHit,
                    s.LastRecordTime
                );
            }

            /// <summary>
            /// Rank damage dealt to an NPC (descending by total).
            /// Also returns each player's overall DPS (same encounter-wide metric as Players).
            /// </summary>
            /// <param name="npcId">NPC ID.</param>
            /// <param name="topN">Number of entries (default 20).</param>
            /// <returns>List of (Uid, Nickname, CombatPower, Profession, DamageToNpc, TotalDps).</returns>
            public List<(long Uid, string Nickname, int CombatPower, string Profession, ulong DamageToNpc, double TotalDps)>
                GetNpcTopAttackers(long npcId, int topN = 20)
            {
                var npc = GetOrCreate(npcId);

                // 快照一次，避免锁外并发修改
                var items = npc.DamageByPlayer.ToArray();

                var ordered = items
                    .OrderByDescending(kv => kv.Value.Total)
                    .Take(topN)
                    .Select(kv =>
                    {
                        var uid = kv.Key;
                        var totalToNpc = kv.Value.Total;

                        // 从玩家管理器拿基础信息与DPS
                        var (nickname, power, profession) = Players.GetPlayerBasicInfo(uid);
                        var full = Players.GetPlayerFullStats(uid);
                        var totalDps = full.TotalDps;

                        return (Uid: uid,
                                Nickname: nickname,
                                CombatPower: power,
                                Profession: profession,
                                DamageToNpc: totalToNpc,
                                TotalDps: totalDps);
                    })
                    .ToList();

                return ordered;
            }

            /// <summary>
            /// Read realtime and total damage for a player against a specific NPC (useful for compact displays).
            /// </summary>
            /// <returns>(Total, Realtime, RealtimeMax, AvgPerHit, MaxHit, MinHit)</returns>
            public (ulong Total, ulong Realtime, ulong RealtimeMax, double AvgPerHit, ulong MaxHit, ulong MinHit)
                GetPlayerVsNpcStats(long npcId, long uid)
            {
                var npc = GetOrCreate(npcId);
                if (!npc.DamageByPlayer.TryGetValue(uid, out var s))
                    return (0, 0, 0, 0, 0, 0);

                return (
                    s.Total,
                    s.RealtimeValue,
                    s.RealtimeMax,
                    s.GetAveragePerHit(),
                    s.MaxSingleHit,
                    s.MinSingleHit == ulong.MaxValue ? 0UL : s.MinSingleHit
                );
            }
        }
        #endregion
    }
    #endregion

    #region Snapshot models

    /// <summary>
    /// Complete snapshot of a battle (for history lists, export, or review).
    /// </summary>
    public sealed class BattleSnapshot
    {
        /// <summary>UI label (e.g., end time).</summary>
        public string Label { get; init; } = "";

        /// <summary>Combat start time (falls back to EndedAt when unknown).</summary>
        public DateTime StartedAt { get; init; }

        /// <summary>Combat end / snapshot timestamp.</summary>
        public DateTime EndedAt { get; init; }

        /// <summary>Combat duration.</summary>
        public TimeSpan Duration { get; init; }

        /// <summary>Total team damage.</summary>
        public ulong TeamTotalDamage { get; init; }

        /// <summary>Total team healing.</summary>
        public ulong TeamTotalHealing { get; init; }

        /// <summary>UID → player snapshot dictionary.</summary>
        public Dictionary<long, SnapshotPlayer> Players { get; init; } = new();

        /// <summary>Total team taken damage.</summary>
        public ulong TeamTotalTakenDamage { get; init; }   // ★ New

    }

    /// <summary>
    /// Snapshot for a single player in the battle (includes aggregates and per-skill details).
    /// </summary>
    public sealed class SnapshotPlayer
    {
        /// <summary>Player UID.</summary>
        public long Uid { get; init; }

        /// <summary>Nickname.</summary>
        public string Nickname { get; init; } = "Unknown";

        /// <summary>Combat power.</summary>
        public int CombatPower { get; init; }

        /// <summary>Profession.</summary>
        public string Profession { get; init; } = "Unknown";

        public string? SubProfession { get; init; }


        /// <summary>Realtime DPS (window total).</summary>
        public ulong RealtimeDps { get; init; }

        /// <summary>Critical rate (0–100%).</summary>
        public double CritRate { get; init; }

        /// <summary>Lucky rate (0–100%).</summary>
        public double LuckyRate { get; init; }

        /// <summary>Damage dealt by critical hits.</summary>
        public ulong CriticalDamage { get; init; }

        /// <summary>Damage dealt by lucky hits.</summary>
        public ulong LuckyDamage { get; init; }

        /// <summary>Damage dealt by hits that were both critical and lucky.</summary>
        public ulong CritLuckyDamage { get; init; }

        /// <summary>Highest single hit.</summary>
        public ulong MaxSingleHit { get; init; }


        // 聚合
        /// <summary>Total damage (entire encounter).</summary>
        public ulong TotalDamage { get; init; }

        /// <summary>Total DPS (encounter average per second).</summary>
        public double TotalDps { get; init; }

        /// <summary>Total healing.</summary>
        public ulong TotalHealing { get; init; }

        /// <summary>Total HPS (encounter average per second).</summary>
        public double TotalHps { get; init; }

        /// <summary>Total taken damage (entire encounter).</summary>
        public ulong TakenDamage { get; init; }

        /// <summary>Timestamp of the last damage-side record.</summary>
        public DateTime? LastRecordTime { get; init; }

        /// <summary>Skill breakdown: damage.</summary>
        public List<SkillSummary> DamageSkills { get; init; } = new();

        /// <summary>Skill breakdown: healing.</summary>
        public List<SkillSummary> HealingSkills { get; init; } = new();

        /// <summary>Skill breakdown: taken damage.</summary>
        public List<SkillSummary> TakenSkills { get; init; } = new();

        /// <summary>
        /// Effective active time for damage (seconds).
        /// Accumulates only when the player deals damage; used for TotalDps = TotalDamage / ActiveSecondsDamage.
        /// </summary>
        public double ActiveSecondsDamage { get; init; }

        /// <summary>
        /// Effective active time for healing (seconds).
        /// Accumulates only when the player provides healing; used for TotalHps = TotalHealing / ActiveSecondsHealing.
        /// </summary>
        public double ActiveSecondsHealing { get; init; }

        /// <summary>
        /// Total healing from critical events (points).
        /// Aggregated from healing events marked critical.
        /// </summary>
        public ulong HealingCritical { get; init; }

        /// <summary>
        /// Total healing from lucky events (points).
        /// Aggregated from healing events marked lucky.
        /// </summary>
        public ulong HealingLucky { get; init; }

        /// <summary>
        /// Total healing that was both critical and lucky (points).
        /// Aggregated from events tagged as both.
        /// </summary>
        public ulong HealingCritLucky { get; init; }

        /// <summary>
        /// Realtime healing (HPS, points per second).
        /// Typically the instantaneous value from a 1-second or N-second window; 0 if not recorded.
        /// </summary>
        public ulong HealingRealtime { get; init; }

        /// <summary>
        /// Observed peak realtime healing (HPS, points per second).
        /// Uses the maximum from the realtime window; 0 if not tracked.
        /// </summary>
        public ulong HealingRealtimeMax { get; init; }

        /// <summary>
        /// Observed peak realtime damage (DPS, points per second).
        /// Uses the maximum from the realtime window; 0 if not tracked.
        /// </summary>
        public ulong RealtimeDpsMax { get; init; }


    }



    #endregion



}
