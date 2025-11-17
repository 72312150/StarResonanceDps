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
using StarResonanceDpsAnalysis.WinForm.Plugin;
using StarResonanceDpsAnalysis.WinForm.Plugin.DamageStatistics;

namespace StarResonanceDpsAnalysis.WinForm.Forms
{
    public partial class DeathStatisticsForm : BorderlessForm
    {
        public DeathStatisticsForm()
        {
            InitializeComponent();
            FormGui.SetDefaultGUI(this); // Apply the default UI styling (fonts, spacing, shadows, etc.)
            FormGui.SetColorMode(this, AppConfig.IsLight); // Apply the configured theme colors
            // Load death statistics
            ToggleTableView();
            // Apply fonts
            SetDefaultFontFromResources();
        }

        /// <summary>
        /// Apply default fonts.
        /// </summary>
        private void SetDefaultFontFromResources()
        {
            TitleText.Font = AppConfig.SaoFont;

            table_DpsDetailDataTable.Font = AppConfig.ContentFont;

        }

        public void ToggleTableView()
        {



            table_DpsDetailDataTable.Columns = new AntdUI.ColumnCollection
            {   new("", "No.")
                {

                    Render = (value, record, rowIndex) => rowIndex + 1,
                    Fixed = true
                },
                new AntdUI.Column("NickName","Player"){ Fixed = true},
                new AntdUI.Column("TotalDeathCount","Deaths"){ Fixed = true},


            };

            table_DpsDetailDataTable.Binding(DeathStatisticsTableDatas.DeathStatisticsTable);
            LoadInformation();


        }


        private void DeathStatisticsForm_Load(object sender, EventArgs e)
        {


        }

        /// <summary>
        /// Refresh death statistics from full-record data.
        /// </summary>
        private void LoadInformation()
        {
            DeathStatisticsTableDatas.DeathStatisticsTable.Clear();
            var rows = FullRecord.GetAllPlayerDeathCounts();

            foreach (var item in rows)
            {
                var uid = item.Uid;
                string nickName = item.Nickname;
                int totalDeathCount = item.Deaths;
                // Look for an existing entry for this player
                var existing = DeathStatisticsTableDatas.DeathStatisticsTable
                    .FirstOrDefault(x => x.Uid == uid);

                if (existing != null)
                {

                    existing.TotalDeathCount = totalDeathCount;
                }
                else
                {

                    DeathStatisticsTableDatas.DeathStatisticsTable
                        .Add(new DeathStatisticsTable(uid, nickName, totalDeathCount));
                }
            }
            // === Compute total deaths and append to the table ===
            int totalDeaths = rows.Sum(r => r.Deaths);

            // Check whether a “Total” row (Uid = 0) already exists
            var totalRow = DeathStatisticsTableDatas.DeathStatisticsTable
                .FirstOrDefault(x => x.Uid == 0);

            if (totalRow != null)
            {
                totalRow.TotalDeathCount = totalDeaths;
                totalRow.NickName = "Total";
            }
            else
            {
                DeathStatisticsTableDatas.DeathStatisticsTable
                    .Add(new DeathStatisticsTable(0, "Total", totalDeaths));
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            LoadInformation(); // Refresh
        }

        private void TitleText_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                FormManager.ReleaseCapture();
                FormManager.SendMessage(this.Handle, FormManager.WM_NCLBUTTONDOWN, FormManager.HTCAPTION, 0);
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            LoadInformation(); // Refresh
        }
    }
}
