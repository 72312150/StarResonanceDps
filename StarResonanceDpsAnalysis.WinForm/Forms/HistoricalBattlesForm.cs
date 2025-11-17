using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using AntdUI;
using StarResonanceDpsAnalysis.Assets;
using StarResonanceDpsAnalysis.WinForm.Control;
using StarResonanceDpsAnalysis.WinForm.Effects;
using StarResonanceDpsAnalysis.WinForm.Plugin;
using StarResonanceDpsAnalysis.WinForm.Plugin.DamageStatistics;

using static StarResonanceDpsAnalysis.WinForm.Control.SkillDetailForm;
using static System.ComponentModel.Design.ObjectSelectorEditor;

namespace StarResonanceDpsAnalysis.WinForm.Forms
{
    public partial class HistoricalBattlesForm : BorderlessForm
    {
        public HistoricalBattlesForm()
        {
            InitializeComponent();
            Text = FormManager.APP_NAME;
            FormGui.SetDefaultGUI(this); // Apply the default UI styling (fonts, spacing, shadows, etc.)
            ToggleTableView();

            label1.Font = AppConfig.TitleFont;
            select2.Font = select1.Font = segmented1.Font = AppConfig.ContentFont;
            var harmonyOsSansFont_Size11 = HandledAssets.HarmonyOS_Sans(11);
            label6.Font = harmonyOsSansFont_Size11;
            label3.Font = label2.Font = label5.Font = AppConfig.ContentFont;

            table_DpsDetailDataTable.Font = AppConfig.ContentFont;
            TeamTotalDamageLabel.Font = TeamTotalHealingLabel.Font = TeamTotalTakenDamageLabel.Font = AppConfig.DigitalFont;

        }

        private void HistoricalBattlesForm_Load(object sender, EventArgs e)
        {
            //FormGui.SetColorMode(this, AppConfig.IsLight); // Apply color theme based on the light/dark setting

            //if (FormManager.showTotal)
            //{
            //    ReadFullSessionTime();
            //}
            //else
            //{
            //    ReadSnapshotTime();
            //}



        }

        /// <summary>
        /// Populate the dropdown with single-battle snapshots.
        /// </summary>
        private void ReadSnapshotTime()
        {
            select1.Items.Clear();
            var statsList = StatisticData._manager.History?.ToList();
            if (statsList.Count == 0) return;
            foreach (var snap in statsList)
            {
                select1.Items.Add(new ComboItemBattle { Snapshot = snap }); // Store the snapshot directly in the item
            }
            select1.SelectedIndex = 0; // Default to the first entry
        }

        /// <summary>
        /// Populate the dropdown with full-session snapshots.
        /// </summary>
        private void ReadFullSessionTime()
        {
            select1.Items.Clear();
            var sessions = FullRecord.SessionHistory?.ToList();
            if (sessions == null || sessions.Count == 0) return;

            foreach (var s in sessions)
            {
                select1.Items.Add(new ComboItemFull { Snapshot = s });
            }
            select1.SelectedIndex = 0; // Default to the first entry
        }


        // Dropdown item representing a single-battle snapshot
        private sealed class ComboItemBattle
        {
            public BattleSnapshot Snapshot { get; init; }
            public override string ToString()
            {
                var s = Snapshot;
                return $"{s.StartedAt:MM-dd HH:mm:ss} ~ {s.EndedAt:HH:mm:ss} ({s.Duration:hh\\:mm\\:ss})";
            }
        }

        // Dropdown item representing a full-session snapshot
        private sealed class ComboItemFull
        {
            public FullSessionSnapshot Snapshot { get; init; }
            public override string ToString()
            {
                var s = Snapshot;
                return $"[Full Battle] {s.StartedAt:MM-dd HH:mm:ss} ~ {s.EndedAt:HH:mm:ss} ({s.Duration:hh\\:mm\\:ss})";
            }
        }

        private void select1_SelectedIndexChanged(object sender, IntEventArgs e)
        {
            if (segmented1.SelectIndex == 0)
            {
                BattleSnapshot? snap = null;
                if (select1.SelectedValue is ComboItemBattle v && v.Snapshot != null) snap = v.Snapshot;
                else if (select1.SelectedValue is ComboItemBattle v2 && v2.Snapshot != null) snap = v2.Snapshot;

                if (snap != null) DumpSnapshot(snap);
            }
            else
            {
                FullSessionSnapshot? snap = null;
                if (select1.SelectedValue is ComboItemFull v && v.Snapshot != null) snap = v.Snapshot;
                else if (select1.SelectedValue is ComboItemFull v2 && v2.Snapshot != null) snap = v2.Snapshot;

                if (snap != null) DumpFullSnapshot(snap);
            }
        }

