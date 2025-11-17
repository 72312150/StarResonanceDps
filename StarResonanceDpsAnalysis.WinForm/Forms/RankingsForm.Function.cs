using System;
using System.Threading.Tasks;

using StarResonanceDpsAnalysis.WinForm.Plugin;

namespace StarResonanceDpsAnalysis.WinForm.Forms
{
    public partial class RankingsForm
    {
        public void ToggleTableView()
        {

            table_DpsDetailDataTable.Columns.Clear();

            table_DpsDetailDataTable.Columns = new AntdUI.ColumnCollection
            {   
                new AntdUI.Column("Button", "Actions"),
                new("", "No.")
                {
                   
                    Render = (value, record, rowIndex) => rowIndex + 1,
                    Fixed = true
                },

                new AntdUI.Column("NickName","Player Name"){ Fixed = true},
                new AntdUI.Column("Professional","Class"){ Fixed = true},
                new AntdUI.Column("SubProfessional","Spec"){ Fixed = true},
                new AntdUI.Column("CombatPower","Combat Power"){ Fixed = true,SortOrder=true },
                new AntdUI.Column("TotalDamage","Total Damage"){ Fixed = true,SortOrder=true},
                new AntdUI.Column("InstantDps","DPS"){ Fixed = true,SortOrder=true},
                new AntdUI.Column("CritRate","Crit Rate"){ Fixed = true},
                new AntdUI.Column("LuckyRate","Luck Rate"){ Fixed = true},
              
                new AntdUI.Column("MaxInstantDps","Peak DPS"){ Fixed = true,SortOrder=true},

                //new AntdUI.Column("battleTime","Battle Duration"),
            };

            table_DpsDetailDataTable.Binding(LeaderboardTableDatas.LeaderboardTable);
            get_dps_rank();

        }



        Dictionary<string, string> rank_type_dict = new Dictionary<string, string>()
        {
            {"Damage Reference","damage_all"},
        };
        /// <summary>
        /// 
        /// </summary>
        private async void get_dps_rank()
        {
            //LeaderboardTableDatas.LeaderboardTable.Clear();
            //string url = @$"{AppConfig.url}/get_dps_rank";
            //var query = new
            //{
            //    rank_type = rank_type_dict[divider3.Text],
            //    professional = segmented1.Items[segmented1.SelectIndex],
            //    uid = AppConfig.Uid
            //};
            //var data = await Common.RequestGet(url,query);
            //if (data["code"].ToString()=="200")
            //{
            //    foreach (var item in data["data"])
            //    {
            //        string battleid = item["battleId"].ToString();
            //        string nickName = item["nickName"].ToString();
            //        string professional = item["professional"].ToString();
            //        double combatPower = double.Parse(item["combatPower"].ToString());
            //        double instantDps = double.Parse(item["instantDps"].ToString());
            //        double totalDamage = double.Parse(item["totalDamage"].ToString());
            //        double maxInstantDps = double.Parse(item["maxInstantDps"].ToString());
            //        double critRate = double.Parse(item["critRate"].ToString());
            //        double luckyRate = double.Parse(item["luckyRate"].ToString());
            //        string subProfessional = item["subProfession"].ToString();

            //        //int battleTime = int.Parse(item["battleTime"].ToString());
            //        LeaderboardTableDatas.LeaderboardTable.Add(new LeaderboardTable(battleid, nickName, professional, combatPower, totalDamage,instantDps, critRate, luckyRate, maxInstantDps, subProfessional));
            //    }
              
            //}
            //else
            //{
            //    //AntdUI.Modal.open(new AntdUI.Modal.Config(this, "Fetch Failed", "Fetch Failed")
            //    //{
            //    //    CloseIcon = true,
            //    //    Keyboard = false,
            //    //    MaskClosable = false,
            //    //});
            //}

        }
    }
}
