using AntdUI;
using StarResonanceDpsAnalysis.WinForm.Plugin;
using StarResonanceDpsAnalysis.WinForm.Plugin.Charts;
using StarResonanceDpsAnalysis.WinForm.Plugin.DamageStatistics;

using SystemPanel = System.Windows.Forms.Panel;

namespace StarResonanceDpsAnalysis.WinForm.Forms
{
    /// <summary>
    /// Realtime chart window that wires up the flat charts and keeps them updated.
    /// </summary>
    public partial class RealtimeChartsForm : BorderlessForm
    {
        private Tabs _tabControl;
        private FlatLineChart _dpsTrendChart;
        private FlatPieChart _skillPieChart;
        private FlatBarChart _teamDpsChart;
        private FlatScatterChart _multiDimensionChart;
        private FlatBarChart _damageTypeChart;
        private Dropdown _playerSelector;

        // Control buttons
        private AntdUI.Button _refreshButton;
        private AntdUI.Button _closeButton;
        private AntdUI.Button _autoRefreshToggle;

        // Auto-refresh state
        private System.Windows.Forms.Timer _autoRefreshTimer;
        private bool _autoRefreshEnabled = false;

        // Dragging support
        private bool _isDragging = false;
        private Point _dragStartPoint;
        private SystemPanel _draggablePanel;

        public RealtimeChartsForm()
        {
            InitializeComponent();
            FormGui.SetDefaultGUI(this);

            Text = "Realtime Chart Visualizer";
            Size = new Size(1000, 700);
            StartPosition = FormStartPosition.CenterScreen;

            // Standard font
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);

            InitializeControls();
            InitializeAutoRefreshTimer();

            // Apply current theme
            RefreshChartsTheme();

            // Load charts
            LoadAllCharts();

            // Enable auto refresh by default
            EnableAutoRefreshByDefault();
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            // 
            // RealtimeChartsForm
            // 
            ClientSize = new Size(1000, 700);
            Name = "RealtimeChartsForm";
            Load += RealtimeChartsForm_Load;
            ResumeLayout(false);
        }

        private void InitializeControls()
        {
            // Create draggable control panel
            _draggablePanel = new SystemPanel
            {
                Height = 50,
                Dock = DockStyle.Top,
                Padding = new Padding(10, 5, 10, 5),
                Cursor = Cursors.SizeAll // indicate draggability
            };

            // Hook drag events
            _draggablePanel.MouseDown += DraggablePanel_MouseDown;
            _draggablePanel.MouseMove += DraggablePanel_MouseMove;
            _draggablePanel.MouseUp += DraggablePanel_MouseUp;

            _refreshButton = new AntdUI.Button
            {
                Text = "Refresh Now",
                Type = TTypeMini.Primary,
                Size = new Size(80, 35),
                Location = new Point(10, 8),
                Font = Font
            };
            _refreshButton.Click += RefreshButton_Click;

            _autoRefreshToggle = new AntdUI.Button
            {
                Text = "Auto Refresh: On", // default to enabled
                Type = TTypeMini.Primary, // primary style when enabled
                Size = new Size(100, 35),
                Location = new Point(100, 8),
                Font = Font
            };
            _autoRefreshToggle.Click += AutoRefreshToggle_Click;

            _closeButton = new AntdUI.Button
            {
                Text = "Close",
                Type = TTypeMini.Default,
                Size = new Size(60, 35),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(_draggablePanel.Width - 70, 8),
                Font = Font
            };
            _closeButton.Click += CloseButton_Click;

            _draggablePanel.Controls.Add(_refreshButton);
            _draggablePanel.Controls.Add(_autoRefreshToggle);
            _draggablePanel.Controls.Add(_closeButton);

            // Tab container
            _tabControl = new Tabs
            {
                Dock = DockStyle.Fill,
                Font = Font
            };

            // Add tab pages
            _tabControl.Pages.Add(new AntdUI.TabPage
            {
                Text = "DPS Trend",
                Font = Font
            });
            _tabControl.Pages.Add(new AntdUI.TabPage
            {
                Text = "Skill Share",
                Font = Font
            });
            _tabControl.Pages.Add(new AntdUI.TabPage
            {
                Text = "Team DPS",
                Font = Font
            });
            _tabControl.Pages.Add(new AntdUI.TabPage
            {
                Text = "Multi-metric",
                Font = Font
            });
            _tabControl.Pages.Add(new AntdUI.TabPage
            {
                Text = "Damage Breakdown",
                Font = Font
            });

            // Prepare page containers
            for (int i = 0; i < 5; i++)
            {
                var panel = new SystemPanel
                {
                    Dock = DockStyle.Fill,
                    BackColor = AppConfig.IsLight ? Color.White : Color.FromArgb(31, 31, 31)
                };
                _tabControl.Pages[i].Controls.Add(panel);
            }

            // Add player selector to the skill share tab
            var skillChartPage = _tabControl.Pages[1];
            var skillChartPanel = skillChartPage.Controls[0] as SystemPanel;

            var playerSelectorPanel = new SystemPanel
            {
                Height = 50,
                Dock = DockStyle.Top,
                Padding = new Padding(10)
            };

            var playerLabel = new AntdUI.Label
            {
                Text = "Select Player:",
                Location = new Point(10, 15),
                AutoSize = true,
                Font = Font
            };

            _playerSelector = new Dropdown
            {
                Location = new Point(90, 10),
                Size = new Size(200, 30),
                Font = Font
            };
            _playerSelector.SelectedValueChanged += PlayerSelector_SelectedValueChanged;

            playerSelectorPanel.Controls.Add(playerLabel);
            playerSelectorPanel.Controls.Add(_playerSelector);
            skillChartPanel.Controls.Add(playerSelectorPanel);

            Controls.Add(_tabControl);
            Controls.Add(_draggablePanel);
        }

