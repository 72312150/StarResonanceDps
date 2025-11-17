using System;
using System.Collections.Generic;
using System.Drawing;

using AntdUI;
using StarResonanceDpsAnalysis.WinForm.Forms;
using StarResonanceDpsAnalysis.WinForm.Plugin;
using StarResonanceDpsAnalysis.WinForm.Plugin.DamageStatistics;

using static StarResonanceDpsAnalysis.WinForm.Forms.DpsStatisticsForm;

namespace StarResonanceDpsAnalysis.WinForm.Control
{
    public partial class SkillDetailForm
    {
        #region Section: Context types (determine data source)
        // Determines whether the detail view targets the current battle, full session, or a snapshot
        public enum DetailContextType
        {
            Current,     // Current battle (default)
            FullRecord,  // Full session (cumulative)
            Snapshot     // Historical snapshot (fixed slice of combat data)
        }
        #endregion

        #region Section: Table column configuration (definitions and binding only)
        public void ToggleTableView()
        {
            table_DpsDetailDataTable.Columns.Clear();

            table_DpsDetailDataTable.Columns = new AntdUI.ColumnCollection
            {
                new AntdUI.Column("Name","Skill"){},
                new AntdUI.Column("Damage","Damage"),
                new AntdUI.Column("TotalDps","DPS/s"),
                new AntdUI.Column("HitCount","Hits"),
                new AntdUI.Column("CritRate","Critical Rate"),
                new AntdUI.Column("AvgPerHit","Average Damage"),
                new AntdUI.Column("Percentage","Percent"),
            };

            // Bind to SkillTableDatas.SkillTable (the data collection maintained externally)
            table_DpsDetailDataTable.Binding(SkillTableDatas.SkillTable);
        }
        #endregion

        #region Section: Instance-level fields (assigned by the caller)
        public long Uid;           // Player UID currently being inspected
        public string Nickname;     // Player nickname for display
        public int Power;           // Player combat power for display
        public string Profession;   // Player class for display

        // Sorting selector: defaults to descending Total; replace externally to change ordering without modifying this class
        public Func<SkillSummary, double> SkillOrderBySelector = s => s.Total;
        #endregion

        #region Section: Private helpers (simple implementations in place of LINQ)
        private SkillData FindRowBySkillId(long skillId)
        {
            for (int i = 0; i < SkillTableDatas.SkillTable.Count; i++)
            {
                if (SkillTableDatas.SkillTable[i].SkillId == skillId)
                    return SkillTableDatas.SkillTable[i];
            }
            return null;
        }

        private double SumTotal(List<SkillSummary> list)
        {
            double sum = 0;
            for (int i = 0; i < list.Count; i++)
            {
                sum += list[i].Total;
            }
            return sum;
        }

        private void SortSkillsDesc(List<SkillSummary> list)
        {
            // Sort descending using the SkillOrderBySelector
            list.Sort(delegate (SkillSummary a, SkillSummary b)
            {
                double va = SkillOrderBySelector != null ? SkillOrderBySelector(a) : a.Total;
                double vb = SkillOrderBySelector != null ? SkillOrderBySelector(b) : b.Total;
                if (va < vb) return 1;   // Descending order
                if (va > vb) return -1;
                return 0;
            });
        }

        private List<SkillSummary> ToListOrEmpty(IReadOnlyList<SkillSummary> src)
        {
            var list = new List<SkillSummary>();
            if (src == null) return list;
            for (int i = 0; i < src.Count; i++) list.Add(src[i]);
            return list;
        }
        #endregion