        // Render an individual battle snapshot
        private void DumpSnapshot(BattleSnapshot snap)
        {
            DpsTableDatas.DpsTable.Clear(); // Clear previous rows
            var sb = new StringBuilder();
            sb.AppendLine($"[Snapshot] {snap.StartedAt:MM-dd HH:mm:ss} ~ {snap.EndedAt:HH:mm:ss}  Duration: {snap.Duration}");
            TeamTotalDamageLabel.Text =Common.FormatWithEnglishUnits(snap.TeamTotalDamage.ToString());
            TeamTotalHealingLabel.Text = Common.FormatWithEnglishUnits(snap.TeamTotalHealing.ToString());
            TeamTotalTakenDamageLabel.Text = Common.FormatWithEnglishUnits(snap.TeamTotalTakenDamage.ToString());
            var tdTotal = snap.TeamTotalDamage;

            var orderedPlayers = ApplySort(snap.Players.Values);

            foreach (var p in orderedPlayers)
            {
                double dmgShare = snap.TeamTotalDamage > 0
            ? Math.Round(p.TotalDamage * 100.0 / snap.TeamTotalDamage, 1)
            : 0.0;


                DpsTableDatas.DpsTable.Add(new DpsTable(
                   /*  1 */ p.Uid,
            /*  2 */ p.Nickname,

            /*  3 Taken damage (matches the live view column order) */
            /*  3 */ p.TakenDamage,

            /*  4~9 Healing aggregates and realtime windows */
            /*  4 */ p.TotalHealing,

            /*  5 */ p.HealingCritical,// Critical healing
            /*  6 */ p.HealingLucky,// Lucky healing
            /*  7 */ p.HealingCritLucky,// Critical + lucky healing
            /*  8 */ p.HealingRealtime,// Realtime HPS
            /*  9 */ p.HealingRealtimeMax,// Peak instantaneous HPS

            /* 10 Profession */
            /* 10 */ p.Profession,// Profession

            /* 11~14 Damage aggregates and breakdown */
            /* 11 */ p.TotalDamage,// Total damage
            /* 12 */ p.CriticalDamage,// Critical damage
            /* 13 */ p.LuckyDamage,// Lucky damage
            /* 14 */ p.CritLuckyDamage,

            /* 15~16 Rate columns (%) */
            /* 15 */ Math.Round(p.CritRate, 1),
            /* 16 */ Math.Round(p.LuckyRate, 1),

            /* 17~18 Realtime/peak damage */
            /* 17 */ p.RealtimeDps,// Realtime DPS
            /* 18 */ p.RealtimeDpsMax,// Peak realtime DPS

            /* 19~20 Average DPS/HPS */
            /* 19 */ Math.Round(p.TotalDps, 1),// Total DPS
            /* 20 */ Math.Round(p.TotalHps, 1),// Total HPS
                    p.CombatPower,// Combat power
                    dmgShare


                /* 22 Combat power */
                /* 22 */
                ));
                //sb.AppendLine(
                //    $"  UID={p.Uid}  Nickname={p.Nickname}  Class={p.Profession}  Power={p.CombatPower}  " +
                //    $"TotalDamage={p.TotalDamage}  DPS={p.TotalDps:F1}  TotalHealing={p.TotalHealing}  HPS={p.TotalHps:F1}  DamageTaken={p.TakenDamage}");
            }

        }

