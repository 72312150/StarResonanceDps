using AntdUI;
using StarResonanceDpsAnalysis.WinForm.Plugin;

namespace StarResonanceDpsAnalysis.WinForm.Control
{
    public partial class DataDisplaySettings : UserControl
    {
        private readonly BorderlessForm _parentForm;
        private System.Windows.Forms.Timer? _refreshDelayTimer;
        private bool _isUpdatingCheckboxes = false; // Prevent recursive updates

        public DataDisplaySettings(BorderlessForm borderlessForm)
        {
            InitializeComponent();
            _parentForm = borderlessForm;

            // Initialize the deferred refresh timer – higher delay to reduce stutter
            _refreshDelayTimer = new System.Windows.Forms.Timer
            {
                Interval = 500 // Raise delay to 500 ms to further reduce frequent refreshes
            };
            _refreshDelayTimer.Tick += RefreshDelayTimer_Tick;
        }

        private void DataDisplaySettings_Load(object sender, EventArgs e)
        {
            // Improve the FlowPanel rendering performance
            OptimizeFlowPanelDisplay();

            InitializeOptimizedLayout();
        }

        /// <summary>
        /// Optimizes FlowPanel rendering performance to reduce scrolling artifacts
        /// </summary>
        private void OptimizeFlowPanelDisplay()
        {
            try
            {
                // Enable double buffering to reduce flicker
                typeof(System.Windows.Forms.Panel).InvokeMember("DoubleBuffered",
                    System.Reflection.BindingFlags.SetProperty |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic,
                    null, flowPanel1, new object[] { true });

                // Configure additional rendering optimizations via reflection
                var setStyleMethod = typeof(System.Windows.Forms.Control).GetMethod("SetStyle",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (setStyleMethod != null)
                {
                    setStyleMethod.Invoke(flowPanel1, new object[] { ControlStyles.OptimizedDoubleBuffer, true });
                    setStyleMethod.Invoke(flowPanel1, new object[] { ControlStyles.AllPaintingInWmPaint, true });
                    setStyleMethod.Invoke(flowPanel1, new object[] { ControlStyles.UserPaint, true });
                    setStyleMethod.Invoke(flowPanel1, new object[] { ControlStyles.ResizeRedraw, true });
                }

                Console.WriteLine("FlowPanel optimization enabled.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FlowPanel optimization failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Initializes the optimized layout
        /// </summary>
        private void InitializeOptimizedLayout()
        {
            // Pause layout updates to improve performance
            flowPanel1.SuspendLayout();

            try
            {
                // Remove existing controls
                flowPanel1.Controls.Clear();

                // Configure FlowPanel base properties
                flowPanel1.AutoScroll = true;
                flowPanel1.Padding = new Padding(10, 10, 10, 10);

                // Step 1: place the action buttons at the top
                AddControlButtons();

                // Step 2: redefine the grouping data
                var groups = new Dictionary<string, string[]>
                {
                    { "⚔️ Damage Overview", new[] { "TotalDamage", "CriticalDamage", "LuckyDamage", "CritLuckyDamage", "DamageTaken" } },
                    { "💥 DPS Metrics", new[] { "InstantDps", "MaxInstantDps", "TotalDps", "CritRate", "LuckyRate" } },
                    { "🛡️ Healing Metrics", new[] { "TotalHealingDone", "CriticalHealingDone", "LuckyHealingDone", "CritLuckyHealingDone" } },
                    { "💚 HPS Metrics", new[] { "InstantHps", "MaxInstantHps", "TotalHps" } }
                };

                // Step 3: create the two-column layout container
                CreateTwoColumnLayout(groups);

                // Diagnostics
                Console.WriteLine("=== Layout initialization complete ===");
                for (int i = 0; i < flowPanel1.Controls.Count; i++)
                {
                    var control = flowPanel1.Controls[i];
                    Console.WriteLine($"Control {i}: {control.GetType().Name} - Height: {control.Height}");
                }
            }
            finally
            {
                // Resume layout updates
                flowPanel1.ResumeLayout(true);

                // Force a refresh
                flowPanel1.PerformLayout();
                flowPanel1.Refresh();
            }
        }

        /// <summary>
        /// Creates the two-column layout
        /// </summary>
        private void CreateTwoColumnLayout(Dictionary<string, string[]> groups)
        {
            var groupList = groups.ToList();
            int groupsPerColumn = (int)Math.Ceiling(groupList.Count / 2.0);

            // Root panel hosting the two columns
            var mainContainer = new System.Windows.Forms.Panel
            {
                Width = flowPanel1.ClientSize.Width - 20,
                AutoSize = true,
                Margin = new Padding(0, 5, 0, 5),
                BackColor = Color.Transparent
            };

            // Left column
            var leftColumn = new System.Windows.Forms.Panel
            {
                Width = (mainContainer.Width - 20) / 2,
                Location = new Point(0, 0),
                AutoSize = true,
                BackColor = Color.Transparent
            };

            // Right column
            var rightColumn = new System.Windows.Forms.Panel
            {
                Width = (mainContainer.Width - 20) / 2,
                Location = new Point((mainContainer.Width - 20) / 2 + 10, 0),
                AutoSize = true,
                BackColor = Color.Transparent
            };

            int currentY_Left = 0;
            int currentY_Right = 0;

            // Distribute groups between the two columns
            for (int i = 0; i < groupList.Count; i++)
            {
                var group = groupList[i];
                var groupPanel = CreateCompactGroupPanel(group.Key, group.Value, leftColumn.Width - 10);

                if (i < groupsPerColumn)
                {
                    // Add to the left column
                    groupPanel.Location = new Point(0, currentY_Left);
                    leftColumn.Controls.Add(groupPanel);
                    currentY_Left += groupPanel.Height + 10;
                }
                else
                {
                    // Add to the right column
                    groupPanel.Location = new Point(0, currentY_Right);
                    rightColumn.Controls.Add(groupPanel);
                    currentY_Right += groupPanel.Height + 10;
                }
            }

            // Apply final heights for each column
            leftColumn.Height = currentY_Left;
            rightColumn.Height = currentY_Right;

            // Use the taller column to size the container
            mainContainer.Height = Math.Max(currentY_Left, currentY_Right);

            mainContainer.Controls.AddRange(new System.Windows.Forms.Control[] { leftColumn, rightColumn });
            flowPanel1.Controls.Add(mainContainer);
        }

        /// <summary>
        /// Creates a compact group panel – separators removed to reduce hitching
        /// </summary>
        private System.Windows.Forms.Panel CreateCompactGroupPanel(string groupTitle, string[] itemKeys, int panelWidth)
        {
            var groupContainer = new System.Windows.Forms.Panel
            {
                Width = panelWidth,
                AutoSize = true,
                BackColor = Color.Transparent,
                BorderStyle = BorderStyle.None,
                Margin = new Padding(0, 0, 0, 15) // Add bottom spacing instead of separators
            };

            // Enable double buffering to improve scrolling
            try
            {
                typeof(System.Windows.Forms.Panel).InvokeMember("DoubleBuffered",
                    System.Reflection.BindingFlags.SetProperty |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic,
                    null, groupContainer, new object[] { true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to enable double buffering for group panel: {ex.Message}");
            }

            int currentY = 0;

            // Create group header with tuned visuals
            var titleLabel = new System.Windows.Forms.Label
            {
                Text = groupTitle,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                ForeColor = AppConfig.IsLight ? Color.FromArgb(51, 51, 51) : Color.FromArgb(220, 220, 220),
                AutoSize = true,
                Location = new Point(0, currentY),
                BackColor = Color.Transparent,
                UseMnemonic = false, // Improve text rendering
                UseCompatibleTextRendering = false // Use modern text rendering
            };
            groupContainer.Controls.Add(titleLabel);
            currentY += titleLabel.Height + 6;

            // Build the option area with a tighter layout
            var optionsPanel = CreateCompactOptionsGrid(itemKeys, panelWidth - 15);
            optionsPanel.Location = new Point(15, currentY);
            groupContainer.Controls.Add(optionsPanel);
            currentY += optionsPanel.Height + 8;

            // Separators were removed because they caused stuttering; use spacing instead

            groupContainer.Height = currentY;
            return groupContainer;
        }

        /// <summary>
        /// Creates a compact options grid layout – optimized for smooth scrolling
        /// </summary>
        private System.Windows.Forms.Panel CreateCompactOptionsGrid(string[] itemKeys, int panelWidth)
        {
            var panel = new System.Windows.Forms.Panel
            {
                Width = panelWidth,
                AutoSize = true,
                BackColor = Color.Transparent
            };

            // Enable double buffering via reflection to minimize flicker while scrolling
            try
            {
                typeof(System.Windows.Forms.Panel).InvokeMember("DoubleBuffered",
                    System.Reflection.BindingFlags.SetProperty |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic,
                    null, panel, new object[] { true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to enable double buffering: {ex.Message}");
            }

            // For narrow columns prefer a single column layout to keep text readable
            int columnCount = 1; // One option per column for clarity
            int columnWidth = panel.Width;
            int rowHeight = 28; // Slightly reduced row height
            int currentRow = 0;

            foreach (var key in itemKeys)
            {
                var setting = ColumnSettingsManager.AllSettings.FirstOrDefault(x => x.Key == key);
                if (setting == null) continue;

                // Create the checkbox with tuned visuals
                var checkbox = new AntdUI.Checkbox
                {
                    Text = setting.Title,
                    Name = setting.Key,
                    Checked = setting.IsVisible,
                    Tag = setting.Key,
                    Size = new Size(columnWidth - 5, 24),
                    Location = new Point(0, currentRow * rowHeight),
                    Font = new Font("Microsoft YaHei UI", 8.5F),
                    BackColor = Color.Transparent
                };

                checkbox.CheckedChanged += checkbox_CheckedChanged;
                panel.Controls.Add(checkbox);
                currentRow++;
            }

            // 计算并设置面板高度
            panel.Height = Math.Max(rowHeight, currentRow * rowHeight);
            return panel;
        }

        /// <summary>
        /// Adds the control buttons at the top
        /// </summary>
        private void AddControlButtons()
        {
            var buttonContainer = new System.Windows.Forms.Panel
            {
                Width = flowPanel1.ClientSize.Width - 30,
                Height = 45,
                Margin = new Padding(0, 0, 0, 15), // Bottom spacing to separate from following content
                BackColor = Color.Transparent
            };

            // Select all button
            var selectAllBtn = new AntdUI.Button
            {
                Text = "Select All",
                Size = new Size(70, 32),
                Location = new Point(0, 6),
                Type = TTypeMini.Primary,
                BorderWidth = 1
            };
            selectAllBtn.Click += (s, e) => SetAllCheckboxes(true);

            // Deselect all button
            var deselectAllBtn = new AntdUI.Button
            {
                Text = "Deselect All",
                Size = new Size(70, 32),
                Location = new Point(80, 6),
                Type = TTypeMini.Default,
                BorderWidth = 1
            };
            deselectAllBtn.Click += (s, e) => SetAllCheckboxes(false);

            // Restore defaults button
            var defaultBtn = new AntdUI.Button
            {
                Text = "Default",
                Size = new Size(70, 32),
                Location = new Point(160, 6),
                Type = TTypeMini.Warn,
                BorderWidth = 1
            };
            defaultBtn.Click += (s, e) => ResetToDefaults();

            buttonContainer.Controls.AddRange(new System.Windows.Forms.Control[] { selectAllBtn, deselectAllBtn, defaultBtn });
            flowPanel1.Controls.Add(buttonContainer);
        }

        /// <summary>
        /// Handles checkbox state changes – optimized to avoid stutter
        /// </summary>
        private void checkbox_CheckedChanged(object sender, BoolEventArgs e)
        {
            // Guard against recursive updates causing performance issues
            if (_isUpdatingCheckboxes) return;

            try
            {
                if (sender is Checkbox cb && cb.Tag is string key)
                {
                    var setting = ColumnSettingsManager.AllSettings.FirstOrDefault(x => x.Key == key);
                    if (setting != null)
                    {
                        // Update the in-memory setting immediately but delay the UI refresh
                        setting.IsVisible = cb.Checked;

                        // Persist asynchronously to avoid blocking the UI thread
                        Task.Run(() =>
                        {
                            try
                            {
                                AppConfig.SetValue("TableSet", cb.Name, cb.Checked.ToString());
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Failed to persist visibility setting asynchronously: {ex.Message}");
                            }
                        });
                    }

                    // Use the deferred refresh to avoid excessive updates
                    if (_refreshDelayTimer != null)
                    {
                        _refreshDelayTimer.Stop();
                        _refreshDelayTimer.Start();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while handling checkbox change: {ex.Message}");
            }
        }

        /// <summary>
        /// Deferred refresh timer callback
        /// </summary>
        private void RefreshDelayTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                _refreshDelayTimer?.Stop();

                // Invoke the refresh on the UI thread
                this.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        ColumnSettingsManager.RefreshTableAction?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Table refresh failed: {ex.Message}");
                    }
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Deferred refresh failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets all checkboxes – optimized for bulk operations
        /// </summary>
        private void SetAllCheckboxes(bool isChecked)
        {
            try
            {
                _isUpdatingCheckboxes = true; // Start bulk update to suppress per-item events

                // Stop the timer to avoid refreshing in intermediate states
                _refreshDelayTimer?.Stop();

                TraverseAndSetCheckboxes(flowPanel1, isChecked);

                // Trigger a refresh after the batch operation
                _refreshDelayTimer?.Start();
            }
            finally
            {
                _isUpdatingCheckboxes = false; // Resume normal event handling
            }
        }

        /// <summary>
        /// Recursively sets checkbox states – optimized for performance
        /// </summary>
        private void TraverseAndSetCheckboxes(System.Windows.Forms.Control parent, bool isChecked)
        {
            foreach (System.Windows.Forms.Control control in parent.Controls)
            {
                if (control is Checkbox checkbox)
                {
                    // During bulk updates set directly without triggering handlers
                    checkbox.Checked = isChecked;

                    // Update the settings entry directly
                    if (checkbox.Tag is string key)
                    {
                        var setting = ColumnSettingsManager.AllSettings.FirstOrDefault(x => x.Key == key);
                        if (setting != null)
                        {
                            setting.IsVisible = isChecked;
                            // Persist asynchronously
                            Task.Run(() => AppConfig.SetValue("TableSet", checkbox.Name, isChecked.ToString()));
                        }
                    }
                }
                else if (control.HasChildren)
                {
                    TraverseAndSetCheckboxes(control, isChecked);
                }
            }
        }

        /// <summary>
        /// Resets all columns to their default visibility – optimized for batch work
        /// </summary>
        private void ResetToDefaults()
        {
            try
            {
                _isUpdatingCheckboxes = true; // Begin bulk update
                _refreshDelayTimer?.Stop(); // Stop the timer

                // Define the default set of important columns
                var defaultColumns = new HashSet<string>
                {
                    // Damage metrics (highest priority)
                    "TotalDamage",      // Total damage
                    "DamageTaken",      // Damage taken
                    "CriticalDamage",   // Critical damage

                    // DPS metrics
                    "TotalDps",         // DPS
                    "CritRate",         // Critical rate
                    "LuckyRate",        // Lucky rate

                    // Healing metrics
                    "TotalHealingDone", // Total healing

                    // HPS metrics
                    "TotalHps"          // HPS
                };

                TraverseAndResetCheckboxes(flowPanel1, defaultColumns);

                // Trigger a refresh once batching completes
                _refreshDelayTimer?.Start();
            }
            finally
            {
                _isUpdatingCheckboxes = false; // Restore normal handling
            }
        }

        /// <summary>
        /// Recursively resets checkboxes to their default state – optimized for performance
        /// </summary>
        private void TraverseAndResetCheckboxes(System.Windows.Forms.Control parent, HashSet<string> defaultColumns)
        {
            foreach (System.Windows.Forms.Control control in parent.Controls)
            {
                if (control is Checkbox checkbox && checkbox.Tag is string key)
                {
                    bool shouldBeChecked = defaultColumns.Contains(key);
                    checkbox.Checked = shouldBeChecked;

                    // 直接更新设置
                    var setting = ColumnSettingsManager.AllSettings.FirstOrDefault(x => x.Key == key);
                    if (setting != null)
                    {
                        setting.IsVisible = shouldBeChecked;
                        // 异步保存
                        Task.Run(() => AppConfig.SetValue("TableSet", checkbox.Name, shouldBeChecked.ToString()));
                    }
                }
                else if (control.HasChildren)
                {
                    TraverseAndResetCheckboxes(control, defaultColumns);
                }
            }
        }

        private void flowPanel1_Click(object sender, EventArgs e)
        {
            // 保留原有的点击事件处理
        }

        /// <summary>
        /// 清理延迟刷新定时器资源
        /// </summary>
        public void CleanupResources()
        {
            try
            {
                // 释放延迟刷新定时器
                _refreshDelayTimer?.Stop();
                _refreshDelayTimer?.Dispose();
                _refreshDelayTimer = null;
                Console.WriteLine("Data display settings resources released.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to clean resources: {ex.Message}");
            }
        }
    }
}