        #region Section: Table data refresh (current vs full-session entry point)
        public enum SourceType { Current, FullRecord }
        public enum MetricType { Damage, Healing, Taken, NpcTaken }
        /// <summary>
        /// Refresh and populate the player skill table.
        /// The source (Current/FullRecord) and metric (Damage/Healing/Taken/NpcTaken) determine the data source.
        /// Map skills to SkillData rows and push them into SkillTableDatas.SkillTable.
        /// </summary>
        public void UpdateSkillTable(long uid, SourceType source, MetricType metric)
        {
            SkillTableDatas.SkillTable.Clear();

            // Normalize the skill list into a consistent structure
            List<SkillSummary> skills;
            if (source == SourceType.Current)
            {
                if (metric == MetricType.Taken)
                {
                    var temp = StatisticData._manager.GetPlayerTakenDamageSummaries(uid, null, true);
                    skills = ToListOrEmpty(temp);
                    SortSkillsDesc(skills);
                }
                else
                {
                    var skillType = (metric == MetricType.Healing)
                        ? Core.SkillType.Heal
                        : Core.SkillType.Damage;

                    var temp = StatisticData._manager.GetPlayerSkillSummaries(uid, null, true, skillType);
                    skills = ToListOrEmpty(temp);
                    SortSkillsDesc(skills);
                }
            }
            else
            {
                // Full-session totals: read from FullRecord (damage/heal/taken retrieved separately)
                var triple = FullRecord.GetPlayerSkills(uid);
                if (metric == MetricType.Healing)
                {
                    skills = ToListOrEmpty(triple.Item2);
                    SortSkillsDesc(skills);
                }
                else if (metric == MetricType.Taken)
                {
                    skills = ToListOrEmpty(triple.Item3);
                    SortSkillsDesc(skills);
                }
                else
                {
                    skills = ToListOrEmpty(triple.Item1);
                    SortSkillsDesc(skills);
                }
            }

            // Compute each skill's share of the total for this list
            double grandTotal = SumTotal(skills);

            for (int i = 0; i < skills.Count; i++)
            {
                var item = skills[i];
                double share = grandTotal > 0 ? (double)item.Total / grandTotal : 0.0;

                string critRateStr = item.CritRate.ToString() + "%";
                string luckyRateStr = item.LuckyRate.ToString() + "%";

                // Check if a matching row already exists
                var existing = FindRowBySkillId(item.SkillId);
                if (existing == null)
                {
                    var newRow = new SkillData(
                        item.SkillId,
                        item.SkillName,
                        null,
                        item.Total,
                        item.HitCount,
                        critRateStr,
                        share,
                        item.AvgPerHit,
                        item.TotalDps
                    );

                    newRow.Share = new CellProgress((float)share);
                    newRow.Share.Fill = AppConfig.DpsColor;
                    newRow.Share.Size = new Size(200, 10);
                    
                    SkillTableDatas.SkillTable.Add(newRow);
                }
                else
                {
                    existing.SkillId = item.SkillId;
                    existing.Name = item.SkillName;
                    existing.Damage = new CellText(item.Total.ToString()) { Font = AppConfig.ContentFont };
                    existing.HitCount = new CellText(item.HitCount.ToString()) { Font = AppConfig.ContentFont };
                    existing.CritRate = new CellText(critRateStr) { Font = AppConfig.ContentFont };
                    existing.AvgPerHit = new CellText(item.AvgPerHit.ToString()) { Font = AppConfig.ContentFont };
                    existing.TotalDps = new CellText(item.TotalDps.ToString()) { Font = AppConfig.ContentFont };
                    existing.Percentage = new CellText(share.ToString()) { Font = AppConfig.ContentFont };

                    var cp = new CellProgress((float)share);
                    cp.Fill = AppConfig.DpsColor;
                    cp.Size = new Size(200, 10);
                    existing.Share = cp;
                }
            }
        }
        #endregion

        #region Section: Data-type selection (dispatch header + table + charts)
        /// <summary>
        /// Choose the dataset based on UI state (ContextType, segmented1) and refresh:
        /// - Snapshot: render header and table directly from snapshot data (no realtime charts)
        /// - Non-snapshot: choose Current or FullRecord based on showTotal/ContextType
        /// </summary>
        public void SelectDataType()
        {
            //// 1) Determine which metric (Damage/Healing/Taken) segmented1 currently selects
            //MetricType metric;
            //if (segmented1.SelectIndex == 1) metric = MetricType.Healing;
            //else if (segmented1.SelectIndex == 2)
            //{
            //    metric = MetricType.Taken;
            //}
            //else metric = MetricType.Damage;

            //// 2) Snapshot mode: render header/table/static charts only (skip realtime graphs)
            //if (ContextType == DetailContextType.Snapshot && SnapshotStartTime is DateTime)
            //{
            //    DateTime snapTime = (DateTime)SnapshotStartTime;
            //    try
            //    {
            //        FillHeader(metric);                    // Header totals (snapshot scope)
            //        UpdateSkillTable_Snapshot(Uid, snapTime, metric); // Table data (snapshot scope)
            //    }
            //    catch { }

            //    try { UpdateCritLuckyChart(); } catch { }
            //    try { UpdateSkillDistributionChart(); } catch { }
            //    return;
            //}

            //// 3) Non-snapshot mode: render using Current or FullRecord data
            //SourceType source;
            //if (ContextType == DetailContextType.FullRecord || FormManager.showTotal) source = SourceType.FullRecord;
            //else source = SourceType.Current;

            //FillHeader(metric); // Header totals (Current/FullRecord)

            //try
            //{
            //    UpdateSkillTable(Uid, source, metric);   // Table view
            //    RefreshDpsTrendChart();                  // Trend chart (only non-snapshot)
            //    UpdateCritLuckyChart();                  // Crit/Lucky share chart
            //    UpdateSkillDistributionChart();          // Normal/Crit/Lucky distribution chart
            //}
            //catch { }
        }
        #endregion

