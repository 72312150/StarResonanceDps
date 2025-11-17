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

using static System.Net.Mime.MediaTypeNames;

namespace StarResonanceDpsAnalysis.WinForm.Forms.AuxiliaryForms
{
    public partial class SkillReferenceForm : BorderlessForm
    {
        public SkillReferenceForm()
        {
            InitializeComponent();
            FormGui.SetDefaultGUI(this); // Apply the default UI styling (fonts, spacing, shadows, etc.)
            FormGui.SetColorMode(this, AppConfig.IsLight); // Apply the configured theme colors
            ToggleTableView();
        }

        private void SkillReferenceForm_Load(object sender, EventArgs e)
        {
            TitleText.Font = AppConfig.SaoFont;
            divider1.Font = AppConfig.ContentFont;
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
                new AntdUI.Column("Name","Skill Name"){ Fixed = true},
                new AntdUI.Column("Damage","Total Damage"){ Fixed = true},
                new AntdUI.Column("HitCount","Hits") { Fixed = true},
                new AntdUI.Column("CritRate","Critical Rate") { Fixed = true},
                new AntdUI.Column("LuckyRate","Lucky Rate") { Fixed = true},
                new Column("AvgPerHit","Average") { Fixed = true},
                new AntdUI.Column("TotalDps","DPS") { Fixed = true},
                new AntdUI.Column("Share","Skill Share") { Fixed = true},



            };

            table_DpsDetailDataTable.Binding(DamageReferenceSkillData.DamageReferenceSkillTable);

        }

        public async void LoadInformation(string battleId, string nickName)
        {
            //divider1.Text = nickName + " skill reference data";
            //DamageReferenceSkillData.DamageReferenceSkillTable.Clear();
            //string url = @$"{AppConfig.url}/get_user_dps";
            //var query = new
            //{
            //    battleId
            //};
            //var data = await Common.RequestGet(url, query);
            //if (data["code"].ToString() == "200")
            //{
            //    foreach (var item in data["data"])
            //    {
            //        string name = item["name"].ToString(); // Player name
            //        string damage = Common.FormatWithEnglishUnits(item["damage"]); // Total damage
            //        int hitCount = Convert.ToInt32(item["hitCount"]); // Hit count
            //        string critRate = item["critRate"].ToString() + "%"; // Critical rate
            //        string luckyRate = item["luckyRate"].ToString() + "%"; // Lucky rate
            //        string avgPerHit = Common.FormatWithEnglishUnits(item["avgPerHit"]); // Average per hit
            //        string totalDps = Common.FormatWithEnglishUnits(item["totalDps"]); // DPS
            //        double share = Convert.ToDouble(item["share"]) * 100; // Skill share percentage
            //        DamageReferenceSkillData.DamageReferenceSkillTable.Add(new DamageReferenceSkill(name, damage, hitCount, critRate, luckyRate, avgPerHit, totalDps, share));
            //    }

            //}

            //this.Activate();

        }

        private void TitleText_MouseDown(object sender, MouseEventArgs e)
        {

            if (e.Button == MouseButtons.Left)
            {
                FormManager.ReleaseCapture();
                FormManager.SendMessage(this.Handle, FormManager.WM_NCLBUTTONDOWN, FormManager.HTCAPTION, 0);
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            MessageBox.Show("Decorative button.");
        }
    }
}
