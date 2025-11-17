using AntdUI;
using StarResonanceDpsAnalysis.Assets;
using StarResonanceDpsAnalysis.WinForm.Forms;
using StarResonanceDpsAnalysis.WinForm.Plugin;
using StarResonanceDpsAnalysis.WinForm.Plugin.Charts;
using StarResonanceDpsAnalysis.WinForm.Plugin.DamageStatistics;

using static StarResonanceDpsAnalysis.WinForm.Forms.DpsStatisticsForm;

namespace StarResonanceDpsAnalysis.WinForm.Control
{
    public partial class SkillDetailForm : BorderlessForm
    {

        // Context selection (defaults to the previous behavior: current battle)
        public DetailContextType ContextType { get; set; } = DetailContextType.Current;

        // Snapshot start time (uses StartedAt to locate the snapshot precisely)
        public DateTime? SnapshotStartTime { get; set; } = null;


        // Trend chart member fields
        private FlatLineChart _dpsTrendChart;
        // Bar and pie chart member fields
        private FlatBarChart _skillDistributionChart;
        private FlatPieChart _critLuckyChart;

        // Selection guard flag
        bool isSelect = false;

        // Splitter adjustment state
        private int _lastSplitterPosition = 350; // Track the previous splitter position (matches designer default)
        private const int SPLITTER_STEP_PIXELS = 30; // Trigger adjustments every 30 pixels
        private const int PADDING_ADJUSTMENT = 15;   // Amount to tweak PaddingRight per step

        private void SetDefaultFontFromResources()
        {

            TitleText.Font = AppConfig.TitleFont;
            label1.Font = AppConfig.HeaderFont;
            label2.Font = label3.Font = label4.Font = AppConfig.ContentFont;

            var harmonyOsSansFont_Size11 = HandledAssets.HarmonyOS_Sans(11);
            label3.Font = label9.Font = harmonyOsSansFont_Size11;

            var harmonyOsSansFont_Size12 = HandledAssets.HarmonyOS_Sans(12);
            NickNameText.Font = harmonyOsSansFont_Size12;

            var digitalFontsControls = new List<System.Windows.Forms.Control>()
            {
                BeatenLabel, AvgDamageText, LuckyDamageText, LuckyTimesLabel,
                CritDamageText, NormalDamageText, NumberCriticalHitsLabel, LuckyRate,
                CritRateText, NumberHitsLabel, TotalDpsText, TotalDamageText
            };
            foreach (var c in digitalFontsControls)
            {
                c.Font = AppConfig.DigitalFont;
            }

            var contentFontControls = new List<System.Windows.Forms.Control>()
            {
                table_DpsDetailDataTable, label13, label14, label1, label2,
                label4, label5, label6, label7, label8, label9, label17,
                NumberCriticalHitsText, UidText, PowerText, segmented1, collapse1,
                label10,label19
            };
            foreach (var c in contentFontControls)
            {
                c.Font = AppConfig.ContentFont;
            }
        }

        public SkillDetailForm()
        {
            InitializeComponent();
            FormGui.SetDefaultGUI(this);
            SetDefaultFontFromResources();


            ToggleTableView();
        }