        #region Section: Header rendering (snapshot / current / full session)
        private void FillHeader(MetricType metric)
        {
            //// ======== Snapshot mode (historical) ========
            //if (ContextType == DetailContextType.Snapshot && SnapshotStartTime is DateTime)
            //{
            //    DateTime snapTime = (DateTime)SnapshotStartTime;

            //    var sessionDict = FullRecord.GetAllPlayersDataBySnapshotTime(snapTime);

            //    SnapshotPlayer sp = null;
            //    if (sessionDict != null)
            //    {
            //        SnapshotPlayer tmp;
            //        if (sessionDict.TryGetValue(Uid, out tmp)) sp = tmp;
            //    }

            //    if (sp == null)
            //    {
            //        // Search _manager.History for a fight with matching start time (±2 seconds)
            //        BattleSnapshot battle = null;
            //        var history = StatisticData._manager.History;
            //        if (history != null)
            //        {
            //            for (int i = 0; i < history.Count; i++)
            //            {
            //                var s = history[i];
            //                double delta = Math.Abs((s.StartedAt - snapTime).TotalSeconds);
            //                if (s.StartedAt == snapTime || delta <= 2.0)
            //                {
            //                    battle = s;
            //                    break;
            //                }
            //            }
            //        }
            //        if (battle != null && battle.Players != null)
            //        {
            //            SnapshotPlayer tmp2;
            //            if (battle.Players.TryGetValue(Uid, out tmp2)) sp = tmp2;
            //        }
            //    }

            //    if (sp == null)
            //    {
            //        TotalDamageText.Text = "0";
            //        TotalDpsText.Text = "0";
            //        CritRateText.Text = "0";
            //        LuckyRate.Text = "0";
            //        NormalDamageText.Text = "0";
            //        CritDamageText.Text = "0";
            //        LuckyDamageText.Text = "0";
            //        AvgDamageText.Text = "0";
            //        NumberHitsLabel.Text = "0";
            //        NumberCriticalHitsLabel.Text = "0";
            //        LuckyTimesLabel.Text = "0";
            //        BeatenLabel.Text = "0";
            //        return;
            //    }

            //    // Helper functions used for snapshot estimates
            //    static int SumHits(IReadOnlyList<SkillSummary> list)
            //    {
            //        if (list == null) return 0;
            //        int total = 0;
            //        for (int i = 0; i < list.Count; i++) total += list[i].HitCount;
            //        return total;
            //    }

            //    static int EstCrits(IReadOnlyList<SkillSummary> list)
            //    {
            //        if (list == null) return 0;
            //        double total = 0;
            //        for (int i = 0; i < list.Count; i++)
            //        {
            //            var s = list[i];
            //            total += s.HitCount * (s.CritRate / 100.0);
            //        }
            //        return (int)Math.Round(total);
            //    }

            //    static int EstLuckies(IReadOnlyList<SkillSummary> list)
            //    {
            //        if (list == null) return 0;
            //        double total = 0;
            //        for (int i = 0; i < list.Count; i++)
            //        {
            //            var s = list[i];
            //            total += s.HitCount * (s.LuckyRate / 100.0);
            //        }
            //        return (int)Math.Round(total);
            //    }

            //    static double WeightedRate(IReadOnlyList<SkillSummary> list, Func<SkillSummary, double> selector)
            //    {
            //        if (list == null || list.Count == 0) return 0;
            //        int hits = SumHits(list);
            //        if (hits <= 0) return 0;

            //        double weightedSum = 0;
            //        for (int i = 0; i < list.Count; i++)
            //        {
            //            var s = list[i];
            //            weightedSum += s.HitCount * selector(s);
            //        }
            //        return Math.Round(weightedSum / hits, 2);
            //    }

            //    static ulong SafeUlong(double v)
            //    {
            //        if (v <= 0) return 0UL;
            //        return (ulong)Math.Round(v);
            //    }

            //    var dmgSkills = sp.DamageSkills;
            //    var healSkills = sp.HealingSkills;
            //    var takenSkills = sp.TakenSkills;

            //    if (metric == MetricType.Damage)
            //    {
            //        var total = sp.TotalDamage;
            //        var dps = sp.TotalDps;
            //        int hits = SumHits(dmgSkills);
            //        double avg = (hits > 0) ? ((double)total / hits) : 0.0;

            //        long normal = (long)total - (long)sp.CriticalDamage - (long)sp.LuckyDamage + (long)sp.CritLuckyDamage;
            //        if (normal < 0) normal = 0;

            //        double critRate = (sp.CritRate > 0) ? sp.CritRate : WeightedRate(dmgSkills, delegate (SkillSummary s) { return s.CritRate; });
            //        double luckyRate = (sp.LuckyRate > 0) ? sp.LuckyRate : WeightedRate(dmgSkills, delegate (SkillSummary s) { return s.LuckyRate; });

            //        TotalDamageText.Text = Common.FormatWithEnglishUnits(total);
            //        TotalDpsText.Text = Common.FormatWithEnglishUnits(dps);
            //        CritRateText.Text = critRate.ToString() + "%";
            //        LuckyRate.Text = luckyRate.ToString() + "%";

            //        NormalDamageText.Text = Common.FormatWithEnglishUnits(SafeUlong(normal));
            //        CritDamageText.Text = Common.FormatWithEnglishUnits(sp.CriticalDamage);
            //        LuckyDamageText.Text = Common.FormatWithEnglishUnits(sp.LuckyDamage);
            //        AvgDamageText.Text = Common.FormatWithEnglishUnits(avg);

            //        NumberHitsLabel.Text = Common.FormatWithEnglishUnits(hits);
            //        NumberCriticalHitsLabel.Text = Common.FormatWithEnglishUnits(EstCrits(dmgSkills));
            //        LuckyTimesLabel.Text = Common.FormatWithEnglishUnits(EstLuckies(dmgSkills));
            //        BeatenLabel.Text = Common.FormatWithEnglishUnits(SumHits(takenSkills));
            //    }
            //    else if (metric == MetricType.Healing)
            //    {
            //        var total = sp.TotalHealing;
            //        var hps = sp.TotalHps;
            //        int hits = SumHits(healSkills);
            //        double avg = (hits > 0) ? ((double)total / hits) : 0.0;

            //        long normalHeal = (long)total - (long)sp.HealingCritical - (long)sp.HealingLucky + (long)sp.HealingCritLucky;
            //        if (normalHeal < 0) normalHeal = 0;

            //        double critRate = WeightedRate(healSkills, delegate (SkillSummary s) { return s.CritRate; });
            //        double luckyRate = WeightedRate(healSkills, delegate (SkillSummary s) { return s.LuckyRate; });

            //        TotalDamageText.Text = Common.FormatWithEnglishUnits(total);
            //        TotalDpsText.Text = Common.FormatWithEnglishUnits(hps);
            //        CritRateText.Text = critRate.ToString() + "%";
            //        LuckyRate.Text = luckyRate.ToString() + "%";

            //        NormalDamageText.Text = Common.FormatWithEnglishUnits(SafeUlong(normalHeal));
            //        CritDamageText.Text = Common.FormatWithEnglishUnits(sp.HealingCritical);
            //        LuckyDamageText.Text = Common.FormatWithEnglishUnits(sp.HealingLucky);
            //        AvgDamageText.Text = Common.FormatWithEnglishUnits(avg);

            //        NumberHitsLabel.Text = Common.FormatWithEnglishUnits(hits);
            //        NumberCriticalHitsLabel.Text = Common.FormatWithEnglishUnits(EstCrits(healSkills));
            //        LuckyTimesLabel.Text = Common.FormatWithEnglishUnits(EstLuckies(healSkills));
            //        BeatenLabel.Text = Common.FormatWithEnglishUnits(hits);
            //    }
            //    else // Taken
            //    {
            //        ulong total = sp.TakenDamage;
            //        int hits = SumHits(takenSkills);
            //        double perSecond = 0.0; // Use snapshot duration if needed

            //        // Max/min taken damage (minimum > 0)
            //        ulong maxSingle = 0UL;
            //        ulong minSingle = 0UL;
            //        if (takenSkills != null && takenSkills.Count > 0)
            //        {
            //            // max
            //            for (int i = 0; i < takenSkills.Count; i++)
            //            {
            //                if (takenSkills[i].MaxSingleHit > maxSingle)
            //                    maxSingle = takenSkills[i].MaxSingleHit;
            //            }
            //            // min (>0)
            //            bool hasMin = false;
            //            for (int i = 0; i < takenSkills.Count; i++)
            //            {
            //                ulong v = takenSkills[i].MinSingleHit;
            //                if (v > 0)
            //                {
            //                    if (!hasMin)
            //                    {
            //                        minSingle = v;
            //                        hasMin = true;
            //                    }
            //                    else
            //                    {
            //                        if (v < minSingle) minSingle = v;
            //                    }
            //                }
            //            }
            //            if (!hasMin) minSingle = 0;
            //        }

            //        TotalDamageText.Text = Common.FormatWithEnglishUnits(total);
            //        TotalDpsText.Text = Common.FormatWithEnglishUnits(perSecond);
            //        CritRateText.Text = Common.FormatWithEnglishUnits(maxSingle);   // UI contract: maximum taken damage
            //        CritDamageText.Text = Common.FormatWithEnglishUnits(minSingle); // UI contract: minimum taken damage
            //        LuckyRate.Text = "0";

            //        NormalDamageText.Text = Common.FormatWithEnglishUnits(total);
            //        CritDamageText.Text = Common.FormatWithEnglishUnits(0);
            //        LuckyDamageText.Text = Common.FormatWithEnglishUnits(0);
            //        AvgDamageText.Text = Common.FormatWithEnglishUnits(hits > 0 ? (double)total / hits : 0.0);

            //        NumberHitsLabel.Text = Common.FormatWithEnglishUnits(hits);
            //        NumberCriticalHitsLabel.Text = "0";
            //        LuckyTimesLabel.Text = "0";
            //        BeatenLabel.Text = Common.FormatWithEnglishUnits(hits);
            //    }

            //    return; // End of snapshot branch
            //}

            //// ======== Non-snapshot (single fight / full session) ========
            //SourceType src;
            //if (ContextType == DetailContextType.FullRecord || FormManager.showTotal) src = SourceType.FullRecord;
            //else src = SourceType.Current;

            //if (src == SourceType.Current)
            //{
            //    var p = StatisticData._manager.GetOrCreate(Uid);

            //    if (metric == MetricType.Damage)
            //    {
            //        TotalDamageText.Text = Common.FormatWithEnglishUnits(p.DamageStats.Total);
            //        TotalDpsText.Text = Common.FormatWithEnglishUnits(p.GetTotalDps());
            //        CritRateText.Text = p.DamageStats.GetCritRate().ToString() + "%";
            //        LuckyRate.Text = p.DamageStats.GetLuckyRate().ToString() + "%";

            //        NormalDamageText.Text = Common.FormatWithEnglishUnits(p.DamageStats.Normal);
            //        CritDamageText.Text = Common.FormatWithEnglishUnits(p.DamageStats.Critical);
            //        LuckyDamageText.Text = Common.FormatWithEnglishUnits(p.DamageStats.LuckyAndCritical);
            //        AvgDamageText.Text = Common.FormatWithEnglishUnits(p.DamageStats.GetAveragePerHit());

            //        NumberHitsLabel.Text = Common.FormatWithEnglishUnits(p.DamageStats.CountTotal);
            //        NumberCriticalHitsLabel.Text = Common.FormatWithEnglishUnits(p.DamageStats.CountCritical);
            //        LuckyTimesLabel.Text = Common.FormatWithEnglishUnits(p.DamageStats.CountLucky);
            //        BeatenLabel.Text = Common.FormatWithEnglishUnits(p.TakenStats.CountTotal);
            //    }
            //    else if (metric == MetricType.Healing)
            //    {
            //        TotalDamageText.Text = Common.FormatWithEnglishUnits(p.HealingStats.Total);
            //        TotalDpsText.Text = Common.FormatWithEnglishUnits(p.GetTotalHps());
            //        CritRateText.Text = p.HealingStats.GetCritRate().ToString() + "%";
            //        LuckyRate.Text = p.HealingStats.GetLuckyRate().ToString() + "%";

            //        NormalDamageText.Text = Common.FormatWithEnglishUnits(p.HealingStats.Normal);
            //        CritDamageText.Text = Common.FormatWithEnglishUnits(p.HealingStats.Critical);
            //        LuckyDamageText.Text = Common.FormatWithEnglishUnits(p.HealingStats.LuckyAndCritical);
            //        AvgDamageText.Text = Common.FormatWithEnglishUnits(p.HealingStats.GetAveragePerHit());

            //        NumberHitsLabel.Text = Common.FormatWithEnglishUnits(p.HealingStats.CountTotal);
            //        NumberCriticalHitsLabel.Text = Common.FormatWithEnglishUnits(p.HealingStats.CountCritical);
            //        LuckyTimesLabel.Text = Common.FormatWithEnglishUnits(p.HealingStats.CountLucky);
            //        BeatenLabel.Text = Common.FormatWithEnglishUnits(p.HealingStats.CountTotal);
            //    }
            //    else // Taken
            //    {
            //        var taken = StatisticData._manager.GetPlayerTakenOverview(Uid);
            //        TotalDamageText.Text = Common.FormatWithEnglishUnits(taken.Total);
            //        TotalDpsText.Text = Common.FormatWithEnglishUnits(taken.AvgTakenPerSec);
            //        CritRateText.Text = Common.FormatWithEnglishUnits(taken.MaxSingleHit); // Maximum taken damage
            //        CritDamageText.Text = Common.FormatWithEnglishUnits(taken.MinSingleHit); // Minimum taken damage
            //        LuckyRate.Text = "0";

            //        NormalDamageText.Text = Common.FormatWithEnglishUnits(p.TakenStats.Total);
            //        CritDamageText.Text = Common.FormatWithEnglishUnits(p.TakenStats.Critical);
            //        LuckyDamageText.Text = Common.FormatWithEnglishUnits(p.TakenStats.LuckyAndCritical);
            //        AvgDamageText.Text = Common.FormatWithEnglishUnits(p.TakenStats.GetAveragePerHit());

            //        NumberHitsLabel.Text = Common.FormatWithEnglishUnits(p.TakenStats.CountTotal);
            //        NumberCriticalHitsLabel.Text = Common.FormatWithEnglishUnits(p.TakenStats.CountCritical);
            //        LuckyTimesLabel.Text = Common.FormatWithEnglishUnits(p.TakenStats.CountLucky);
            //        BeatenLabel.Text = Common.FormatWithEnglishUnits(p.TakenStats.CountTotal);
            //    }
            //}
            //else // FullRecord
            //{
            //    var p = FullRecord.Shim.GetOrCreate(Uid);

            //    if (metric == MetricType.Damage)
            //    {
            //        TotalDamageText.Text = Common.FormatWithEnglishUnits(p.DamageStats.Total);
            //        TotalDpsText.Text = Common.FormatWithEnglishUnits(p.GetTotalDps());
            //        CritRateText.Text = p.DamageStats.GetCritRate().ToString() + "%";
            //        LuckyRate.Text = p.DamageStats.GetLuckyRate().ToString() + "%";

            //        NormalDamageText.Text = Common.FormatWithEnglishUnits(p.DamageStats.Normal);
            //        CritDamageText.Text = Common.FormatWithEnglishUnits(p.DamageStats.Critical);
            //        LuckyDamageText.Text = Common.FormatWithEnglishUnits(p.DamageStats.Lucky);
            //        AvgDamageText.Text = Common.FormatWithEnglishUnits(p.DamageStats.GetAveragePerHit());

            //        NumberHitsLabel.Text = Common.FormatWithEnglishUnits(p.DamageStats.CountTotal);
            //        NumberCriticalHitsLabel.Text = Common.FormatWithEnglishUnits(p.DamageStats.CountCritical);
            //        LuckyTimesLabel.Text = Common.FormatWithEnglishUnits(p.DamageStats.CountLucky);
            //        BeatenLabel.Text = Common.FormatWithEnglishUnits(p.TakenStats.CountTotal);
            //    }
            //    else if (metric == MetricType.Healing)
            //    {
            //        TotalDamageText.Text = Common.FormatWithEnglishUnits(p.HealingStats.Total);
            //        TotalDpsText.Text = Common.FormatWithEnglishUnits(p.GetTotalHps());
            //        CritRateText.Text = p.HealingStats.GetCritRate().ToString() + "%";
            //        LuckyRate.Text = p.HealingStats.GetLuckyRate().ToString() + "%";

            //        NormalDamageText.Text = Common.FormatWithEnglishUnits(p.HealingStats.Normal);
            //        CritDamageText.Text = Common.FormatWithEnglishUnits(p.HealingStats.Critical);
            //        LuckyDamageText.Text = Common.FormatWithEnglishUnits(p.HealingStats.Lucky);
            //        AvgDamageText.Text = Common.FormatWithEnglishUnits(p.HealingStats.GetAveragePerHit());

            //        NumberHitsLabel.Text = Common.FormatWithEnglishUnits(p.HealingStats.CountTotal);
            //        NumberCriticalHitsLabel.Text = Common.FormatWithEnglishUnits(p.HealingStats.CountCritical);
            //        LuckyTimesLabel.Text = Common.FormatWithEnglishUnits(p.HealingStats.CountLucky);
            //        BeatenLabel.Text = Common.FormatWithEnglishUnits(p.HealingStats.CountTotal);
            //    }
            //    else // Taken
            //    {
            //        var taken = FullRecord.Shim.GetPlayerTakenOverview(Uid);
            //        TotalDamageText.Text = Common.FormatWithEnglishUnits(taken.Total);
            //        TotalDpsText.Text = Common.FormatWithEnglishUnits(taken.AvgTakenPerSec);
            //        CritRateText.Text = Common.FormatWithEnglishUnits(taken.MaxSingleHit);
            //        CritDamageText.Text = Common.FormatWithEnglishUnits(taken.MinSingleHit);
            //        LuckyRate.Text = "0";

            //        NormalDamageText.Text = Common.FormatWithEnglishUnits(p.TakenStats.Total);
            //        CritDamageText.Text = Common.FormatWithEnglishUnits(p.TakenStats.Critical);
            //        LuckyDamageText.Text = Common.FormatWithEnglishUnits(p.TakenStats.Lucky);
            //        AvgDamageText.Text = Common.FormatWithEnglishUnits(p.TakenStats.GetAveragePerHit());

            //        NumberHitsLabel.Text = Common.FormatWithEnglishUnits(p.TakenStats.CountTotal);
            //        NumberCriticalHitsLabel.Text = Common.FormatWithEnglishUnits(p.TakenStats.CountCritical);
            //        LuckyTimesLabel.Text = Common.FormatWithEnglishUnits(p.TakenStats.CountLucky);
            //        BeatenLabel.Text = Common.FormatWithEnglishUnits(p.TakenStats.CountTotal);
            //    }
            //}
        }
        #endregion