        #region Drag Handling

        private void DraggablePanel_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                _dragStartPoint = e.Location;
                _draggablePanel.Cursor = Cursors.Hand;
            }
        }

        private void DraggablePanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && e.Button == MouseButtons.Left)
            {
                // Calculate delta
                var deltaX = e.Location.X - _dragStartPoint.X;
                var deltaY = e.Location.Y - _dragStartPoint.Y;

                // Move window
                this.Location = new Point(this.Location.X + deltaX, this.Location.Y + deltaY);
            }
        }

        private void DraggablePanel_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = false;
                _draggablePanel.Cursor = Cursors.SizeAll;
            }
        }

        #endregion

        private void EnableAutoRefreshByDefault()
        {
            _autoRefreshEnabled = true;
            _autoRefreshTimer.Enabled = true;
            _autoRefreshToggle.Text = "Auto Refresh: On";
            _autoRefreshToggle.Type = TTypeMini.Primary;
        }

        private void LoadAllCharts()
        {
            try
            {
                // DPS trend chart
                var dpsTrendPanel = _tabControl.Pages[0].Controls[0] as SystemPanel;
                _dpsTrendChart = ChartVisualizationService.CreateDpsTrendChart();
                dpsTrendPanel.Controls.Add(_dpsTrendChart);

                // Skill share chart
                var skillChartPanel = _tabControl.Pages[1].Controls[0] as SystemPanel;
                UpdatePlayerSelector();
                var selectedPlayer = _playerSelector.SelectedValue as PlayerSelectorItem;
                var playerId = selectedPlayer?.Uid ?? 0;
                _skillPieChart = ChartVisualizationService.CreateSkillDamagePieChart(playerId);
                skillChartPanel.Controls.Add(_skillPieChart);

                // Team DPS chart
                var teamDpsPanel = _tabControl.Pages[2].Controls[0] as SystemPanel;
                _teamDpsChart = ChartVisualizationService.CreateTeamDpsBarChart();
                teamDpsPanel.Controls.Add(_teamDpsChart);

                // Multi metric chart
                var multiDimensionPanel = _tabControl.Pages[3].Controls[0] as SystemPanel;
                _multiDimensionChart = ChartVisualizationService.CreateDpsRadarChart();
                multiDimensionPanel.Controls.Add(_multiDimensionChart);

                // Damage breakdown chart
                var damageTypePanel = _tabControl.Pages[4].Controls[0] as SystemPanel;
                _damageTypeChart = ChartVisualizationService.CreateDamageTypeStackedChart();
                damageTypePanel.Controls.Add(_damageTypeChart);

                // Initial refresh
                RefreshAllCharts();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load charts: {ex.Message}");
                MessageBox.Show($"Failed to load charts:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void InitializeAutoRefreshTimer()
        {
            _autoRefreshTimer = new System.Windows.Forms.Timer
            {
                Interval = 100, // 100 ms default refresh cadence
                Enabled = false
            };
            _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
        }

        #region Event Handlers

        private void RefreshButton_Click(object sender, EventArgs e)
        {
            RefreshAllCharts();

            // Indicate refresh state
            _refreshButton.Text = "Refreshing...";
            _refreshButton.Enabled = false;

            var resetTimer = new System.Windows.Forms.Timer { Interval = 300 };
            resetTimer.Tick += (s, args) =>
            {
                _refreshButton.Text = "Refresh Now";
                _refreshButton.Enabled = true;
                resetTimer.Stop();
                resetTimer.Dispose();
            };
            resetTimer.Start();
        }

        private void AutoRefreshToggle_Click(object sender, EventArgs e)
        {
            _autoRefreshEnabled = !_autoRefreshEnabled;
            _autoRefreshTimer.Enabled = _autoRefreshEnabled;

            _autoRefreshToggle.Text = $"Auto Refresh: {(_autoRefreshEnabled ? "On" : "Off")}";
            _autoRefreshToggle.Type = _autoRefreshEnabled ? TTypeMini.Primary : TTypeMini.Default;
        }

        private void AutoRefreshTimer_Tick(object sender, EventArgs e)
        {
            RefreshAllCharts();
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void PlayerSelector_SelectedValueChanged(object sender, ObjectNEventArgs e)
        {
            if (_playerSelector.SelectedValue is PlayerSelectorItem item && _skillPieChart != null)
            {
                ChartVisualizationService.RefreshSkillDamagePieChart(_skillPieChart, item.Uid);
            }
        }

        #endregion

        private void RefreshAllCharts()
        {
            try
            {
                // Update source data
                ChartVisualizationService.UpdateAllDataPoints();

                // Refresh charts without losing cached state
                if (_dpsTrendChart != null)
                {
                    ChartVisualizationService.RefreshDpsTrendChart(_dpsTrendChart, null, ChartDataType.Damage);
                    _dpsTrendChart.ReloadPersistentData(); // reload to avoid data loss
                }

                if (_skillPieChart != null)
                {
                    var selectedPlayer = _playerSelector.SelectedValue as PlayerSelectorItem;
                    ChartVisualizationService.RefreshSkillDamagePieChart(_skillPieChart, selectedPlayer?.Uid ?? 0);
                }

                if (_teamDpsChart != null)
                    ChartVisualizationService.RefreshTeamDpsBarChart(_teamDpsChart);

                if (_multiDimensionChart != null)
                    ChartVisualizationService.RefreshDpsRadarChart(_multiDimensionChart);

                if (_damageTypeChart != null)
                    ChartVisualizationService.RefreshDamageTypeStackedChart(_damageTypeChart);

                // Refresh player dropdown
                UpdatePlayerSelector();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to refresh charts: {ex.Message}");
            }
        }

        private void UpdatePlayerSelector()
        {
            var players = StatisticData._manager.GetPlayersWithCombatData().ToList();

            // Preserve current selection
            var currentSelection = _playerSelector.SelectedValue as PlayerSelectorItem;

            _playerSelector.Items.Clear();

            foreach (var player in players)
            {
                var displayName = string.IsNullOrEmpty(player.Nickname) ? $"Player {player.Uid}" : player.Nickname;
                var item = new PlayerSelectorItem { Uid = player.Uid, DisplayName = displayName };
                _playerSelector.Items.Add(item);

                // Restore selection or default to the first entry
                if ((currentSelection != null && currentSelection.Uid == player.Uid) ||
                    (currentSelection == null && _playerSelector.Items.Count == 1))
                {
                    _playerSelector.SelectedValue = item;
                }
            }
        }

        public void RefreshChartsTheme()
        {
            var isDark = !AppConfig.IsLight;

            // Apply window theme
            FormGui.SetColorMode(this, AppConfig.IsLight);

            // Update chart theming
            if (_dpsTrendChart != null)
                _dpsTrendChart.IsDarkTheme = isDark;

            if (_skillPieChart != null)
                _skillPieChart.IsDarkTheme = isDark;

            if (_teamDpsChart != null)
                _teamDpsChart.IsDarkTheme = isDark;

            if (_multiDimensionChart != null)
                _multiDimensionChart.IsDarkTheme = isDark;

            if (_damageTypeChart != null)
                _damageTypeChart.IsDarkTheme = isDark;
        }

        public void ClearAllChartData()
        {
            _dpsTrendChart?.ClearSeries();
            _skillPieChart?.ClearData();
            _teamDpsChart?.ClearData();
            _multiDimensionChart?.ClearSeries();
            _damageTypeChart?.ClearData();
            _playerSelector?.Items.Clear();
        }

        public void ManualRefreshCharts()
        {
            RefreshAllCharts();
        }

        public void SetAutoRefreshInterval(int milliseconds)
        {
            if (_autoRefreshTimer != null)
            {
                _autoRefreshTimer.Interval = Math.Max(50, milliseconds); // floor at 50 ms
            }
        }

        public bool IsAutoRefreshEnabled => _autoRefreshEnabled;

        public int GetRefreshInterval => _autoRefreshTimer?.Interval ?? 100;

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _autoRefreshTimer?.Stop();
            _autoRefreshTimer?.Dispose();
            base.OnFormClosed(e);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // Trigger an initial refresh once the window is ready
            if (_dpsTrendChart != null)
            {
                RefreshAllCharts();
            }
        }

        private void RealtimeChartsForm_Load(object sender, EventArgs e)
        {

        }
    }

    /// <summary>
    /// Value object for the player dropdown.
    /// </summary>
    public class PlayerSelectorItem
    {
        public long Uid { get; set; }
        public string DisplayName { get; set; } = "";

        public override string ToString()
        {
            return DisplayName;
        }
    }
}