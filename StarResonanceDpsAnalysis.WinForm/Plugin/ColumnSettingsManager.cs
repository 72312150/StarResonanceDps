using AntdUI;

namespace StarResonanceDpsAnalysis.WinForm.Plugin
{
    public class ColumnSetting
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public bool IsVisible { get; set; } = false; // 默认全部隐藏
        public Func<Column> Builder { get; set; }
    }

    public static class ColumnSettingsManager
    {
        public static Action? RefreshTableAction { get; set; }

        // 可配置的列设置 - 移除了战力，因为它是默认固定显示的
        public static List<ColumnSetting> AllSettings =
        [
            new() {
                Key = "TotalDamage", Title = "Total Damage", IsVisible = true,
                Builder = () =>  new AntdUI.Column("TotalDamage", "Total Damage",ColumnAlign.Center)
            },
            new() {
                Key = "DamageTaken", Title = "Damage Taken", IsVisible = true,
                Builder = () => new Column("DamageTaken", "Damage Taken", ColumnAlign.Center)
            },
            new() {
                Key = "CriticalDamage", Title = "Critical Damage", IsVisible = true,
                Builder = () => new Column("CriticalDamage", "Critical Damage")
            },
            new() {
                Key = "LuckyDamage", Title = "Lucky Damage", IsVisible = true,
                Builder = () => new Column("LuckyDamage", "Lucky Damage")
            },
            new() {
                Key = "CritLuckyDamage", Title = "Critical + Lucky Damage", IsVisible = true,
                Builder = () => new Column("CritLuckyDamage", "Critical + Lucky Damage")
            },
            new() {
                Key = "InstantDps", Title = "Instant DPS", IsVisible = true,
                Builder = () => new Column("InstantDps", "Instant DPS")
            },
            new() {
                Key = "MaxInstantDps", Title = "Peak Instant DPS", IsVisible = true,
                Builder = () => new Column("MaxInstantDps", "Peak Instant DPS")
            },
            new() {
                Key = "TotalDps", Title = "DPS", IsVisible = true,
                Builder = () => new Column("TotalDps", "DPS", ColumnAlign.Center)
            },
            new() {
                Key = "CritRate", Title = "Critical Rate", IsVisible = true,
                Builder = () => new Column("CritRate", "Critical Rate")
            },
            new() {
                Key = "LuckyRate", Title = "Lucky Rate", IsVisible = true,
                Builder = () => new Column("LuckyRate", "Lucky Rate")
            },
            new() {
                Key = "TotalHealingDone", Title = "Total Healing", IsVisible = true,
                Builder = () => new Column("TotalHealingDone", "Total Healing", ColumnAlign.Center)
            },
            new() {
                Key = "CriticalHealingDone", Title = "Critical Healing", IsVisible = true,
                Builder = () => new Column("CriticalHealingDone", "Critical Healing")
            },
            new() {
                Key = "LuckyHealingDone", Title = "Lucky Healing", IsVisible = true,
                Builder = () => new Column("LuckyHealingDone", "Lucky Healing")
            },
            new() {
                Key = "CritLuckyHealingDone", Title = "Critical + Lucky Healing", IsVisible = true,
                Builder = () => new Column("CritLuckyHealingDone", "Critical + Lucky Healing")
            },
            new() {
                Key = "InstantHps", Title = "Instant HPS", IsVisible = true,
                Builder = () => new Column("InstantHps", "Instant HPS")
            },
            new() {
                Key = "MaxInstantHps", Title = "Peak Instant HPS", IsVisible = true,
                Builder = () => new Column("MaxInstantHps", "Peak Instant HPS")
            },
            new() {
                Key = "TotalHps", Title = "HPS", IsVisible = true,
                Builder = () => new Column("TotalHps", "HPS", ColumnAlign.Center)
            },
        ];

        public static StackedHeaderRow[] BuildStackedHeader()
        {
            var list = new List<StackedColumn[]>();

            // 与上方 Key 一致的分组
            string[] group1 = { "TotalDamage", "CriticalDamage", "LuckyDamage", "CritLuckyDamage" };
            string[] group2 = { "InstantDps", "MaxInstantDps", "TotalDps" };
            string[] group3 = { "TotalHealingDone", "CriticalHealingDone", "LuckyHealingDone", "CritLuckyHealingDone" };
            string[] group4 = { "InstantHps", "MaxInstantHps", "TotalHps" };

            list.Add(BuildGroup(group1, "Total Damage"));
            list.Add(BuildGroup(group2, "DPS"));
            list.Add(BuildGroup(group3, "Total Healing"));
            list.Add(BuildGroup(group4, "HPS"));

            return [new StackedHeaderRow([.. list.SelectMany(x => x)])];
        }

        private static StackedColumn[] BuildGroup(string[] keys, string title)
        {
            var visible = keys.Where(k => AllSettings.FirstOrDefault(x => x.Key == k)?.IsVisible ?? false).ToList();
            return visible.Count > 1
                ? [new StackedColumn(string.Join(',', visible), title)]
                : [];
        }

        public static ColumnCollection BuildColumns()
        {
            var columns = new List<Column>
            {
                new("", "Index")
                {
                    Width = "50",
                    Render = (value, record, rowIndex) => rowIndex + 1,
                    Fixed = true
                },
                // 基础信息列 - 固定显示，不可配置
                new("Uid", "Player UID",ColumnAlign.Center){ SortOrder = true },
                new("NickName", "Nickname",ColumnAlign.Center){ SortOrder = true },
                new("Profession", "Class",ColumnAlign.Center),
                // 战力 - 固定显示，不在设置中配置
                new("CombatPower", "Combat Power", ColumnAlign.Center){ SortOrder = true }
            };

            // 添加可配置的列
            columns.AddRange(AllSettings.Where(s => s.IsVisible).Select(s => s.Builder()));

            return [.. columns];
        }
    }
}