        // Render a full-session snapshot
        private void DumpFullSnapshot(FullSessionSnapshot snap)
        {
            DpsTableDatas.DpsTable.Clear(); // Clear previous rows
            var sb = new StringBuilder();
            sb.AppendLine($"[Full Snapshot] {snap.StartedAt:MM-dd HH:mm:ss} ~ {snap.EndedAt:HH:mm:ss}  Duration: {snap.Duration}");
            TeamTotalDamageLabel.Text = Common.FormatWithEnglishUnits(snap.TeamTotalDamage.ToString());
            TeamTotalHealingLabel.Text = Common.FormatWithEnglishUnits(snap.TeamTotalHealing.ToString());
            TeamTotalTakenDamageLabel.Text = Common.FormatWithEnglishUnits( snap.TeamTotalTakenDamage.ToString());
            var orderedPlayers = ApplySort(snap.Players.Values);

            foreach (var p in orderedPlayers)
            {
                double dmgShare = snap.TeamTotalDamage > 0
           ? Math.Round(p.TotalDamage * 100.0 / snap.TeamTotalDamage, 1)
           : 0.0;
                // Full-session snapshots do not expose per-category totals or realtime peaks;
                // these columns either use the provided values or fall back to zero while DPS/HPS use their aggregates.
                DpsTableDatas.DpsTable.Add(new DpsTable(
                    /*  1 */ p.Uid,
                    /*  2 */ p.Nickname,

                    /*  3 Taken damage */
                    /*  3 */ p.TakenDamage,

                    /*  4~9 Healing metrics (snapshot fields may be missing → default to zero) */
                    /*  4 */ p.TotalHealing,
                    /*  5 */ p.HealingCritical,          // Critical healing (may be zero)
                    /*  6 */ p.HealingLucky,          // Lucky healing (may be zero)
                    /*  7 */ p.HealingCritLucky,          // Critical + lucky healing (may be zero)
                    /*  8 */ p.HealingRealtime,          // Realtime HPS (may be zero)
                    /*  9 */ p.HealingRealtimeMax,          // Peak instantaneous HPS (may be zero)

                    /* 10 Profession */
                    /* 10 */ p.Profession,

                    /* 11~14 Damage metrics (snapshot fields may be missing → default to zero) */
                    /* 11 */ p.TotalDamage,
                    /* 12 */ p.CriticalDamage,          // Critical damage (may be zero)
                    /* 13 */ p.LuckyDamage,          // Lucky damage (may be zero)
                    /* 14 */ p.CriticalDamage,          // Crit + lucky damage (may be zero)

                    /* 15~16 Rate columns (%) */
                    /* 15 */ p.CritRate,          // CritRate
                    /* 16 */ p.LuckyRate,          // LuckyRate

                    /* 17~18 Realtime/peak DPS (snapshot aggregates only) */
                    /* 17 */ p.RealtimeDps,          // Realtime DPS
                    /* 18 */ p.RealtimeDpsMax,          // Peak realtime DPS

                    /* 19~20 Average DPS/HPS */
                    /* 19 */ Math.Round(p.TotalDps, 1),
                    /* 20 */ Math.Round(p.TotalHps, 1),

                    /* 22 Combat power */
                    /* 22 */ p.CombatPower, dmgShare
                ));


            }


        }

        // Helper types and sorting utilities
        private enum SortMode { ByDamage, ByHealing, ByTaken }
        private SortMode _sortMode = SortMode.ByDamage; // Default sorting: total damage
        // SnapshotPlayer represents the per-player snapshot data model
        private IEnumerable<SnapshotPlayer> ApplySort(IEnumerable<SnapshotPlayer> players)
        {
            switch (_sortMode)
            {
                case SortMode.ByHealing:
                    // Sort by total healing, then average HPS, then peak HPS
                    return players
                        .OrderByDescending(p => p.TotalHealing)
                        .ThenByDescending(p => p.TotalHps)
                        .ThenByDescending(p => p.HealingRealtimeMax);

                case SortMode.ByTaken:
                    // Sort by total damage taken, then use total damage to break ties
                    return players
                        .OrderByDescending(p => p.TakenDamage)
                        .ThenByDescending(p => p.TotalDamage);

                case SortMode.ByDamage:
                default:
                    // Sort by total damage, then average DPS, then peak single hit
                    return players
                        .OrderByDescending(p => p.TotalDamage)
                        .ThenByDescending(p => p.TotalDps)
                        .ThenByDescending(p => p.MaxSingleHit);
            }
        }

        private void label1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                FormManager.ReleaseCapture();
                FormManager.SendMessage(this.Handle, FormManager.WM_NCLBUTTONDOWN, FormManager.HTCAPTION, 0);
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (segmented1.SelectIndex == 0)
            {
                ReadSnapshotTime(); // Refresh the individual snapshot dropdown
            }
            else
            {
                ReadFullSessionTime(); // Refresh the full-session dropdown
            }

        }

        private void button3_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void segmented1_SelectIndexChanged(object sender, IntEventArgs e)
        {
            DpsTableDatas.DpsTable.Clear(); // Clear previous rows
            select1.Items.Clear();
            if (segmented1.SelectIndex == 0)
            {
                ReadSnapshotTime();
            }
            else
            {
                ReadFullSessionTime();
            }
        }

