using System.Drawing.Drawing2D;

namespace StarResonanceDpsAnalysis.WinForm.Plugin.Charts
{
    /// <summary>
    /// Flat bar chart control.
    /// </summary>
    public class FlatBarChart : UserControl
    {
        #region Fields and Properties

        private readonly List<BarChartData> _data = new();
        private bool _isDarkTheme = false;
        private string _titleText = "";
        private string _xAxisLabel = "";
        private string _yAxisLabel = "";
        private bool _showLegend = true;

        // Compact padding to maximize chart space
        private const int PaddingLeft = 35;
        private const int PaddingRight = 15;
        private const int PaddingTop = 25;
        private const int PaddingBottom = 50;

        // Modern palette
        private readonly Color[] _colors = {
            Color.FromArgb(74, 144, 226),   // blue
            Color.FromArgb(126, 211, 33),   // green
            Color.FromArgb(245, 166, 35),   // orange
            Color.FromArgb(208, 2, 27),     // red
            Color.FromArgb(144, 19, 254),   // purple
            Color.FromArgb(80, 227, 194),   // teal
            Color.FromArgb(184, 233, 134),  // light green
            Color.FromArgb(75, 213, 238),   // sky blue
            Color.FromArgb(248, 231, 28),   // yellow
            Color.FromArgb(189, 16, 224)    // magenta
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

        public string XAxisLabel
        {
            get => _xAxisLabel;
            set
            {
                _xAxisLabel = value;
                Invalidate();
            }
        }

        public string YAxisLabel
        {
            get => _yAxisLabel;
            set
            {
                _yAxisLabel = value;
                Invalidate();
            }
        }

        public bool ShowLegend
        {
            get => _showLegend;
            set
            {
                _showLegend = value;
                Invalidate();
            }
        }

        #endregion

        #region Constructors

        public FlatBarChart()
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

            for (int i = 0; i < data.Count; i++)
            {
                _data.Add(new BarChartData
                {
                    Label = data[i].Label,
                    Value = data[i].Value,
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

            // Determine max value
            var maxValue = _data.Max(d => d.Value);
            if (maxValue <= 0) return;

            // Compute chart bounds
            var chartRect = new Rectangle(PaddingLeft, PaddingTop,
                                        Width - PaddingLeft - PaddingRight,
                                        Height - PaddingTop - PaddingBottom);

            // Draw grid
            DrawGrid(g, chartRect, maxValue);

            // Draw axes
            DrawAxes(g, chartRect, maxValue);

            // Draw bar series
            DrawBars(g, chartRect, maxValue);

            // Draw title
            DrawTitle(g);
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

        private void DrawGrid(Graphics g, Rectangle chartRect, double maxValue)
        {
            var gridColor = _isDarkTheme ? Color.FromArgb(64, 64, 64) : Color.FromArgb(230, 230, 230);
            using var gridPen = new Pen(gridColor, 1);

            // Horizontal gridlines at 20% intervals
            for (int i = 0; i <= 5; i++)
            {
                var y = chartRect.Y + (float)chartRect.Height * i / 5;
                g.DrawLine(gridPen, chartRect.X, y, chartRect.Right, y);
            }
        }

        private void DrawAxes(Graphics g, Rectangle chartRect, double maxValue)
        {
            var axisColor = _isDarkTheme ? Color.FromArgb(128, 128, 128) : Color.FromArgb(180, 180, 180);
            using var axisPen = new Pen(axisColor, 1);
            using var textBrush = new SolidBrush(ForeColor);
            using var font = new Font("Microsoft YaHei", 7);

            // X axis
            g.DrawLine(axisPen, chartRect.X, chartRect.Bottom, chartRect.Right, chartRect.Bottom);

            // Y axis
            g.DrawLine(axisPen, chartRect.X, chartRect.Y, chartRect.X, chartRect.Bottom);

            // Category labels
            var barWidth = (float)chartRect.Width / _data.Count;
            for (int i = 0; i < _data.Count; i++)
            {
                var x = chartRect.X + barWidth * (i + 0.5f);
                var text = _data[i].Label;

                var size = g.MeasureString(text, font);

                // Render horizontally
                var textX = x - size.Width / 2;
                var textY = chartRect.Bottom + 5;

                g.DrawString(text, font, textBrush, textX, textY);
            }

            // Y-axis ticks
            for (int i = 0; i <= 5; i++)
            {
                var y = chartRect.Bottom - (float)chartRect.Height * i / 5;
                var value = maxValue * i / 5;
                var text = $"{value:F0}%";

                var size = g.MeasureString(text, font);
                g.DrawString(text, font, textBrush, chartRect.X - size.Width - 3, y - size.Height / 2);
            }

            // Axis labels
            if (!string.IsNullOrEmpty(_xAxisLabel))
            {
                var size = g.MeasureString(_xAxisLabel, font);
                var x = chartRect.X + (chartRect.Width - size.Width) / 2;
                var y = chartRect.Bottom + 35;
                g.DrawString(_xAxisLabel, font, textBrush, x, y);
            }

            if (!string.IsNullOrEmpty(_yAxisLabel))
            {
                var size = g.MeasureString(_yAxisLabel, font);
                using var matrix = new Matrix();
                matrix.RotateAt(-90, new PointF(10, chartRect.Y + (chartRect.Height + size.Width) / 2));
                g.Transform = matrix;
                g.DrawString(_yAxisLabel, font, textBrush, 10, chartRect.Y + (chartRect.Height + size.Width) / 2);
                g.ResetTransform();
            }
        }

        private void DrawBars(Graphics g, Rectangle chartRect, double maxValue)
        {
            var barWidth = (float)chartRect.Width / _data.Count * 0.85f;
            var barSpacing = (float)chartRect.Width / _data.Count * 0.075f;

            for (int i = 0; i < _data.Count; i++)
            {
                var data = _data[i];
                var barHeight = (float)(data.Value / maxValue * chartRect.Height);

                var x = chartRect.X + i * (barWidth + barSpacing * 2) + barSpacing;
                var y = chartRect.Bottom - barHeight;

                var barRect = new RectangleF(x, y, barWidth, barHeight);

                // Flat fill without border
                using var brush = new SolidBrush(data.Color);
                g.FillRectangle(brush, barRect);

                // Draw value label when the bar is tall enough
                if (barHeight > 15)
                {
                    var valueText = $"{data.Value:F1}%";
                    using var font = new Font("Microsoft YaHei", 6, FontStyle.Regular);
                    using var textBrush = new SolidBrush(ForeColor);

                    var textSize = g.MeasureString(valueText, font);
                    var textX = x + (barWidth - textSize.Width) / 2;

                    var textAboveY = y - textSize.Height - 2;
                    var textInsideY = y + 2;

                    // Prefer above-bar placement when space allows
                    var textY = (textAboveY >= chartRect.Y) ? textAboveY : textInsideY;

                    // Keep label within chart bounds
                    if (textY + textSize.Height <= chartRect.Bottom && textY >= chartRect.Y)
                    {
                        // Choose contrasting color when drawn inside the bar
                        Color textColor = ForeColor;
                        if (textY == textInsideY)
                        {
                            textColor = GetContrastColor(data.Color);
                        }

                        using var contrastBrush = new SolidBrush(textColor);
                        g.DrawString(valueText, font, contrastBrush, textX, textY);
                    }
                }
            }
        }

        /// <summary>
        /// Compute a contrasting text color for the given background.
        /// </summary>
        private Color GetContrastColor(Color backgroundColor)
        {
            // Calculate perceived brightness
            var brightness = (backgroundColor.R * 0.299 + backgroundColor.G * 0.587 + backgroundColor.B * 0.114);

            // Return black or white depending on brightness
            return brightness > 128 ? Color.Black : Color.White;
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

        #endregion
    }

    /// <summary>
    /// Bar chart data item.
    /// </summary>
    public class BarChartData
    {
        public string Label { get; set; } = "";
        public double Value { get; set; }
        public Color Color { get; set; }
    }
}