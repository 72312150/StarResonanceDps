namespace StarResonanceDpsAnalysis.WinForm.Plugin.DamageStatistics
{
    /// <summary>
    /// Session-wide recorder (spans multiple battles): increments in real time via AddDamage/AddHealing/AddTakenDamage hooks.
    /// - Start(): begin recording (ClearAll will not remove this state)
    /// - Stop(): end recording while keeping the data for on-demand snapshots
    /// - Reset(): manually clear the current session
    /// - TakeSnapshot(): create a “full session snapshot” (includes player aggregates and skill breakdowns)
    /// - GetTeamDps()/GetPlayerDps(): compute per-second damage for the session
    /// </summary>
    public static class FullRecord
    {
        // # Navigation / category index (documentation only):
        // #   1) Common helpers & number formatting: R2()
        // #   2) Shim read-only facades (aligned with StatisticData): Shim.StatsLike / Shim.PlayerLike / Shim.TakenOverviewLike
        // #   3) UI projection helpers: StatView / ToView() / MergeStats()
        // #   4) External statistics queries (matching StatisticData): GetPlayerDamageStats/HealingStats/TakenStats
        // #   5) Session state & control: IsRecording/StartedAt/EndedAt + Start/Stop/Reset/GetSessionTotalTimeSpan
        // #   6) Snapshot entry points & history: TakeSnapshot / SessionHistory / internal StopInternal/EffectiveEndTime
        // #   7) Write hooks (invoked by the decode pipeline): RecordDamage/RecordHealing/RecordTakenDamage + UpdateRealtimeDps
        // #   8) Snapshot & DPS public APIs: GetPlayersWithTotals/GetPlayersWithTotalsArray/GetTeamDps/GetPlayerDps/etc.
        // #   9) Snapshot time queries: GetAllPlayersDataBySnapshotTime/GetPlayerSkillsBySnapshotTime
        // #  10) Internal utilities: SessionSeconds/GetOrCreate/Accumulate/ToSkillSummary
        // #  11) Internal data structures: PlayerAcc / StatAcc

        // ======================================================================
        // # Section 1: Common helpers and numeric formatting
        // ======================================================================

        // # Common helper: round to two decimals (away from zero)
        private static double R2(double v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

        // ======================================================================
        // # Section 2: Shim read-only facades (aligned with StatisticData for UI reuse)
        // ======================================================================
        public static class Shim
        {
            // # — Read-only statistical object mirroring PlayerData.*Stats —
            // # Provides read-only views to upper layers without exposing internal accumulators
            public sealed class StatsLike
            {
                public ulong Total, Normal, Critical, Lucky;
                public int CountTotal, CountNormal, CountCritical, CountLucky;
                public ulong MaxSingleHit, MinSingleHit; // Min=0 means no record
                public double ActiveSeconds;             // Used when computing DPS/HPS

                public double GetAveragePerHit() => CountTotal > 0 ? R2((double)Total / CountTotal) : 0.0;
                public double GetCritRate() => CountTotal > 0 ? R2((double)CountCritical * 100.0 / CountTotal) : 0.0;
                public double GetLuckyRate() => CountTotal > 0 ? R2((double)CountLucky * 100.0 / CountTotal) : 0.0;
            }

            // # — Facade similar to StatisticData._manager.GetOrCreate(uid) —
            // # Allows callers to consume session stats the same way they use live stats
            public sealed class PlayerLike
            {
                public StatsLike DamageStats { get; init; } = new();
                public StatsLike HealingStats { get; init; } = new();
                public StatsLike TakenStats { get; init; } = new();

                public double GetTotalDps() => DamageStats.ActiveSeconds > 0 ? R2(DamageStats.Total / DamageStats.ActiveSeconds) : 0.0;
                public double GetTotalHps() => HealingStats.ActiveSeconds > 0 ? R2(HealingStats.Total / HealingStats.ActiveSeconds) : 0.0;
            }

            // # Taken-damage overview (used by the UI summary header)
            public sealed class TakenOverviewLike
            {
                public ulong Total { get; init; }
                public double AvgTakenPerSec { get; init; }
                public ulong MaxSingleHit { get; init; }
                public ulong MinSingleHit { get; init; }
            }

            // # Internal: convert an accumulator into a read-only StatsLike
            private static StatsLike From(StatAcc s)
            {
                // # Project the internal StatAcc into a read-only StatsLike for UI/external display
                return new StatsLike
                {
                    Total = s.Total,
                    Normal = s.Normal,
                    Critical = s.Critical,
                    Lucky = s.Lucky + s.CritLucky,   // Merge lucky + crit-lucky contributions
                    CountTotal = s.CountTotal,
                    CountNormal = s.CountNormal,
                    CountCritical = s.CountCritical,
                    CountLucky = s.CountLucky,
                    MaxSingleHit = s.MaxSingleHit,
                    MinSingleHit = s.MinSingleHit, // 0 indicates no record
                    ActiveSeconds = s.ActiveSeconds
                };
            }

            // # Internal: aggregate a set of StatAcc (e.g., merge per-skill taken damage into player totals)
            private static StatAcc MergeStats(IEnumerable<StatAcc> items)
            {
                // # Merge multiple StatAcc entries into a player-level aggregate (taken damage, etc.)
                var acc = new StatAcc();
                ulong min = 0; bool hasMin = false;
                double maxActiveSecs = 0;

                foreach (var s in items)
                {
                    acc.Total += s.Total;
                    acc.Normal += s.Normal;
                    acc.Critical += s.Critical;
                    acc.Lucky += s.Lucky;
                    acc.CritLucky += s.CritLucky;
                    acc.HpLessen += s.HpLessen;

                    acc.CountNormal += s.CountNormal;
                    acc.CountCritical += s.CountCritical;
                    acc.CountLucky += s.CountLucky;
                    acc.CountTotal += s.CountTotal;

                    if (s.MaxSingleHit > acc.MaxSingleHit) acc.MaxSingleHit = s.MaxSingleHit;
                    if (s.MinSingleHit > 0 && (!hasMin || s.MinSingleHit < min)) { min = s.MinSingleHit; hasMin = true; }
                    if (s.ActiveSeconds > maxActiveSecs) maxActiveSecs = s.ActiveSeconds;
                }

                acc.MinSingleHit = hasMin ? min : 0;
                acc.ActiveSeconds = maxActiveSecs; // Use the maximum active time to avoid inflating the denominator
                return acc;
            }

            /// <summary>
            /// Obtain a read-only player view by projecting FullRecord data into a StatisticData-like structure.
            /// </summary>
            public static PlayerLike GetOrCreate(long uid)
            {
                // # Use FullRecord accumulators to return a facade similar to StatisticData
                lock (_sync)
                {
                    if (!_players.TryGetValue(uid, out var p))
                        return new PlayerLike();

                    // Damage / Healing pulled directly from the FullRecord player aggregators
                    var dmg = From(p.Damage);
                    var heal = From(p.Healing);

                    // Taken: merge per-skill stats; if none exist, fall back to TakenDamage + session duration
                    StatAcc takenAcc;
                    if (p.TakenSkills != null && p.TakenSkills.Count > 0)
                        takenAcc = MergeStats(p.TakenSkills.Values);
                    else
                        takenAcc = new StatAcc
                        {
                            Total = p.TakenDamage,
                            ActiveSeconds = Math.Max(0.0, GetSessionTotalTimeSpan().TotalSeconds)
                        };
                    var taken = From(takenAcc);

                    return new PlayerLike
                    {
                        DamageStats = dmg,
                        HealingStats = heal,
                        TakenStats = taken
                    };
                }
            }

            /// <summary>
            /// Taken damage overview (total, per-second average, max/min hit).
            /// </summary>
            public static TakenOverviewLike GetPlayerTakenOverview(long uid)
            {
                // # Overview of taken damage: total, per-second average, max/min
                var p = GetOrCreate(uid);
                var t = p.TakenStats;
                double perSec = t.ActiveSeconds > 0 ? R2(t.Total / t.ActiveSeconds) : 0.0;

                return new TakenOverviewLike
                {
                    Total = t.Total,
                    AvgTakenPerSec = perSec,
                    MaxSingleHit = t.MaxSingleHit,
                    MinSingleHit = t.MinSingleHit
                };
            }
        }

        // ======================================================================
        // # Section 3: UI read-only statistic projections (StatView mapping/merging)
        // ======================================================================

        // # === UI read-only StatView representation ===
        public readonly record struct StatView(
            ulong Total,
            ulong Normal,
            ulong Critical,
            ulong Lucky,
            int CountTotal,
            int CountNormal,
            int CountCritical,
            int CountLucky,
            ulong MaxSingleHit,
            ulong MinSingleHit,
            double PerSecond,      // = Total / ActiveSeconds(>0 ?)
            double AveragePerHit,  // = Total / CountTotal(>0 ?)
            double CritRate,       // Percentage with two decimal places
            double LuckyRate       // %
        );

        // # Convert an internal accumulator into a UI-facing view
        private static StatView ToView(StatAcc s)
        {
            // # Map the accumulator into a UI-ready view (per-second, averages, crit/lucky rates)
            int ct = s.CountTotal;
            double secs = s.ActiveSeconds > 0 ? s.ActiveSeconds : 0;
            double perSec = secs > 0 ? R2(s.Total / secs) : 0;
            double avg = ct > 0 ? R2((double)s.Total / ct) : 0;
            double crit = ct > 0 ? R2((double)s.CountCritical * 100.0 / ct) : 0.0;
            double lucky = ct > 0 ? R2((double)s.CountLucky * 100.0 / ct) : 0.0;

            ulong min = s.MinSingleHit; // In StatAcc, Min=0 means “not set yet”, so return 0
            ulong luckyCombined = s.Lucky + s.CritLucky;   // ★ Key: combine the lucky buckets
            return new StatView(
                Total: s.Total,
                Normal: s.Normal,
                Critical: s.Critical,
                Lucky: luckyCombined,
                CountTotal: s.CountTotal,
                CountNormal: s.CountNormal,
                CountCritical: s.CountCritical,
                CountLucky: s.CountLucky,
                MaxSingleHit: s.MaxSingleHit,
                MinSingleHit: min,
                PerSecond: perSec,
                AveragePerHit: avg,
                CritRate: crit,
                LuckyRate: lucky
            );
        }

        // # Merge a set of StatAcc entries (used for taken damage: collapse per-skill stats into a player total)
        private static StatAcc MergeStats(IEnumerable<StatAcc> items)
        {
            var acc = new StatAcc();
            ulong min = 0;
            bool hasMin = false;
            double maxActiveSecs = 0;

            foreach (var s in items)
            {
                acc.Total += s.Total;
                acc.Normal += s.Normal;
                acc.Critical += s.Critical;
                acc.Lucky += s.Lucky;
                acc.CritLucky += s.CritLucky;
                acc.HpLessen += s.HpLessen;

                acc.CountNormal += s.CountNormal;
                acc.CountCritical += s.CountCritical;
                acc.CountLucky += s.CountLucky;
                acc.CountTotal += s.CountTotal;

                if (s.MaxSingleHit > acc.MaxSingleHit) acc.MaxSingleHit = s.MaxSingleHit;
                if (s.MinSingleHit > 0 && (!hasMin || s.MinSingleHit < min)) { min = s.MinSingleHit; hasMin = true; }

                if (s.ActiveSeconds > maxActiveSecs) maxActiveSecs = s.ActiveSeconds;
            }

            acc.MinSingleHit = hasMin ? min : 0;
            acc.ActiveSeconds = maxActiveSecs; // Use the maximum active seconds to avoid inflating totals
            return acc;
        }

        // ======================================================================
        // # Section 4: External statistics queries (aligned with StatisticData)
        // ======================================================================

        /// <summary>Retrieve a player's session-wide damage statistics (UI view).</summary>
        public static StatView GetPlayerDamageStats(long uid)
        {
            lock (_sync)
            {
                if (_players.TryGetValue(uid, out var p))
                    return ToView(p.Damage);
                return default;
            }
        }

        /// <summary>Retrieve a player's session-wide healing statistics (UI view).</summary>
        public static StatView GetPlayerHealingStats(long uid)
        {
            lock (_sync)
            {
                if (_players.TryGetValue(uid, out var p))
                    return ToView(p.Healing);
                return default;
            }
        }

        /// <summary>Retrieve a player's session-wide taken-damage statistics (UI view).</summary>
        public static StatView GetPlayerTakenStats(long uid)
        {
            lock (_sync)
            {
                if (_players.TryGetValue(uid, out var p))
                {
                    if (p.TakenSkills.Count > 0)
                        return ToView(MergeStats(p.TakenSkills.Values));

                    // When no per-skill details exist, return at least Total; fall back to session duration for seconds
                    var secs = GetSessionTotalTimeSpan().TotalSeconds; // Uses the existing session-duration API
                    var fake = new StatAcc { Total = p.TakenDamage, ActiveSeconds = secs > 0 ? secs : 0 };
                    return ToView(fake);
                }
                return default;
            }
        }

        // ======================================================================
        // # Section 4.5: Death statistics queries (based on TakenSkills.CountDead)
        // ======================================================================

        /// <summary>Get the total number of deaths for the team during the current session.</summary>
        public static int GetTeamDeathCount()
        {
            int teamDeaths = 0;
            PlayerAcc[] playersSnapshot;
            lock (_sync) { playersSnapshot = _players.Values.ToArray(); }

            foreach (var p in playersSnapshot)
                foreach (var kv in p.TakenSkills)
                    teamDeaths += kv.Value.CountDead;

            return teamDeaths;
        }

        /// <summary>Get the number of deaths for a specific player during the current session.</summary>
        public static int GetPlayerDeathCount(long uid)
        {
            lock (_sync)
            {
                if (!_players.TryGetValue(uid, out var p)) return 0;
                int deaths = 0;
                foreach (var kv in p.TakenSkills)
                    deaths += kv.Value.CountDead;
                return deaths;
            }
        }

        /// <summary>
        /// Return a death-count roster for all players (descending by default).
        /// When includeZero=false, players with zero deaths are filtered out.
        /// </summary>
        public static List<(long Uid, string Nickname, int CombatPower, string Profession, string? SubProfession, int Deaths)>
            GetAllPlayerDeathCounts(bool includeZero = false)
        {
            var result = new List<(long, string, int, string, string?, int)>();
            PlayerAcc[] playersSnapshot;
            lock (_sync) { playersSnapshot = _players.Values.ToArray(); }

            foreach (var p in playersSnapshot)
            {
                int deaths = 0;
                foreach (var kv in p.TakenSkills)
                    deaths += kv.Value.CountDead;

                if (includeZero || deaths > 0)
                    result.Add((p.Uid, p.Nickname, p.CombatPower, p.Profession, p.SubProfession, deaths));
            }

            return result.OrderByDescending(x => x.Item6).ToList(); // Sort descending by death count
        }

        /// <summary>
        /// Get a per-skill breakdown of a player's deaths (descending).
        /// Returns tuples of SkillId, SkillName, Deaths.
        /// </summary>
        public static List<(long SkillId, string SkillName, int Deaths)>
            GetPlayerDeathBreakdownBySkill(long uid)
        {
            lock (_sync)
            {
                if (!_players.TryGetValue(uid, out var p) || p.TakenSkills.Count == 0)
                    return new();

                var list = new List<(long, string, int)>(p.TakenSkills.Count);
                foreach (var kv in p.TakenSkills)
                {
                    var sid = kv.Key;
                    var s = kv.Value;
                    if (s.CountDead <= 0) continue;

                    var name = SkillBook.Get(sid).Name;
                    list.Add((sid, name, s.CountDead));
                }
                return list.OrderByDescending(x => x.Item3).ToList();

            }
        }
        // ======================================================================
        // # Section 5: Row definitions exposed for external binding
        // ======================================================================

        // # Row structure for external binding (extend fields as needed)
        public sealed record FullPlayerTotal(
                long Uid,
                string Nickname,
                int CombatPower,
                string Profession,
                string? SubProfession,
                ulong TotalDamage,
                ulong TotalHealing,
                ulong TakenDamage,
                double Dps,   // Session DPS (damage only)
                double Hps    // Session HPS
            );

        // ======================================================================
        // # Section 6: Session state and control (start / stop / reset / duration)
        // ======================================================================

        // # Session state fields — track whether recording is active and start/end timestamps
        public static bool IsRecording { get; private set; }
        public static DateTime? StartedAt { get; private set; }
        public static DateTime? EndedAt { get; private set; }

        // # Fully disable the “idle auto-stop” mechanism: no longer track LastEventAt or use timers.
        // # Field retained for compatibility; remove if no longer referenced.
        private static readonly bool DisableIdleAutoStop = true;

        // # Persistent accumulators: session aggregates spanning battles
        private static readonly Dictionary<long, PlayerAcc> _players = new();

        // # ★ Session snapshot history (pushed on Stop or automatic stop)
        private static readonly List<FullSessionSnapshot> _sessionHistory = new();
        public static IReadOnlyList<FullSessionSnapshot> SessionHistory => _sessionHistory; // Exposed as read-only for UI consumption

        // # New: realtime team DPS (for UI display)
        public static double TeamRealtimeDps { get; private set; }     // Based on effective session seconds (damage only)

        // # Control region (start / stop / reset) ------------------------------------------------------
        #region Control

        /// <summary>
        /// Start session-wide recording:
        /// - If a session is already running, return immediately;
        /// - On the very first start, capture StartedAt;
        /// - Clear EndedAt to indicate the session is active.
        /// </summary>
        public static void Start()
        {
            if (IsRecording) return;

            IsRecording = true;
            if (StartedAt is null) StartedAt = DateTime.Now; // Track the first start timestamp
            EndedAt = null;
        }

        private static readonly object _sync = new();

        /// <summary>
        /// Manually stop session recording without clearing historical data:
        /// - If currently recording, emit a session snapshot;
        /// - Clear only the “current session” aggregates (snapshots remain intact);
        /// - Reset time markers so a new session can begin.
        /// </summary>
        public static void Stop()
        {
            lock (_sync)
            {
                // 1) 若在录制中，先入快照（保留历史）
                if (IsRecording)
                    StopInternal(auto: false);

                // 2) 清【当前会话】累计（不动历史）
                _players.Clear();
                TeamRealtimeDps = 0;
                _npcs.Clear();          // Reset NPC session accumulators


                // 3) 重置时间基，准备新会话
                StartedAt = null;
                EndedAt = null;
            }
        }

        /// <summary>
        /// Clear all stored session snapshots
        /// </summary>
        public static void ClearSessionHistory()
        {
            lock (_sync)
            {
                _sessionHistory.Clear();
            }
        }



        /// <summary>
        /// Reset the current session:
        /// - If a session is active or data exists, persist a snapshot first;
        /// - Clear the current session aggregates and time markers;
        /// - Set IsRecording to true to enter a fresh recording state.
        /// </summary>
        public static void Reset(bool preserveHistory = true)
        {

            if (AppConfig.ClearAllDataWhenSwitch && preserveHistory) return; // Honor global config: skip clearing when switch protection is on
            lock (_sync)
            {
                // 1) 如有进行中的或已有数据的会话，先入一条快照（不影响历史）
                bool hasData = _players.Count > 0 || StartedAt != null;
                if (hasData)
                {
                    // StopInternal: lock in EndedAt, create a snapshot, append to _sessionHistory
                    StopInternal(auto: false);
                }

                // 2) 清【当前会话】累计（不动历史，除非显式要求清）
                _players.Clear();
                TeamRealtimeDps = 0;
                _npcs.Clear();          // Reset NPC accumulators

                // 3) 清时间基与录制状态
                StartedAt = DateTime.Now;   // Resume with a fresh start time
                EndedAt = null;
                IsRecording = true;

                // 4) 可选：清历史（当前保留）

            }
        }

        /// <summary>
        /// Get the total duration of the current session (TimeSpan).
        /// - While recording: Now - StartedAt
        /// - After stopping: EndedAt - StartedAt
        /// </summary>
        public static TimeSpan GetSessionTotalTimeSpan()
        {
            if (StartedAt is null) return TimeSpan.Zero;
            DateTime end = IsRecording ? DateTime.Now : (EndedAt ?? DateTime.Now);
            var duration = end - StartedAt.Value;
            return duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        }

        /// <summary>
        /// 获取指定玩家的全程技能统计（基于当前时刻快照）。
        /// 返回三个只读列表（伤害技能/治疗技能/承伤技能）。
        /// </summary>
        public static (IReadOnlyList<SkillSummary> DamageSkills,
                       IReadOnlyList<SkillSummary> HealingSkills,
                       IReadOnlyList<SkillSummary> TakenSkills)
            GetPlayerSkills(long uid)
        {
            var snap = TakeSnapshot();
            if (snap.Players.TryGetValue(uid, out var p))
            {
                return (p.DamageSkills, p.HealingSkills, p.TakenSkills);
            }
            return (Array.Empty<SkillSummary>(), Array.Empty<SkillSummary>(), Array.Empty<SkillSummary>());
        }

        #endregion

        // ======================================================================
        // # Category 7: list retrieval / aggregation helpers for UI binding and display
        // ======================================================================

        /// <summary>
        /// Retrieve the current session totals per player (sorted by total damage descending).
        /// When includeZero=false, players with zero damage/healing/taken values are filtered out.
        /// DPS/HPS denominators use each player’s active seconds (falling back to session duration when inactive).
        /// </summary>
        public static List<FullPlayerTotal> GetPlayersWithTotals(bool includeZero = false)
        {
            var snap = TakeSnapshot();

            // Do not rely on snap.Duration as a universal denominator
            var list = new List<FullPlayerTotal>(snap.Players.Count);
            foreach (var kv in snap.Players)
            {
                var p = kv.Value;

                // Determine effective denominators (fallback to session length as a safety net)
                var secsDmg = p.ActiveSecondsDamage > 0 ? p.ActiveSecondsDamage : snap.Duration.TotalSeconds;
                var secsHeal = p.ActiveSecondsHealing > 0 ? p.ActiveSecondsHealing : snap.Duration.TotalSeconds;

                // Preserve the includeZero filtering logic
                if (!includeZero && p.TotalDamage == 0 && p.TotalHealing == 0 && p.TakenDamage == 0)
                    continue;

                list.Add(new FullPlayerTotal(
                    Uid: p.Uid,
                    Nickname: p.Nickname,
                    CombatPower: p.CombatPower,
                    SubProfession: p.SubProfession,
                    Profession: p.Profession,
                    TotalDamage: p.TotalDamage,
                    TotalHealing: p.TotalHealing,
                    TakenDamage: p.TakenDamage,
                    Dps: secsDmg > 0 ? R2(p.TotalDamage / secsDmg) : 0,
                    Hps: secsHeal > 0 ? R2(p.TotalHealing / secsHeal) : 0
                ));
            }

            return list.OrderByDescending(r => r.TotalDamage).ToList();
        }

        /// <summary>
        /// Return the overall combat duration (HH:mm:ss based on the maximum damage active time),
        /// primarily for UI text display.
        /// </summary>
        public static string GetEffectiveDurationString()
        {
            double activeSeconds = 0;

            // Snapshot the collection inside the lock; perform calculations afterwards
            PlayerAcc[] playersSnapshot;
            lock (_sync)
            {
                playersSnapshot = _players.Values.ToArray();
            }

            foreach (var p in playersSnapshot)
            {
                if (p.Damage.ActiveSeconds > activeSeconds)
                    activeSeconds = p.Damage.ActiveSeconds;
            }

            var ts = TimeSpan.FromSeconds(activeSeconds);
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        /// <summary>
        /// Convenience helper so callers can directly bind via ToArray (mirrors StatisticData usage).
        /// </summary>
        public static FullPlayerTotal[] GetPlayersWithTotalsArray(bool includeZero = false)
            => GetPlayersWithTotals(includeZero).ToArray();

        // ======================================================================
        // # Category 8: internal stop handling and effective end time (snapshot helpers)
        // ======================================================================

        // # Internal helper
        /// <summary>
        /// Internal stop wrapper:
        /// - auto=true indicates the stop was triggered by idle timeout;
        /// - Fix EndedAt to mark the session end;
        /// - Generate a snapshot and append it to history.
        /// </summary>
        private static void StopInternal(bool auto)
        {
            IsRecording = false;
            EndedAt = DateTime.Now;

            bool hasAnyData;
            PlayerAcc[] playersSnapshot;
            lock (_sync)
            {
                playersSnapshot = _players.Values.ToArray();
            }
            hasAnyData = playersSnapshot.Any(p =>
                p.Damage.Total > 0 || p.Healing.Total > 0 || p.TakenDamage > 0);

            if (!hasAnyData) return;

            var snapshot = TakeSnapshot(); // Snapshot-safe even when called from auto-stop
            if (snapshot.Duration.TotalSeconds >= 1 || hasAnyData)
            {
                lock (_sync) // Guard history list mutations as well
                {
                    _sessionHistory.Add(snapshot);
                }
            }
        }

        // # Internal helper
        /// <summary>
        /// Determine the “effective end time”:
        /// - When recording, use the current time as the endpoint;
        /// - After stopping, reuse EndedAt.
        /// </summary>
        private static DateTime EffectiveEndTime()
        {
            if (StartedAt is null) return DateTime.Now;
            return IsRecording ? DateTime.Now : (EndedAt ?? DateTime.Now);
        }

        // ======================================================================
        // # Category 9: write entry points invoked by the decoding/event pipeline
        // ======================================================================

        #region Embedded write APIs (only require a single call from the pipeline)

        /// <summary>
        /// Record a damage event:
        /// - Aggregate into player totals and per-skill buckets;
        /// - Update realtime DPS (based on damage-active seconds);
        /// - Ignore zero values.
        /// </summary>
        public static void RecordDamage(
            long uid, long skillId, ulong value, bool isCrit, bool isLucky, ulong hpLessen,
            string nickname, int combatPower, string profession,
            string? damageElement = null, bool isCauseLucky = false, string? subProfession = null
        )
        {
            if (!IsRecording || value == 0) return;
            var p = GetOrCreate(uid, nickname, combatPower, profession, subProfession);

            // ① Aggregate at the player level, preserving isCauseLucky
            Accumulate(p.Damage, value, isCrit, isLucky, hpLessen, isCauseLucky);

            // ② Aggregate per skill, also tracking isCauseLucky
            var s = p.DamageSkills.TryGetValue(skillId, out var tmp) ? tmp : (p.DamageSkills[skillId] = new StatAcc());
            Accumulate(s, value, isCrit, isLucky, hpLessen, isCauseLucky);

            // ③ Optional: break down by element when provided
            if (!string.IsNullOrEmpty(damageElement))
            {
                if (!p.DamageSkillsByElement.TryGetValue(skillId, out var byElem))
                    byElem = p.DamageSkillsByElement[skillId] = new Dictionary<string, StatAcc>();

                if (!byElem.TryGetValue(damageElement, out var es))
                    es = byElem[damageElement] = new StatAcc();

                Accumulate(es, value, isCrit, isLucky, hpLessen, isCauseLucky);
            }

            // Update realtime DPS (same behavior as before)
            UpdateRealtimeDps(p);

        }

        /// <summary>
        /// Record a healing event:
        /// - Aggregate into player totals and per-skill buckets;
        /// - Update realtime healing metrics (RealtimeDpsHealing);
        /// - Ignore zero values.
        /// </summary>
        public static void RecordHealing(
            long uid, long skillId, ulong value, bool isCrit, bool isLucky,
            string nickname, int combatPower, string profession,
            string? damageElement = null, bool isCauseLucky = false, ulong targetUuid = 0, string? subProfession = null
        )

        {
            if (!IsRecording || value == 0) return;
            var p = GetOrCreate(uid, nickname, combatPower, profession, subProfession);

            // Player aggregate
            Accumulate(p.Healing, value, isCrit, isLucky, 0, isCauseLucky);

            // Per-skill aggregate
            var s = p.HealingSkills.TryGetValue(skillId, out var tmp) ? tmp : (p.HealingSkills[skillId] = new StatAcc());
            Accumulate(s, value, isCrit, isLucky, 0, isCauseLucky);

            // Optional: per-element breakdown
            if (!string.IsNullOrEmpty(damageElement))
            {
                if (!p.DamageSkillsByElement.TryGetValue(skillId, out var byElem)) // Reuse the same map; split if you prefer separate structures
                    byElem = p.DamageSkillsByElement[skillId] = new Dictionary<string, StatAcc>();
                if (!byElem.TryGetValue(damageElement, out var es))
                    es = byElem[damageElement] = new StatAcc();
                Accumulate(es, value, isCrit, isLucky, 0, isCauseLucky);
            }

            // Optional: per-target breakdown
            if (targetUuid != 0)
            {
                if (!p.HealingSkillsByTarget.TryGetValue(skillId, out var byTarget))
                    byTarget = p.HealingSkillsByTarget[skillId] = new Dictionary<ulong, StatAcc>();
                if (!byTarget.TryGetValue(targetUuid, out var ts))
                    ts = byTarget[targetUuid] = new StatAcc();
                Accumulate(ts, value, isCrit, isLucky, 0, isCauseLucky);
            }

            UpdateRealtimeDps(p);

        }

        /// <summary>
        /// Record a taken-damage event:
        /// - Aggregate total and per-skill taken damage (hpLessen applied);
        /// - Exclude from team/player DPS (used solely for defensive stats and UI display);
        /// - Ignore zero values unless hpLessen provides a fallback.
        /// </summary>
        public static void RecordTakenDamage(
            long uid, long skillId, ulong value, bool isCrit, bool isLucky, ulong hpLessen,
            string nickname, int combatPower, string profession,
            int damageSource = 0, bool isMiss = false, bool isDead = false
        )

        {
            if (!IsRecording) return; // Note: taken-damage value can be 0 (blocks/shields); do not exit prematurely
            var p = GetOrCreate(uid, nickname, combatPower, profession);

            // Per-skill bucket
            var s = p.TakenSkills.TryGetValue(skillId, out var tmp) ? tmp : (p.TakenSkills[skillId] = new StatAcc());

            // ① Miss: increment counters only — no value recorded
            if (isMiss)
            {
                s.CountMiss++;
                return;
            }

            // ② Dead: count occurrences and continue recording values when present
            if (isDead)
                s.CountDead++;

            // hpLessen serves as the fallback magnitude
            var lessen = hpLessen > 0 ? hpLessen : value;

            // Accumulate actual taken damage at the player level
            p.TakenDamage += lessen;

            // Only persist meaningful taken damage (value may be 0, depending on protocol semantics)
            if (value > 0 || lessen > 0)
            {
                Accumulate(s, value, isCrit, isLucky, lessen, false /* cause-lucky is typically not applied to taken damage */);
            }

            // Taken damage does not enter team DPS; just refresh realtime display for the player
            UpdateRealtimeDps(p, includeHealing: false);

        }

        /// <summary>
        /// Update realtime DPS for players and the team:
        /// - Player totals: damage/healing each use their own ActiveSeconds;
        /// - Per-skill: realtime DPS calculated from each skill’s ActiveSeconds;
        /// - Team: total damage divided by the maximum damage-active seconds across the roster.
        /// </summary>
        private static void UpdateRealtimeDps(PlayerAcc p, bool includeHealing = true)
        {
            // Player aggregate based on effective event duration
            var dmgSecs = p.Damage.ActiveSeconds;
            p.RealtimeDpsDamage = dmgSecs > 0 ? R2(p.Damage.Total / dmgSecs) : 0;

            if (includeHealing)
            {
                var healSecs = p.Healing.ActiveSeconds;
                p.RealtimeDpsHealing = healSecs > 0 ? R2(p.Healing.Total / healSecs) : 0;
            }

            // Per-skill snapshots (optionally using each skill’s active duration)
            foreach (var kv in p.DamageSkills)
            {
                var s = kv.Value;
                var secs = s.ActiveSeconds;
                s.RealtimeDps = secs > 0 ? R2(s.Total / secs) : 0;
            }
            if (includeHealing)
            {
                foreach (var kv in p.HealingSkills)
                {
                    var s = kv.Value;
                    var secs = s.ActiveSeconds;
                    s.RealtimeDps = secs > 0 ? R2(s.Total / secs) : 0;
                }
            }

            // Team realtime DPS: rely on the maximum active duration to reflect actual fight time
            double teamActiveSecs = 0;
            foreach (var pp in _players.Values)
                teamActiveSecs = Math.Max(teamActiveSecs, pp.Damage.ActiveSeconds);

            ulong teamTotal = 0;
            foreach (var pp in _players.Values) teamTotal += pp.Damage.Total;

            TeamRealtimeDps = teamActiveSecs > 0 ? R2(teamTotal / teamActiveSecs) : 0;
        }

        #endregion


        // ======================================================================
        // # Category 9.5: session-wide NPC statistics (aggregated by attacker, not by skill)
        // ======================================================================
        #region NPC (Session-wide)

        private sealed class NpcAcc
        {
            public long NpcId { get; }
            public string Name { get; set; } = "Unknown NPC";
            public StatAcc Taken { get; } = new();                       // Total damage taken by this NPC
            public Dictionary<long, StatAcc> DamageByPlayer { get; } = new(); // Aggregated attacker → NPC damage

            public NpcAcc(long id) { NpcId = id; }
        }

        private static readonly Dictionary<long, NpcAcc> _npcs = new();

        private static NpcAcc GetOrCreateNpc(long npcId, string? name = null)
        {
            if (!_npcs.TryGetValue(npcId, out var n))
            {
                n = new NpcAcc(npcId);
                _npcs[npcId] = n;
            }
            if (!string.IsNullOrWhiteSpace(name)) n.Name = name!;
            return n;
        }

        private static StatAcc GetNpcPlayerAcc(NpcAcc n, long uid)
        {
            if (!n.DamageByPlayer.TryGetValue(uid, out var s))
                s = n.DamageByPlayer[uid] = new StatAcc();
            return s;
        }

        /// <summary>
        /// Session-wide record of “player → NPC” damage (skill-agnostic):
        /// - value/hpLessen follow the existing pipeline semantics;
        /// - Miss events only increment counters in the player→NPC bucket (no values stored);
        /// - Dead events only increment counters on the NPC Taken side.
        /// </summary>
        public static void RecordNpcTakenDamage(
            long npcId,
            long attackerUid,
            ulong value,
            bool isCrit,
            bool isLucky,
            ulong hpLessen = 0,
            bool isMiss = false,
            bool isDead = false,
            string? npcName = null
        )
        {
            if (!IsRecording) return;

            var n = GetOrCreateNpc(npcId, npcName);
            var lessen = hpLessen > 0 ? hpLessen : value;

            if (isMiss)
            {
                // Count misses only within the attacker → NPC bucket
                GetNpcPlayerAcc(n, attackerUid).CountMiss++;
                return;
            }
            if (isDead) n.Taken.CountDead++;

            // Aggregate NPC taken damage
            Accumulate(n.Taken, value, isCrit, isLucky, lessen, false);

            // 攻击者对该 NPC 的聚合（可计算该玩家对该NPC的专属DPS）
            var ps = GetNpcPlayerAcc(n, attackerUid);
            Accumulate(ps, value, isCrit, isLucky, lessen, false);
        }

        /// <summary>Assign an optional display name for an NPC.</summary>
        public static void SetNpcName(long npcId, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            GetOrCreateNpc(npcId).Name = name;
        }

        /// <summary>Retrieve the set of NPC IDs encountered during the current session.</summary>
        public static IReadOnlyList<long> GetAllNpcIds() => _npcs.Keys.ToList();

        /// <summary>
        /// 读取 NPC 全程承伤概览：总量/每秒/最大最小单次/最后时间。
        /// PerSec relies on the NPC’s own ActiveSeconds (advanced via Accumulate).
        /// </summary>
        public static (ulong Total, double PerSec, ulong MaxHit, ulong MinHit, DateTime? LastTime, string Name)
            GetNpcOverview(long npcId)
        {
            if (!_npcs.TryGetValue(npcId, out var n)) return (0, 0, 0, 0, null, "Unknown NPC");
            var s = n.Taken;
            var secs = s.ActiveSeconds > 0 ? s.ActiveSeconds : 0;
            var per = secs > 0 ? R2(s.Total / secs) : 0;
            var min = s.MinSingleHit; // 0 indicates no record
            return (s.Total, per, s.MaxSingleHit, min, s.LastAt, n.Name);
        }

        /// <summary>
        /// Produce a ranking of players damaging a specific NPC (sorted by total damage).
        /// Also returns: player-wide DPS (GetPlayerDps) and NPC-specific DPS (based on that NPC bucket’s ActiveSeconds).
        /// </summary>
        public static List<(long Uid, string Nickname, int CombatPower, string Profession,
                           ulong DamageToNpc, double PlayerDps, double NpcOnlyDps)>
            GetNpcTopAttackers(long npcId, int topN = 20)
        {
            if (!_npcs.TryGetValue(npcId, out var n) || n.DamageByPlayer.Count == 0) return new();

            // Snapshot the dictionary to avoid mutations during iteration
            var arr = n.DamageByPlayer.ToArray();

            // Capture a snapshot of basic player info
            Dictionary<long, (string Nick, int Power, string Prof)> baseInfo;
            lock (_sync)
            {
                baseInfo = _players.ToDictionary(
                    kv => kv.Key,
                    kv => (kv.Value.Nickname, kv.Value.CombatPower, kv.Value.Profession)
                );
            }

            return arr
                .OrderByDescending(kv => kv.Value.Total)
                .Take(topN)
                .Select(kv =>
                {
                    var uid = kv.Key;
                    var s = kv.Value;
                    var nick = baseInfo.TryGetValue(uid, out var bi) ? bi.Nick : "Unknown";
                    var power = baseInfo.TryGetValue(uid, out bi) ? bi.Power : 0;
                    var prof = baseInfo.TryGetValue(uid, out bi) ? bi.Prof : "Unknown";

                    var playerDps = GetPlayerDps(uid); // Session-wide DPS (damage side)
                    var npcOnlyDps = s.ActiveSeconds > 0 ? R2(s.Total / s.ActiveSeconds) : 0;

                    return (uid, nick, power, prof, s.Total, playerDps, npcOnlyDps);
                })
                .ToList();
        }

        #endregion


        // ======================================================================
        // # Category 10: snapshots & DPS exports (public interfaces for snapshot generation)
        // ======================================================================

        /// <summary>
        /// Generate a session snapshot for the current moment:
        /// - Use EffectiveEndTime() for the end timestamp;
        /// - Aggregate team-wide damage/healing/taken metrics;
        /// - Build per-player SnapshotPlayer entries (including skill summaries sorted by damage/healing/taken).
        /// </summary>
        public static FullSessionSnapshot TakeSnapshot()
        {
            // ========= 1) Inside the lock: read and copy only—avoid LINQ/heavy computation while locked =========
            DateTime end, start;
            PlayerAcc[] playersSnap;
            NpcAcc[] npcSnap;

            lock (_sync)
            {
                end = EffectiveEndTime();                  // End timestamp (Now if active, otherwise EndedAt)
                start = StartedAt ?? end;                  // If never started, treat as zero-duration
                playersSnap = _players.Values.ToArray();   // Freeze mutable collections as arrays
                npcSnap = _npcs.Values.ToArray();          // Freeze NPC list likewise
            }

            // ========= 2) Outside the lock: perform all LINQ/aggregation on the snapshots =========
            var duration = end - start;
            if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;

            ulong teamDmg = 0, teamHeal = 0, teamTaken = 0;

            // Work with the snapshot dictionary; no references to the live _players map
            var players = new Dictionary<long, SnapshotPlayer>(playersSnap.Length);

            foreach (var p in playersSnap)
            {
                // Top-level totals derived from the snapshot
                teamDmg += p.Damage.Total;
                teamHeal += p.Healing.Total;
                teamTaken += p.TakenDamage;

                // For per-skill breakdowns, freeze internal dictionaries before transforming
                var damageSkillsArr = p.DamageSkills.Count > 0 ? p.DamageSkills.ToArray() : Array.Empty<KeyValuePair<long, StatAcc>>();
                var healingSkillsArr = p.HealingSkills.Count > 0 ? p.HealingSkills.ToArray() : Array.Empty<KeyValuePair<long, StatAcc>>();
                var takenSkillsArr = p.TakenSkills.Count > 0 ? p.TakenSkills.ToArray() : Array.Empty<KeyValuePair<long, StatAcc>>();

                var damageSkills = damageSkillsArr
                    .Select(kv => ToSkillSummary(kv.Key, kv.Value, duration))
                    .OrderByDescending(x => x.Total)
                    .ToList();

                var healingSkills = healingSkillsArr
                    .Select(kv => ToSkillSummary(kv.Key, kv.Value, duration))
                    .OrderByDescending(x => x.Total)
                    .ToList();

                var takenSkills = takenSkillsArr
                    .Select(kv => ToSkillSummary(kv.Key, kv.Value, duration))
                    .OrderByDescending(x => x.Total)
                    .ToList();

                // Per-skill realtime peaks (reuse the snapshot arrays and cached RealtimeDps values)
                double dmgRealtimeMax = damageSkillsArr.Length > 0
                    ? damageSkillsArr.Select(s => s.Value.RealtimeDps).DefaultIfEmpty(0).Max()
                    : 0;

                double healRealtimeMax = healingSkillsArr.Length > 0
                    ? healingSkillsArr.Select(s => s.Value.RealtimeDps).DefaultIfEmpty(0).Max()
                    : 0;

                players[p.Uid] = new SnapshotPlayer
                {
                    Uid = p.Uid,
                    Nickname = p.Nickname,
                    CombatPower = p.CombatPower,
                    Profession = p.Profession,
                    SubProfession = p.SubProfession,

                    // Aggregate totals
                    TotalDamage = p.Damage.Total,
                    TotalHealing = p.Healing.Total,
                    TakenDamage = p.TakenDamage,

                    // Session-scale DPS/HPS computed with each track’s active seconds (same formula as live view)
                    TotalDps = p.Damage.ActiveSeconds > 0 ? R2(p.Damage.Total / p.Damage.ActiveSeconds) : 0,
                    TotalHps = p.Healing.ActiveSeconds > 0 ? R2(p.Healing.Total / p.Healing.ActiveSeconds) : 0,

                    LastRecordTime = null, // Available for future extension if the write path tracks last-seen timestamps
                    ActiveSecondsDamage = p.Damage.ActiveSeconds,
                    ActiveSecondsHealing = p.Healing.ActiveSeconds,

                    // Per-skill lists
                    DamageSkills = damageSkills,
                    HealingSkills = healingSkills,
                    TakenSkills = takenSkills,

                    // Realtime metrics sourced from the FullRecord accumulators
                    RealtimeDps = (ulong)Math.Round(p.RealtimeDpsDamage),
                    HealingRealtime = (ulong)Math.Round(p.RealtimeDpsHealing),
                    RealtimeDpsMax = (ulong)Math.Round(dmgRealtimeMax),
                    HealingRealtimeMax = (ulong)Math.Round(healRealtimeMax),

                    // Damage-side breakdown and rates
                    CriticalDamage = p.Damage.Critical,
                    LuckyDamage = p.Damage.Lucky + p.Damage.CritLucky, // Combine lucky and crit-lucky
                    CritLuckyDamage = p.Damage.CritLucky,
                    MaxSingleHit = p.Damage.MaxSingleHit,
                    CritRate = p.Damage.CountTotal > 0 ? R2((double)p.Damage.CountCritical * 100.0 / p.Damage.CountTotal) : 0.0,
                    LuckyRate = p.Damage.CountTotal > 0 ? R2((double)p.Damage.CountLucky * 100.0 / p.Damage.CountTotal) : 0.0,
                };
            }

            // Build NPC session snapshots
            var nps = new Dictionary<long, FullSessionNpc>(npcSnap.Length);
            foreach (var n in npcSnap)
            {
                var s = n.Taken;
                var secs = s.ActiveSeconds > 0 ? s.ActiveSeconds : 0;
                var per = secs > 0 ? R2(s.Total / secs) : 0;
                var min = s.MinSingleHit;

                // Top attackers (take first 10 by default)
                var top = n.DamageByPlayer
                    .OrderByDescending(kv => kv.Value.Total)
                    .Take(10)
                    .Select(kv =>
                    {
                        var uid = kv.Key;
                        var ns = kv.Value;
                        var npcOnlyDps = ns.ActiveSeconds > 0 ? R2(ns.Total / ns.ActiveSeconds) : 0;

                        // Resolve nickname by consulting the player snapshot array
                        var p = playersSnap.FirstOrDefault(pp => pp.Uid == uid);
                        var nick = p != null ? p.Nickname : "Unknown";

                        return (uid, nick, ns.Total, npcOnlyDps);
                    })
                    .ToList();

                nps[n.NpcId] = new FullSessionNpc
                {
                    NpcId = n.NpcId,
                    Name = n.Name,
                    TotalTaken = s.Total,
                    TakenPerSec = per,
                    MaxSingleHit = s.MaxSingleHit,
                    MinSingleHit = min,
                    TopAttackers = top
                };
            }
            // ========= 3) Assemble the snapshot object using the local snapshot data =========
            return new FullSessionSnapshot
            {
                StartedAt = start,
                EndedAt = end,
                Duration = duration,
                TeamTotalDamage = teamDmg,
                TeamTotalHealing = teamHeal,
                TeamTotalTakenDamage = teamTaken,
                Players = players,
                Npcs = nps

            };
        }


        /// <summary>
        /// Get the team’s current session DPS (damage only).
        /// - Denominator: the maximum Damage.ActiveSeconds across the roster;
        /// - Numerator: total team damage.
        /// </summary>
        public static double GetTeamDps()
        {
            lock (_sync)
            {
                double teamActiveSecs = 0;
                foreach (var p in _players.Values)
                    if (p.Damage.ActiveSeconds > teamActiveSecs)
                        teamActiveSecs = p.Damage.ActiveSeconds;

                if (teamActiveSecs <= 0) return 0.0;

                ulong total = 0;
                foreach (var p in _players.Values) total += p.Damage.Total;

                return R2(total / teamActiveSecs);
            }
        }

        /// <summary>
        /// Get the current session DPS for a specific player (damage only).
        /// - Denominator: the entire session duration (SessionSeconds);
        /// - Returns 0 if the session has not started or duration is zero.
        /// </summary>
        public static double GetPlayerDps(long uid)
        {
            var secs = SessionSeconds();
            if (secs <= 0) return 0;
            return _players.TryGetValue(uid, out var p) ? R2(p.Damage.Total / secs) : 0;
        }

        // Ensure the namespace is imported when consuming these helpers:
        // using StarResonanceDpsAnalysis.Plugin.DamageStatistics;

        public static (IReadOnlyList<SkillSummary> DamageSkills,
                      IReadOnlyList<SkillSummary> HealingSkills,
                      IReadOnlyList<SkillSummary> TakenSkills)
        GetPlayerSkillsBySnapshotTimeEx(DateTime snapshotStartTime, long uid, double toleranceSeconds = 2.0)
        {
            // First check the session-wide history
            var session = SessionHistory?.FirstOrDefault(s =>
                s.StartedAt == snapshotStartTime ||
                Math.Abs((s.StartedAt - snapshotStartTime).TotalSeconds) <= toleranceSeconds);

            if (session != null && session.Players != null &&
                session.Players.TryGetValue(uid, out var sp1))
            {
                return (sp1.DamageSkills ?? new List<SkillSummary>(),
                        sp1.HealingSkills ?? new List<SkillSummary>(),
                        sp1.TakenSkills ?? new List<SkillSummary>());
            }

            // Next check individual battle history
            var battles = StatisticData._manager.History;
            var battle = battles?.FirstOrDefault(s =>
                s.StartedAt == snapshotStartTime ||
                Math.Abs((s.StartedAt - snapshotStartTime).TotalSeconds) <= toleranceSeconds);

            if (battle != null && battle.Players != null &&
                battle.Players.TryGetValue(uid, out var sp2))
            {
                return (sp2.DamageSkills ?? new List<SkillSummary>(),
                        sp2.HealingSkills ?? new List<SkillSummary>(),
                        sp2.TakenSkills ?? new List<SkillSummary>());
            }

            // Fall back to empty collections when nothing matches
            return (Array.Empty<SkillSummary>(), Array.Empty<SkillSummary>(), Array.Empty<SkillSummary>());
        }



        // ======================================================================
        // # Category 11: snapshot-time lookups (historical queries)
        // ======================================================================

        #region Queries by snapshot timestamp
        /// <summary>
        /// Retrieve all player data for the snapshot that starts at the specified time.
        /// - Returns null when no matching snapshot exists.
        /// </summary>
        public static IReadOnlyDictionary<long, SnapshotPlayer>? GetAllPlayersDataBySnapshotTime(DateTime snapshotStartTime)
        {
            var snapshot = SessionHistory.FirstOrDefault(s => s.StartedAt == snapshotStartTime);
            return snapshot?.Players;
        }

        /// <summary>
        /// 按快照的开始时间和玩家 UID 获取该玩家的技能数据。
        /// - 返回 (伤害技能, 治疗技能)，若找不到则返回两个空列表。
        /// </summary>
        public static (IReadOnlyList<SkillSummary> DamageSkills, IReadOnlyList<SkillSummary> HealingSkills)
            GetPlayerSkillsBySnapshotTime(DateTime snapshotStartTime, long uid)
        {
            var snapshot = SessionHistory.FirstOrDefault(s => s.StartedAt == snapshotStartTime);
            if (snapshot != null && snapshot.Players.TryGetValue(uid, out var player))
            {
                return (player.DamageSkills, player.HealingSkills);
            }
            return (Array.Empty<SkillSummary>(), Array.Empty<SkillSummary>());
        }
        #endregion

        // ======================================================================
        // # Category 12: internal utilities (duration, get-or-create, accumulation, projections)
        // ======================================================================

        #region Internal helpers

        /// <summary>
        /// Compute the session’s effective duration in seconds:
        /// - Returns 0 when the session has not started;
        /// - While recording: measure from StartedAt to Now;
        /// - After stopping: measure from StartedAt to EndedAt.
        /// </summary>
        private static double SessionSeconds()
        {
            if (StartedAt is null) return 0;

            DateTime end = IsRecording
                ? DateTime.Now           // Active session: use Now as the temporary endpoint
                : (EndedAt ?? DateTime.Now);

            var sec = (end - StartedAt.Value).TotalSeconds;
            return sec > 0 ? sec : 0;
        }

        /// <summary>
        /// Fetch or create a player accumulator and sync base info (nickname/combat power/profession).
        /// </summary>
        private static PlayerAcc GetOrCreate(long uid, string nickname, int combatPower, string profession, string? subProfession = null)
        {
            if (!_players.TryGetValue(uid, out var p))
            {
                p = new PlayerAcc(uid);
                _players[uid] = p;
            }
            // Keep the most recent basic info
            p.Nickname = nickname;
            p.CombatPower = combatPower;
            p.Profession = profession;
            if (subProfession != null)
            {
                p.SubProfession = subProfession;

            }

            return p;
        }

        /// <summary>
        /// Accumulate a single event into the statistics structure:
        /// - Distinguish normal/critical/lucky/crit-lucky;
        /// - Maintain totals, hpLessen, counters, and max/min single-hit values;
        /// - Use FirstAt/LastAt to advance ActiveSeconds with a capped gap.
        /// </summary>
        private static void Accumulate(
            StatAcc acc, ulong value, bool isCrit, bool isLucky, ulong hpLessen,
            bool isCauseLucky = false // Flag to indicate cause-lucky contributions
        )
        {
            // Quadrant accumulation logic
            if (isCrit && isLucky) acc.CritLucky += value;
            else if (isCrit) acc.Critical += value;
            else if (isLucky) acc.Lucky += value;
            else acc.Normal += value;

            acc.Total += value;
            acc.HpLessen += hpLessen;

            // Increment counters
            if (isCrit) acc.CountCritical++;
            if (isLucky) acc.CountLucky++;
            if (!isCrit && !isLucky) acc.CountNormal++;
            acc.CountTotal++;

            // Cause-lucky tracking
            if (isLucky && isCauseLucky)
            {
                acc.CauseLucky += value;
                acc.CountCauseLucky++;
            }

            // Extrema
            if (value > 0)
            {
                if (value > acc.MaxSingleHit) acc.MaxSingleHit = value;
                if (acc.MinSingleHit == 0 || value < acc.MinSingleHit) acc.MinSingleHit = value;
            }

            // Timing / active duration (retains original behavior)
            var now = DateTime.Now;
            if (acc.FirstAt is null) { acc.FirstAt = now; }
            else
            {
                const double GAP_CAP_SECONDS = 3.0;
                var gap = (now - (acc.LastAt ?? acc.FirstAt.Value)).TotalSeconds;
                if (gap < 0) gap = 0;
                if (gap > GAP_CAP_SECONDS) gap = GAP_CAP_SECONDS;
                acc.ActiveSeconds += gap;
            }
            acc.LastAt = now;
        }


        /// <summary>
        /// Convert internal skill statistics into snapshot-facing summaries (DPS, averages, rates, etc.).
        /// - Realtime fields remain zero within snapshots.
        /// </summary>
        private static SkillSummary ToSkillSummary(long skillId, StatAcc s, TimeSpan duration)
        {
            var meta = SkillBook.Get(skillId);
            return new SkillSummary
            {
                SkillId = skillId,
                SkillName = meta.Name,
                Total = s.Total,
                HitCount = s.CountTotal,
                AvgPerHit = s.CountTotal > 0 ? R2((double)s.Total / s.CountTotal) : 0.0,
                CritRate = s.CountTotal > 0 ? R2((double)s.CountCritical * 100.0 / s.CountTotal) : 0.0,
                LuckyRate = s.CountTotal > 0 ? R2((double)s.CountLucky * 100.0 / s.CountTotal) : 0.0,
                MaxSingleHit = s.MaxSingleHit,
                MinSingleHit = s.MinSingleHit,
                RealtimeValue = 0,          // Snapshots represent historical data; realtime stays zero
                RealtimeMax = 0,            // Same rationale as above
                TotalDps = s.ActiveSeconds > 0 ? R2(s.Total / s.ActiveSeconds) : 0,
                LastTime = null,            // Hook for future extension (last occurrence timestamp)
                ShareOfTotal = 0,           // Placeholder for percentage-of-total calculations
                LuckyDamage = s.Lucky + s.CritLucky,
                CritLuckyDamage = s.CritLucky,
                CauseLuckyDamage = s.CauseLucky, // Already tracked within StatAcc
                CountLucky = s.CountLucky,

            };
        }

        // ===== Internal data structures =====

        /// <summary>
        /// Player accumulator (maintained throughout the session).
        /// - Stores base info (nickname/combat power/profession) plus damage/healing/taken statistics;
        /// - Aggregates per-skill data via DamageSkills/HealingSkills/TakenSkills;
        /// - Publishes realtime metrics for UI display.
        /// </summary>
        private sealed class PlayerAcc
        {
            public long Uid { get; }
            public string Nickname { get; set; } = "Unknown";
            public int CombatPower { get; set; }
            public string Profession { get; set; } = "Unknown";
            public string? SubProfession { get; set; } // Optional specialization
            public StatAcc Damage { get; } = new();
            public StatAcc Healing { get; } = new();
            public ulong TakenDamage { get; set; }

            public Dictionary<long, StatAcc> DamageSkills { get; } = new();
            public Dictionary<long, StatAcc> HealingSkills { get; } = new();
            public Dictionary<long, StatAcc> TakenSkills { get; } = new();

            // Realtime DPS aggregates
            public double RealtimeDpsDamage { get; set; }
            public double RealtimeDpsHealing { get; set; }

            public PlayerAcc(long uid) => Uid = uid;

            // Optional extended breakdowns
            public Dictionary<long, Dictionary<string, StatAcc>> DamageSkillsByElement { get; } = new();
            public Dictionary<long, Dictionary<ulong, StatAcc>> HealingSkillsByTarget { get; } = new();


        }

        /// <summary>
        /// Generic statistical accumulator:
        /// - Quadrant totals: Normal/Critical/Lucky/CritLucky;
        /// - Counters: Count* fields;
        /// - Extremes: MaxSingleHit/MinSingleHit;
        /// - Timing: FirstAt/LastAt/ActiveSeconds;
        /// - RealtimeDps: enables realtime UI visuals (per skill/per category).
        /// </summary>
        private sealed class StatAcc
        {
            public ulong Normal, Critical, Lucky, CritLucky, HpLessen, Total;
            public ulong MaxSingleHit, MinSingleHit; // Min=0 indicates no assigned value
            public int CountNormal, CountCritical, CountLucky, CountTotal;
            public DateTime? FirstAt;     // Timestamp of the first record
            public DateTime? LastAt;      // Timestamp of the most recent record
            public double ActiveSeconds;  // Accumulated span of activity (seconds)
            // Realtime DPS (per skill / per class)
            public double RealtimeDps { get; set; }

            // Extended fields for full-session recording
            public ulong CauseLucky;      // Cause-lucky accumulated value
            public int CountCauseLucky;   // Cause-lucky occurrence count
            public int CountMiss;         // Miss count (primarily for taken damage)
            public int CountDead;         // Death count (taken damage)
        }
        #endregion
    }

    // ======================================================================
    // # Category 13: snapshot structure definitions (session-spanning aggregation)
    // ======================================================================

    /// <summary>Session snapshot structure (similar to BattleSnapshot but spanning multiple battles). Used for historical/statistical displays.</summary>
    public sealed class FullSessionSnapshot
    {
        public DateTime StartedAt { get; init; }          // Snapshot start time
        public DateTime EndedAt { get; init; }            // Snapshot end time
        public TimeSpan Duration { get; init; }           // Total duration
        public ulong TeamTotalDamage { get; init; }       // Total team damage
        public ulong TeamTotalHealing { get; init; }      // Total team healing
        public Dictionary<long, SnapshotPlayer> Players { get; init; } = new(); // Per-player breakdown
        public ulong TeamTotalTakenDamage { get; init; }  // Total team damage taken

        // NPC aggregation captured in the session snapshot
        public Dictionary<long, FullSessionNpc> Npcs { get; init; } = new();
    }

    /// <summary>NPC view stored within a session snapshot (skill-agnostic).</summary>
    public sealed class FullSessionNpc
    {
        public long NpcId { get; init; }
        public string Name { get; init; } = "Unknown NPC";
        public ulong TotalTaken { get; init; }
        public double TakenPerSec { get; init; }
        public ulong MaxSingleHit { get; init; }
        public ulong MinSingleHit { get; init; }

        // Top attackers (core fields only; extend if needed)
        public List<(long Uid, string Nickname, ulong DamageToNpc, double NpcOnlyDps)> TopAttackers { get; init; } = new();
    }
}