        private int fixedWidth = 1911; // Form width
        private void SkillDetailForm_Load(object sender, EventArgs e)
        {
            FormGui.SetColorMode(this, AppConfig.IsLight); // Apply the configured theme colors

            isSelect = true;
            select1.Items = new AntdUI.BaseCollection() { "Sort by Damage", "Sort by DPS", "Sort by Hits", "Sort by Critical Rate" };
            select1.SelectedIndex = 0;
            isSelect = false;

            // Initialize and add the line chart to the collapse item
            InitializeDpsTrendChart();

            // Initialize and add the bar and pie charts
            InitializeSkillDistributionChart();
            InitializeCritLuckyChart();

            // Subscribe to the collapse item resize event to keep the chart sized correctly
            collapseItem1.Resize += Panel7_Resize;

            // Attach splitter1 events explicitly to ensure handlers fire
            splitter1.SplitterMoving += splitter1_SplitterMoving;
            splitter1.SplitterMoved += splitter1_SplitterMoved;

            // Set a minimum of 350 on the splitter to prevent dragging too far left
            splitter1.Panel1MinSize = 350;

            // Initialize splitter position tracking so it stays in sync
            _lastSplitterPosition = splitter1.SplitterDistance;

            // Ensure the chart starts with the correct baseline (350px => PaddingRight=160, 5 grid lines)
            if (_dpsTrendChart != null)
            {
                var offsetFrom350 = splitter1.SplitterDistance - 350;
                var steps = offsetFrom350 / SPLITTER_STEP_PIXELS;
                var initialPadding = Math.Max(10, Math.Min(300, 160 - steps * PADDING_ADJUSTMENT));
                var initialGridLines = Math.Max(3, Math.Min(10, 5 + steps)); // Adjusted maximum from 20 to 10

                _dpsTrendChart.SetPaddingRight(initialPadding);
                _dpsTrendChart.SetVerticalGridLines(initialGridLines);

                //Console.WriteLine($"Chart init — splitter: {splitter1.SplitterDistance}, padding: {initialPadding}, vertical lines: {initialGridLines}");
            }
        }

        /// <summary>
        /// Configure the trend chart refresh callback (avoid duplicate logic).
        /// </summary>
        private void SetupTrendChartRefreshCallback()
        {
            //if (_dpsTrendChart == null) return;

            //_dpsTrendChart.SetRefreshCallback(() =>
            //{
            //    try
            //    {
            //        var dataType = segmented1.SelectIndex switch
            //        {
            //            0 => ChartDataType.Damage,
            //            1 => ChartDataType.Healing,
            //            2 => ChartDataType.TakenDamage,
            //            _ => ChartDataType.Damage
            //        };

            //        var source = FormManager.showTotal ? ChartDataSource.FullRecord : ChartDataSource.Current;
            //        ChartVisualizationService.RefreshDpsTrendChart(_dpsTrendChart, Uid, dataType, source);
            //    }
            //    catch (Exception ex)
            //    {
            //        Console.WriteLine($"Failed to refresh chart callback: {ex.Message}");
            //    }
            //});
        }

