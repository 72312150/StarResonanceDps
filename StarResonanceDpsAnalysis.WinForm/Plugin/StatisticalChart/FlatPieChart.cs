using System.Drawing.Drawing2D;

namespace StarResonanceDpsAnalysis.WinForm.Plugin.Charts
{
    /// <summary>
    /// Flat pie chart control.
    /// </summary>
    public class FlatPieChart : UserControl
    {
        #region Fields and Properties

        private readonly List<PieChartData> _data = new();
        private bool _isDarkTheme = false;
        private string _titleText = "";
        private bool _showLabels = true;
        private bool _showPercentages = true;

        // Modern flat palette
        private readonly Color[] _colors = {
            Color.FromArgb(255, 107, 107),  // red
            Color.FromArgb(78, 205, 196),   // teal
            Color.FromArgb(69, 183, 209),   // blue
            Color.FromArgb(150, 206, 180),  // green
            Color.FromArgb(255, 234, 167),  // yellow
            Color.FromArgb(221, 160, 221),  // purple
            Color.FromArgb(152, 216, 200),  // mint
            Color.FromArgb(247, 220, 111),  // gold
            Color.FromArgb(187, 143, 206),  // lilac
            Color.FromArgb(133, 193, 233)   // sky blue
        };

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                _isDarkTheme = value;
                ApplyTheme();
                Invalidate();
            }
        }

        public string TitleText
        {
            get => _titleText;
            set
            {
                _titleText = value;
                Invalidate();
            }
        }

        public bool ShowLabels
        {
            get => _showLabels;
            set
            {
                _showLabels = value;
                Invalidate();
            }
        }

        public bool ShowPercentages
        {
            get => _showPercentages;
            set
            {
                _showPercentages = value;
                Invalidate();
            }
        }

        #endregion

        #region Constructors

        public FlatPieChart()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw, true);

            ApplyTheme();
        }

        #endregion

        #region Data Management

        public void SetData(List<(string Label, double Value)> data)
        {
            _data.Clear();

            var total = data.Sum(d => d.Value);
            if (total <= 0) return;

            for (int i = 0; i < data.Count; i++)
            {
                var percentage = data[i].Value / total * 100;
                _data.Add(new PieChartData
                {
                    Label = data[i].Label,
                    Value = data[i].Value,
                    Percentage = percentage,
                    Color = _colors[i % _colors.Length]
                });
            }

            Invalidate();
        }

        public void ClearData()
        {
            _data.Clear();
            Invalidate();
        }

        #endregion

        #region Theming

        private void ApplyTheme()
        {
            if (_isDarkTheme)
            {
                BackColor = Color.FromArgb(31, 31, 31);
                ForeColor = Color.White;
            }
            else
            {
                BackColor = Color.White;
                ForeColor = Color.Black;
            }
        }

        #endregion

        #region Rendering

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Clear background
            g.Clear(BackColor);

            if (_data.Count == 0)
            {
                DrawNoDataMessage(g);
                return;
            }

            // Title
            DrawTitle(g);

            // Chart rectangle
            var titleHeight = string.IsNullOrEmpty(_titleText) ? 0 : 30;
            var margin = 10;
            var pieSize = Math.Min(Width - margin * 2, Height - titleHeight - margin * 2);
            var pieRect = new Rectangle(
                (Width - pieSize) / 2,
                titleHeight + (Height - titleHeight - pieSize) / 2,
                pieSize,
                pieSize
            );

            // Slices
            DrawPieSlices(g, pieRect);

            // Labels
            if (_showLabels)
            {
                DrawLabels(g, pieRect);
            }
        }

        private void DrawNoDataMessage(Graphics g)
        {
            var message = "No data available";
            var font = new Font("Microsoft YaHei", 12, FontStyle.Regular);
            var brush = new SolidBrush(_isDarkTheme ? Color.Gray : Color.DarkGray);

            var size = g.MeasureString(message, font);
            var x = (Width - size.Width) / 2;
            var y = (Height - size.Height) / 2;

            g.DrawString(message, font, brush, x, y);

            font.Dispose();
            brush.Dispose();
        }

        private void DrawTitle(Graphics g)
        {
            if (string.IsNullOrEmpty(_titleText)) return;

            using var font = new Font("Microsoft YaHei", 14, FontStyle.Bold);
            using var brush = new SolidBrush(ForeColor);

            var size = g.MeasureString(_titleText, font);
            var x = (Width - size.Width) / 2;
            var y = 10;

            g.DrawString(_titleText, font, brush, x, y);
        }

        private void DrawPieSlices(Graphics g, Rectangle pieRect)
        {
            float startAngle = 0;

            foreach (var data in _data)
            {
                var sweepAngle = (float)(data.Percentage * 360 / 100);

                // Flat slice (no border)
                using var brush = new SolidBrush(data.Color);
                g.FillPie(brush, pieRect, startAngle, sweepAngle);

                startAngle += sweepAngle;
            }
        }

        private void DrawLabels(Graphics g, Rectangle pieRect)
        {
            // Compact font for dense UI
            using var font = new Font("Microsoft YaHei", 7, FontStyle.Regular);
            using var brush = new SolidBrush(ForeColor);

            float startAngle = 0;
            var centerX = pieRect.X + pieRect.Width / 2f;
            var centerY = pieRect.Y + pieRect.Height / 2f;
            var radius = pieRect.Width / 2f;

            foreach (var data in _data)
            {
                var sweepAngle = (float)(data.Percentage * 360 / 100);
                var labelAngle = startAngle + sweepAngle / 2;

                // Place label closer to the center
                var labelRadius = radius * 0.75f;
                var labelX = centerX + labelRadius * (float)Math.Cos(labelAngle * Math.PI / 180);
                var labelY = centerY + labelRadius * (float)Math.Sin(labelAngle * Math.PI / 180);

                // Compose label text
                var labelText = "";
                if (_showLabels && _showPercentages && data.Percentage >= 5.0)
                {
                    // Trim long names
                    var skillName = data.Label.Length > 6 ? data.Label.Substring(0, 6) + ".." : data.Label;
                    labelText = $"{skillName}\n{data.Percentage:F1}%";
                }
                else if (_showPercentages && data.Percentage >= 3.0)
                {
                    labelText = $"{data.Percentage:F1}%";
                }

                if (!string.IsNullOrEmpty(labelText))
                {
                    var textSize = g.MeasureString(labelText, font);
                    var textX = labelX - textSize.Width / 2;
                    var textY = labelY - textSize.Height / 2;

                    // Lightweight translucent background
                    var bgColor = _isDarkTheme ? Color.FromArgb(150, 0, 0, 0) : Color.FromArgb(150, 255, 255, 255);
                    using var bgBrush = new SolidBrush(bgColor);
                    g.FillRectangle(bgBrush, textX - 1, textY - 1, textSize.Width + 2, textSize.Height + 2);

                    // Label text
                    g.DrawString(labelText, font, brush, textX, textY);
                }

                startAngle += sweepAngle;
            }
        }

        #endregion
    }

    /// <summary>
    /// Pie chart entry metadata.
    /// </summary>
    public class PieChartData
    {
        public string Label { get; set; } = "";
        public double Value { get; set; }
        public double Percentage { get; set; }
        public Color Color { get; set; }
    }
}