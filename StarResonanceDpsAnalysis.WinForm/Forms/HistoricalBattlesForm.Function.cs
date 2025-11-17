using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using StarResonanceDpsAnalysis.WinForm.Plugin;

namespace StarResonanceDpsAnalysis.WinForm.Forms
{
    public partial class HistoricalBattlesForm
    {
        public void ToggleTableView()
        {

            table_DpsDetailDataTable.Columns.Clear();

            table_DpsDetailDataTable.Columns = new AntdUI.ColumnCollection
            {
                new AntdUI.Column("Uid", "UID"),
                new AntdUI.Column("NickName", "Name"),
                new AntdUI.Column("Profession", "Class"),
                new AntdUI.Column("CombatPower", "Combat Power"),
                new AntdUI.Column("TotalDamage", "Total Damage"),
                new AntdUI.Column("TotalDps", "Average DPS"),
                new AntdUI.Column("CritRate", "Critical Rate"),
                new AntdUI.Column("LuckyRate", "Lucky Rate"),
                new AntdUI.Column("CriticalDamage", "Critical Damage"),
                new AntdUI.Column("LuckyDamage", "Lucky Damage"),
                new AntdUI.Column("CritLuckyDamage", "Critical + Lucky Damage"),
                new AntdUI.Column("MaxInstantDps", "Peak DPS"),

                new AntdUI.Column("TotalHealingDone", "Total Healing"),
                new AntdUI.Column("TotalHps", "Average HPS"),
                new AntdUI.Column("CriticalHealingDone", "Critical Healing"),
                new AntdUI.Column("LuckyHealingDone", "Lucky Healing"),
                new AntdUI.Column("CritLuckyHealingDone", "Critical + Lucky Healing"),
                new AntdUI.Column("MaxInstantHps", "Peak HPS"),
                new AntdUI.Column("DamageTaken", "Damage Taken"),
               // new AntdUI.Column("Share","Damage Share"),
                new AntdUI.Column("DmgShare","Team Damage Share (%)"),
            };

            table_DpsDetailDataTable.Binding(DpsTableDatas.DpsTable);


        }
    }


}