        /// <summary>
        /// Handle collapse item size changes.
        /// </summary>
        private void Panel7_Resize(object sender, EventArgs e)
        {
            if (_dpsTrendChart != null)
            {
                try
                {
                    // Dock.Fill keeps the chart sized automatically
                    // We only need to delay the redraw until after layout settles
                    var resizeTimer = new System.Windows.Forms.Timer { Interval = 100 };
                    resizeTimer.Tick += (s, args) =>
                    {
                        resizeTimer.Stop();
                        resizeTimer.Dispose();

                        if (_dpsTrendChart != null && !_dpsTrendChart.IsDisposed)
                        {
                            _dpsTrendChart.Invalidate();
                        }
                    };
                    resizeTimer.Start();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error while resizing chart: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Initialize the DPS trend chart.
        /// </summary>
        private void InitializeDpsTrendChart()
        {
            try
            {
                // Clear existing controls from the collapse item
                collapseItem1.Controls.Clear();

                // Ensure the collapse item has the correct min size and auto-resize settings
                collapseItem1.MinimumSize = new Size(ChartConfigManager.MIN_WIDTH, ChartConfigManager.MIN_HEIGHT);
                collapseItem1.Anchor = AnchorStyles.Top | AnchorStyles.Left;

                // Create the DPS trend chart using the shared configuration (defaults follow the global source)
                _dpsTrendChart = ChartVisualizationService.CreateDpsTrendChart(specificPlayerId: Uid);

                // Make the chart dock-fill so it matches the other charts
                _dpsTrendChart.Dock = DockStyle.Fill;

                // Hook up the realtime refresh callback for the current player
                SetupTrendChartRefreshCallback();

                // Add the chart to the collapse item
                collapseItem1.Controls.Add(_dpsTrendChart);

                // Ensure the control is added before refreshing data
                Application.DoEvents(); // Flush UI updates before refreshing data

                // Initial data refresh
                RefreshDpsTrendChart();
            }
            catch (Exception ex)
            {
                // If initialization fails, render an error label
                var errorLabel = new AntdUI.Label
                {
                    Text = $"Failed to initialize chart: {ex.Message}",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Color.Red,
                    Font = new Font("Microsoft YaHei", 10, FontStyle.Regular)
                };
                collapseItem1.Controls.Add(errorLabel);

                Console.WriteLine($"Failed to initialize chart: {ex}");
            }
        }

        /// <summary>
        /// Refresh the DPS trend chart data.
        /// </summary>
        private void RefreshDpsTrendChart()
        {
            //if (_dpsTrendChart != null)
            //{
            //    try
            //    {
            //        var dataType = segmented1.SelectIndex switch
            //        {
            //            0 => ChartDataType.Damage,      // Damage
            //            1 => ChartDataType.Healing,     // Healing
            //            2 => ChartDataType.TakenDamage, // Damage taken
            //            _ => ChartDataType.Damage       // Default to damage
            //        };

            //        var source = FormManager.showTotal ? ChartDataSource.FullRecord : ChartDataSource.Current;
            //        ChartVisualizationService.RefreshDpsTrendChart(_dpsTrendChart, Uid, dataType, source);
            //    }
            //    catch (Exception ex)
            //    {
            //        Console.WriteLine($"Failed to refresh DPS trend chart: {ex.Message}");
            //    }
            //}
        }

        private bool _suspendUiUpdate = false;

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (_suspendUiUpdate) return;

            SelectDataType();

            // Charts now refresh themselves; only perform essential data checks here
            // RefreshDpsTrendChart(); // Manual refresh removed; chart handles this internally
        }

        private void segmented1_SelectIndexChanged(object sender, IntEventArgs e)
        {
            select1.Items.Clear();
            isSelect = true;
            label3.Text = "Damage Summary";
            label1.Text = "Total Damage";
            label2.Text = "DPS";
            label4.Text = "Critical Rate";
            label5.Text = "Lucky Rate";
            switch (e.Value)
            {
                case 0:
                    select1.Items = new AntdUI.BaseCollection() { "Sort by Damage", "Sort by DPS", "Sort by Hits", "Sort by Critical Rate" };
                    break;
                case 1:
                    select1.Items = new AntdUI.BaseCollection() { "Sort by Healing", "Sort by HPS", "Sort by Hits", "Sort by Critical Rate" };
                    label3.Text = "Healing Summary";
                    label1.Text = "Total Healing";
                    label2.Text = "HPS";
                    label4.Text = "Critical Rate";
                    label5.Text = "Lucky Rate";
                    break;
                case 2:
                    select1.Items = new AntdUI.BaseCollection() { "Sort by Damage Taken", "Sort by Damage Taken per Second", "Sort by Hits Taken", "Sort by Critical Rate" };
                    label3.Text = "Damage Taken Summary";
                    label1.Text = "Total Damage Taken";
                    label2.Text = "Damage Taken / s";
                    label4.Text = "Highest Hit Taken";
                    label5.Text = "Lowest Hit Taken";
                    break;
            }

            select1.SelectedValue = select1.Items[0];
            // Manually refresh the UI


            isSelect = false;
            // Suspend updates once
            _suspendUiUpdate = true;

            // Clear the skill table when switching to avoid stale data
            SkillTableDatas.SkillTable.Clear();

            // Refresh immediately using the new mode
            bool isHeal = segmented1.SelectIndex != 0;
            SelectDataType();

            // Update chart data
            UpdateSkillDistributionChart();
            UpdateCritLuckyChart();

            // Resume on the next timer tick
            _suspendUiUpdate = false;

        }

        private void select1_SelectedIndexChanged(object sender, IntEventArgs e)
        {
            //if (isSelect) return;

            //// 1) Determine the metric (segmented1: 0=Damage 1=Healing 2=Taken)
            //MetricType metric = segmented1.SelectIndex switch
            //{
            //    1 => MetricType.Healing,
            //    2 => MetricType.Taken,
            //    _ => MetricType.Damage
            //};

            //// 2) Set up sorting (returns double to avoid variance issues)
            //SkillOrderBySelector = e.Value switch
            //{
            //    0 => s => s.Total,       // Total amount
            //    1 => s => s.TotalDps,    // DPS
            //    2 => s => s.HitCount,    // Hit count
            //    3 => s => s.CritRate,    // Critical rate
            //    _ => s => s.Total
            //};

            //// 3) Determine the data source (single fight vs full session)
            //SourceType source = FormManager.showTotal ? SourceType.FullRecord : SourceType.Current;

            //// 4) Refresh the skill table (internally uses SkillOrderBySelector)
            //UpdateSkillTable(Uid, source, metric);

            //// (Optional) Update the charts on the right as well:
            //try { RefreshDpsTrendChart(); } catch { /* Ignore rendering exceptions */ }
            //UpdateSkillDistributionChart();
            //UpdateCritLuckyChart();
        }

        private static readonly Dictionary<string, Image> _professionImages = new()
        {
            { "冰魔导师", HandledAssets.冰魔导师_Opacity10 },
            { "Frost Mage", HandledAssets.冰魔导师_Opacity10 },
            { "巨刃守护者", HandledAssets.巨刃守护者_Opacity10 },
            { "Heavy Guardian", HandledAssets.巨刃守护者_Opacity10 },
            { "森语者", HandledAssets.森语者_Opacity10 },
            { "Verdant Oracle", HandledAssets.森语者_Opacity10 },
            { "灵魂乐手", HandledAssets.灵魂乐手_Opacity10 },
            { "Soul Musician", HandledAssets.灵魂乐手_Opacity10 },
            { "神射手", HandledAssets.神射手_Opacity10 },
            { "Marksman", HandledAssets.神射手_Opacity10 },
            { "神盾骑士", HandledAssets.神盾骑士_Opacity10 },
            { "Shield Knight", HandledAssets.神盾骑士_Opacity10 },
            { "雷影剑士", HandledAssets.雷影剑士_Opacity10 },
            { "Stormblade", HandledAssets.雷影剑士_Opacity10 },
            { "青岚骑士", HandledAssets.青岚骑士_Opacity10 },
            { "Wind Knight", HandledAssets.青岚骑士_Opacity10 },
        };

        public void GetPlayerInfo(string nickname, int power, string profession)
        {
            NickNameText.Text = nickname;
            PowerText.Text = power.ToString();
            UidText.Text = Uid.ToString();
            LevelLabel.Text = StatisticData._manager.GetAttrKV(Uid, "level")?.ToString() ?? "";
            Rank_levelLabel.Text = StatisticData._manager.GetAttrKV(Uid, "rank_level")?.ToString() ?? "";

            var flag = _professionImages.TryGetValue(profession, out var img);
            table_DpsDetailDataTable.BackgroundImage = flag ? img : null;

            if (_dpsTrendChart != null)
            {
                SetupTrendChartRefreshCallback();
                // Immediately refresh the chart data
                RefreshDpsTrendChart();
            }
        }

        public void ResetDpsTrendChart()
        {
            if (_dpsTrendChart != null)
            {
                try
                {
                    _dpsTrendChart.FullReset();
                    ChartConfigManager.ApplySettings(_dpsTrendChart);

                    if (ChartVisualizationService.IsCapturing)
                    {
                        _dpsTrendChart.StartAutoRefresh(ChartConfigManager.REFRESH_INTERVAL);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to reset DPS trend chart: {ex.Message}");
                }
            }
        }

        private void splitter1_SplitterMoving(object sender, SplitterCancelEventArgs e)
        {
            if (e.SplitX < 350)
            {
                e.Cancel = true;
                return;
            }

            var offsetFrom350 = e.SplitX - 350;
            var steps = offsetFrom350 / SPLITTER_STEP_PIXELS; // Determine how many 30px steps were moved

            var newPadding = Math.Max(10, Math.Min(300, 160 - steps * PADDING_ADJUSTMENT));
            var newGridLines = Math.Max(3, Math.Min(10, 5 + steps));

            if (_dpsTrendChart != null)
            {
                var currentGridLines = _dpsTrendChart.GetVerticalGridLines();
                var currentPadding = _dpsTrendChart.GetPaddingRight();

                if (currentGridLines != newGridLines || currentPadding != newPadding)
                {
                    _dpsTrendChart.SetPaddingRight(newPadding);
                    _dpsTrendChart.SetVerticalGridLines(newGridLines);
                }
            }
        }

        private void splitter1_SplitterMoved(object sender, SplitterEventArgs e)
        {
            if (_dpsTrendChart != null)
            {
                var offsetFrom350 = e.SplitX - 350;
                var steps = offsetFrom350 / SPLITTER_STEP_PIXELS;

                _lastSplitterPosition = 350 + steps * SPLITTER_STEP_PIXELS;

                var finalPadding = Math.Max(10, Math.Min(300, 160 - steps * PADDING_ADJUSTMENT));
                var finalGridLines = Math.Max(3, Math.Min(10, 5 + steps));

                _dpsTrendChart.SetPaddingRight(finalPadding);
                _dpsTrendChart.SetVerticalGridLines(finalGridLines);

                _dpsTrendChart.Invalidate();
            }
        }

        private void TitleText_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                FormManager.ReleaseCapture();
                FormManager.SendMessage(this.Handle, FormManager.WM_NCLBUTTONDOWN, FormManager.HTCAPTION, 0);
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            SelectDataType();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            _dpsTrendChart?.StopAutoRefresh();
            Close();
        }

        private void ApplyThemeToCharts()
        {
            var isDark = !Config.IsLight;
            if (_dpsTrendChart != null)
            {
                _dpsTrendChart.IsDarkTheme = isDark;
                _dpsTrendChart.Invalidate();
            }
            if (_skillDistributionChart != null)
            {
                _skillDistributionChart.IsDarkTheme = isDark;
                _skillDistributionChart.Invalidate();
            }
            if (_critLuckyChart != null)
            {
                _critLuckyChart.IsDarkTheme = isDark;
                _critLuckyChart.Invalidate();
            }
        }

        private void SkillDetailForm_ForeColorChanged(object sender, EventArgs e)
        {
            if (Config.IsLight)
            {
                table_DpsDetailDataTable.RowSelectedBg = ColorTranslator.FromHtml("#AED4FB");
                panel1.Back = panel2.Back = ColorTranslator.FromHtml("#67AEF6");
            }
            else
            {
                table_DpsDetailDataTable.RowSelectedBg = ColorTranslator.FromHtml("#10529a");
                panel1.Back = panel2.Back = ColorTranslator.FromHtml("#255AD0");
            }

            ApplyThemeToCharts();
        }

        private void InitializeSkillDistributionChart()
        {
            try
            {
                _skillDistributionChart = new FlatBarChart
                {
                    Dock = DockStyle.Fill,
                    TitleText = "",
                    XAxisLabel = "",
                    YAxisLabel = "",
                    IsDarkTheme = !Config.IsLight
                };
                collapseItem3.Controls.Clear();
                collapseItem3.Controls.Add(_skillDistributionChart);
                UpdateSkillDistributionChart();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize crit/luck chart: {ex.Message}");
            }
        }

        private void InitializeCritLuckyChart()
        {
            try
            {
                _critLuckyChart = new FlatPieChart
                {
                    Dock = DockStyle.Fill,
                    TitleText = "",
                    ShowLabels = true,
                    ShowPercentages = true,
                    IsDarkTheme = !Config.IsLight
                };
                collapseItem2.Controls.Clear();
                collapseItem2.Controls.Add(_critLuckyChart);
                UpdateCritLuckyChart();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize skill share chart: {ex.Message}");
            }
        }
    }
}