        #region Section: Charts — normal/critical/lucky distribution (bar chart)
        /// <summary>
        /// Update the bar chart showing normal/critical/lucky ratios.
        /// Source: Damage/Healing/Taken statistics from Current or FullRecord.
        /// </summary>
        private void UpdateSkillDistributionChart()
        {
            //if (_skillDistributionChart == null) return;

            //try
            //{
            //    SourceType source;
            //    if (ContextType == DetailContextType.FullRecord || FormManager.showTotal) source = SourceType.FullRecord;
            //    else source = SourceType.Current;

            //    MetricType metric;
            //    if (segmented1.SelectIndex == 1) metric = MetricType.Healing;
            //    else if (segmented1.SelectIndex == 2) metric = MetricType.Taken;
            //    else metric = MetricType.Damage;

            //    double critRate = 0;
            //    double luckyRate = 0;
            //    if (source == SourceType.Current)
            //    {
            //        var p = StatisticData._manager.GetOrCreate(Uid);
            //        if (metric == MetricType.Healing)
            //        {
            //            critRate = p.HealingStats.GetCritRate();
            //            luckyRate = p.HealingStats.GetLuckyRate();
            //        }
            //        else if (metric == MetricType.Taken)
            //        {
            //            critRate = p.TakenStats.GetCritRate();
            //            luckyRate = p.TakenStats.GetLuckyRate();
            //        }
            //        else
            //        {
            //            critRate = p.DamageStats.GetCritRate();
            //            luckyRate = p.DamageStats.GetLuckyRate();
            //        }
            //    }
            //    else
            //    {
            //        var p = FullRecord.Shim.GetOrCreate(Uid);
            //        if (metric == MetricType.Healing)
            //        {
            //            critRate = p.HealingStats.GetCritRate();
            //            luckyRate = p.HealingStats.GetLuckyRate();
            //        }
            //        else if (metric == MetricType.Taken)
            //        {
            //            critRate = p.TakenStats.GetCritRate();
            //            luckyRate = p.TakenStats.GetLuckyRate();
            //        }
            //        else
            //        {
            //            critRate = p.DamageStats.GetCritRate();
            //            luckyRate = p.DamageStats.GetLuckyRate();
            //        }
            //    }

            //    double normalRate = 100 - critRate - luckyRate;
            //    if (normalRate < 0) normalRate = 0;

            //    var chartData = new List<(string, double)>();
            //    if (normalRate > 0) chartData.Add(("Normal", normalRate));
            //    if (critRate > 0) chartData.Add(("Critical", critRate));
            //    if (luckyRate > 0) chartData.Add(("Lucky", luckyRate));

            //    _skillDistributionChart.SetData(chartData);
            //}
            //catch (Exception ex)
            //{
            //    Console.WriteLine("Failed to update crit/lucky chart: " + ex.Message);
            //}
        }
        #endregion