        private void table_DpsDetailDataTable_CellClick(object sender, TableClickEventArgs e)
        {
            //if (e.ColumnIndex <= 0) return;

            //// Row index safety check (AntdUI tables are 0-based, so no adjustment here)
            //int idx = e.RowIndex - 1;
            //if (idx < 0 || idx >= DpsTableDatas.DpsTable.Count) return;

            //var row = DpsTableDatas.DpsTable[idx];
            //var uid = row.Uid;
            //string nick = row.NickName;
            //int power = row.CombatPower;
            //string prof = row.Profession;

            //// Prepare the detail form
            //if (FormManager.skillDetailForm == null || FormManager.skillDetailForm.IsDisposed)
            //    FormManager.skillDetailForm = new SkillDetailForm();

            //var f = FormManager.skillDetailForm;
            //f.Uid = uid;
            //f.Nickname = nick;
            //f.Power = power;
            //f.Profession = prof;

            //// Snapshot context and timestamps
            //f.ContextType = DetailContextType.Snapshot;
            //f.SnapshotStartTime = GetSelectedSnapshotStartTime();
            //if (f.SnapshotStartTime is null)
            //{
            //    // Optional: keep for debugging
            //    // MessageBox.Show("Failed to determine snapshot time (no dropdown selection?)");
            //    return;
            //}

            //// Populate top-of-form player information
            //f.GetPlayerInfo(nick, power, prof);

            //// Optional debug: check whether skill counts are zero to distinguish missing data from UI issues
            ///*
            //var counts = StarResonanceDpsAnalysis.Plugin.DamageStatistics.FullRecord
            //             .GetPlayerSkillsBySnapshotTimeEx(f.SnapshotStartTime.Value, uid);
            //MessageBox.Show($"Snapshot Skills → D:{counts.DamageSkills.Count} H:{counts.HealingSkills.Count} T:{counts.TakenSkills.Count}");
            //*/

            //// Refresh and display
            //f.SelectDataType();   // Should hit the snapshot branch: UpdateSkillTable_Snapshot(...)
            //if (!f.Visible) f.Show(); else f.Activate();
        }

        private DateTime? GetSelectedSnapshotStartTime()
        {
            if (segmented1.SelectIndex == 0 && select1.SelectedValue is ComboItemBattle b && b.Snapshot != null)
                return b.Snapshot.StartedAt;
            if (segmented1.SelectIndex != 0 && select1.SelectedValue is ComboItemFull f && f.Snapshot != null)
                return f.Snapshot.StartedAt;
            return null;
        }

        private void HistoricalBattlesForm_ForeColorChanged(object sender, EventArgs e)
        {
            if (Config.IsLight)
            {
                table_DpsDetailDataTable.RowSelectedBg = ColorTranslator.FromHtml("#AED4FB");
                splitter1.Panel2.BackColor = splitter1.BackColor = ColorTranslator.FromHtml("#FFFFFF");
                panel1.Back = ColorTranslator.FromHtml("#67AEF6");
                splitter1.Panel1.BackColor = ColorTranslator.FromHtml("#FFFFFF");
                table_DpsDetailDataTable.BackColor = ColorTranslator.FromHtml("#FFFFFF");
            }
            else
            {
                splitter1.Panel2.BackColor = splitter1.BackColor = ColorTranslator.FromHtml("#1F1F1F");
                panel1.Back = ColorTranslator.FromHtml("#255AD0");
                splitter1.Panel1.BackColor = ColorTranslator.FromHtml("#141414");
                table_DpsDetailDataTable.BackColor = ColorTranslator.FromHtml("#1F1F1F");
                table_DpsDetailDataTable.RowSelectedBg = ColorTranslator.FromHtml("#10529a");
            }
        }

        private void select2_SelectedIndexChanged(object sender, IntEventArgs e)
        {
            var val = select2?.SelectedValue?.ToString();
            _sortMode = val switch
            {
                "Sort by Healing" => SortMode.ByHealing,
                "Sort by Damage Taken" => SortMode.ByTaken,
                _ => SortMode.ByDamage
            };

            // Re-render the currently-selected view
            if (segmented1.SelectIndex == 0)
            {
                if (select1.SelectedValue is ComboItemBattle b && b.Snapshot != null)
                    DumpSnapshot(b.Snapshot);
            }
            else
            {
                if (select1.SelectedValue is ComboItemFull f && f.Snapshot != null)
                    DumpFullSnapshot(f.Snapshot);
            }
        }
    }
}