        #region Section: Charts — skill share (pie/donut)
        /// <summary>
        /// Update the skill share chart (top 10 skills by total value).
        /// Source: Current or FullRecord; metric selection follows segmented1.
        /// </summary>
        private void UpdateCritLuckyChart()
        {
            //if (_critLuckyChart == null) return;

            //try
            //{
            //    SourceType source;
            //    if (ContextType == DetailContextType.FullRecord || FormManager.showTotal) source = SourceType.FullRecord;
            //    else source = SourceType.Current;

            //    MetricType metric;
            //    if (segmented1.SelectIndex == 1) metric = MetricType.Healing;
            //    else if (segmented1.SelectIndex == 2) metric = MetricType.Taken;
            //    else metric = MetricType.Damage;

            //    List<SkillSummary> skills = new List<SkillSummary>();

            //    if (source == SourceType.Current)
            //    {
            //        if (metric == MetricType.Healing)
            //        {
            //            var tmp = StatisticData._manager.GetPlayerSkillSummaries(Uid, 10, true, Core.SkillType.Heal);
            //            skills = ToListOrEmpty(tmp);
            //        }
            //        else if (metric == MetricType.Taken)
            //        {
            //            var tmp = StatisticData._manager.GetPlayerTakenDamageSummaries(Uid, 10, true);
            //            skills = ToListOrEmpty(tmp);
            //        }
            //        else
            //        {
            //            var tmp = StatisticData._manager.GetPlayerSkillSummaries(Uid, 10, true, Core.SkillType.Damage);
            //            skills = ToListOrEmpty(tmp);
            //        }
            //        // Safety: resort using the current selector and take the top 10
            //        SortSkillsDesc(skills);
            //        if (skills.Count > 10) skills = skills.GetRange(0, 10);
            //    }
            //    else
            //    {
            //        var triple = FullRecord.GetPlayerSkills(Uid);
            //        if (metric == MetricType.Healing)
            //            skills = ToListOrEmpty(triple.Item2);
            //        else if (metric == MetricType.Taken)
            //            skills = ToListOrEmpty(triple.Item3);
            //        else
            //            skills = ToListOrEmpty(triple.Item1);

            //        SortSkillsDesc(skills);
            //        if (skills.Count > 10) skills = skills.GetRange(0, 10);
            //    }

            //    var chartData = new List<(string, double)>();
            //    for (int i = 0; i < skills.Count; i++)
            //    {
            //        chartData.Add((skills[i].SkillName, (double)skills[i].Total));
            //    }
            //    _critLuckyChart.SetData(chartData);
            //}
            //catch (Exception ex)
            //{
            //    Console.WriteLine("Failed to update skill share chart: " + ex.Message);
            //}
        }
        #endregion

        #region Section: Snapshot table population (distinct data pathway)
        /// <summary>
        /// Populate the skill table based on snapshot time and UID:
        /// - Fetch the three skill lists via FullRecord.GetPlayerSkillsBySnapshotTimeEx
        /// - Render only the table; realtime charts are skipped
        /// </summary>
        private void UpdateSkillTable_Snapshot(long uid, DateTime startedAt, MetricType metric)
        {
            SkillTableDatas.SkillTable.Clear();

            var triple = FullRecord.GetPlayerSkillsBySnapshotTimeEx(startedAt, uid);

            List<SkillSummary> skills = new List<SkillSummary>();
            if (metric == MetricType.Healing)
                skills = ToListOrEmpty(triple.Item2);
            else if (metric == MetricType.Taken)
                skills = ToListOrEmpty(triple.Item3);
            else
                skills = ToListOrEmpty(triple.Item1);

            SortSkillsDesc(skills);

            // Compute share and produce rows
            double grandTotal = 0;
            for (int i = 0; i < skills.Count; i++) grandTotal += skills[i].Total;

            for (int i = 0; i < skills.Count; i++)
            {
                var s = skills[i];
                double share = grandTotal > 0 ? (double)s.Total / grandTotal : 0.0;

                var row = new SkillData(
                    s.SkillId,
                    s.SkillName,
                    null,
                    s.Total,
                    s.HitCount,
                    s.CritRate.ToString() + "%",
                    share,
                    s.AvgPerHit,
                    s.TotalDps
                );

                var cp = new CellProgress((float)share);
                cp.Size = new Size(200, 10);
                row.Share = cp;

                SkillTableDatas.SkillTable.Add(row);
            }

            // For debugging missing snapshot data, temporarily enable the following dialog to verify the source
            // if (SkillTableDatas.SkillTable.Count == 0)
            // {
            //     MessageBox.Show("Snapshot skill list is empty; verify GetPlayerSkillsBySnapshotTimeEx did not return null.");
            // }
        }
        #endregion
    }
}
