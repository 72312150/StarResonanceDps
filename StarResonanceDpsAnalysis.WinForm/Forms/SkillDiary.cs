using System;
using System.Text.RegularExpressions;

using AntdUI;
using StarResonanceDpsAnalysis.WinForm.Core;
using StarResonanceDpsAnalysis.WinForm.Forms;
using StarResonanceDpsAnalysis.WinForm.Plugin;

namespace StarResonanceDpsAnalysis.WinForm.Forms
{
    public partial class SkillDiary : BorderlessForm
    {
        public SkillDiary()
        {
            InitializeComponent();
            FormGui.SetDefaultGUI(this);
            TitleText.Font = AppConfig.SaoFont;
            label10.Font = AppConfig.ContentFont;
            richTextBox1.Font = AppConfig.ContentFont;

        }




        public void AppendDiaryLine(string line)
        {
            if (richTextBox1.InvokeRequired)
            {
                richTextBox1.BeginInvoke(new Action<string>(AppendDiaryLine), line);
                return;
            }

            // ---- Theme palette (adjust as needed) ----
            Color colorTime = Color.FromArgb(140, 140, 140);
            Color colorSep = Color.FromArgb(170, 170, 170);
            Color colorName = Color.FromArgb(30, 30, 30);
            Color colorDmg = Color.IndianRed;
            Color colorHeal = Color.SeaGreen;
            Color colorCount = Color.DimGray;
            Color badgeCritBack = Color.FromArgb(255, 236, 204); // Soft orange background
            Color badgeCritFore = Color.FromArgb(178, 99, 0);
            Color badgeLuckyBack = Color.FromArgb(234, 223, 255); // Soft purple background
            Color badgeLuckyFore = Color.FromArgb(84, 46, 158);

            // Helper: write plain text
            void Write(string text, Color? color = null, FontStyle style = FontStyle.Regular)
            {
                richTextBox1.SelectionStart = richTextBox1.TextLength;
                richTextBox1.SelectionLength = 0;

                richTextBox1.SelectionColor = color ?? richTextBox1.ForeColor;
                richTextBox1.SelectionFont = new Font(richTextBox1.Font, style);
                // Clear background to avoid inheriting badge backgrounds
                if (HasSelectionBackColor()) richTextBox1.SelectionBackColor = Color.Transparent;

                richTextBox1.AppendText(text);

                // Restore selection formatting
                richTextBox1.SelectionColor = richTextBox1.ForeColor;
                richTextBox1.SelectionFont = richTextBox1.Font;
                if (HasSelectionBackColor()) richTextBox1.SelectionBackColor = Color.Transparent;
            }

            // Helper: capsule badge (background color + surrounding spaces)
            void Badge(string text, Color back, Color fore, bool bold = false)
            {
                richTextBox1.SelectionStart = richTextBox1.TextLength;
                richTextBox1.SelectionLength = 0;

                if (HasSelectionBackColor()) richTextBox1.SelectionBackColor = back;
                richTextBox1.SelectionColor = fore;
                richTextBox1.SelectionFont = new Font(richTextBox1.Font, bold ? FontStyle.Bold : FontStyle.Regular);

                // Add spaces on both sides to make the badge look balanced
                richTextBox1.AppendText(" " + text + " ");

                // Restore selection formatting
                richTextBox1.SelectionColor = richTextBox1.ForeColor;
                richTextBox1.SelectionFont = richTextBox1.Font;
                if (HasSelectionBackColor()) richTextBox1.SelectionBackColor = Color.Transparent;
            }

            // Detect whether SelectionBackColor is supported (older frameworks might not)
            bool HasSelectionBackColor()
            {
                try
                {
                    var _ = richTextBox1.SelectionBackColor;
                    return true;
                }
                catch { return false; }
            }

            // --------- Parse [duration] prefix ----------
            var m = Regex.Match(line, @"^\[(?<dur>[^\]]+)\]\s*(?<rest>.*)$");
            if (m.Success)
            {
                // Time segment in brackets displayed in light gray
                Write("[", colorTime);
                Write(m.Groups["dur"].Value, colorTime);
                Write("] ", colorTime);

                line = m.Groups["rest"].Value; // Remaining content
            }

            // --------- Split parts using " | " (aligned with existing output format) ----------
            var parts = line.Split(new[] { " | " }, StringSplitOptions.None);

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();

                // The first segment is typically the skill name
                if (i == 0)
                {
                    // Render skill names bold with darker color
                    Write(part, colorName, FontStyle.Bold);
                }
                else
                {
                    // Match "伤害:12345" or "治疗:54321"
                    var kv = Regex.Match(part, @"^(?<k>伤害|治疗)\s*:\s*(?<v>\d+)$");
                    if (kv.Success)
                    {
                        string k = kv.Groups["k"].Value;
                        string v = kv.Groups["v"].Value; // Preserve full number (no K/M formatting)

                        if (k == "伤害")
                            Write($"Damage:{v}", colorDmg, FontStyle.Regular);
                        else
                            Write($"Healing:{v}", colorHeal, FontStyle.Regular);
                    }
                    else if (part.StartsWith("释放次数:") || part.StartsWith("次数:"))
                    {
                        var normalized = part
                            .Replace("释放次数:", "Casts:")
                            .Replace("次数:", "Casts:");
                        Write(normalized, colorCount, FontStyle.Regular);
                    }
                    else if (part.StartsWith("暴击"))
                    {
                        // Supports "暴击" or "暴击:3"
                        var n = Regex.Match(part, @"^暴击(?::\s*(?<n>\d+))?$");
                        if (n.Success)
                        {
                            string label = n.Groups["n"].Success ? $"Critical ×{n.Groups["n"].Value}" : "Critical";
                            Badge(label, badgeCritBack, badgeCritFore, bold: true);
                        }
                        else
                        {
                            Badge("Critical", badgeCritBack, badgeCritFore, bold: true);
                        }
                    }
                    else if (part.StartsWith("幸运"))
                    {
                        var n = Regex.Match(part, @"^幸运(?::\s*(?<n>\d+))?$");
                        if (n.Success)
                        {
                            string label = n.Groups["n"].Success ? $"Lucky ×{n.Groups["n"].Value}" : "Lucky";
                            Badge(label, badgeLuckyBack, badgeLuckyFore, bold: true);
                        }
                        else
                        {
                            Badge("Lucky", badgeLuckyBack, badgeLuckyFore, bold: true);
                        }
                    }
                    else
                    {
                        // Any other segments keep the default styling
                        Write(part);
                    }
                }

                // Separator between segments (skip after the last one)
                if (i < parts.Length - 1) Write("  |  ", colorSep);
            }

            // Append newline and scroll to bottom
            richTextBox1.AppendText(Environment.NewLine);
            richTextBox1.SelectionStart = richTextBox1.Text.Length;
            richTextBox1.ScrollToCaret();
        }



        private void SkillDiary_Load(object sender, EventArgs e)
        {
            FormGui.SetColorMode(this, AppConfig.IsLight); // Apply current theme
        }

        private void button3_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void button4_Click(object sender, EventArgs e)
        {
            richTextBox1.Text = string.Empty;
            SkillDiaryGate.Reset();
        }

        private void TitleText_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                FormManager.ReleaseCapture();
                FormManager.SendMessage(this.Handle, FormManager.WM_NCLBUTTONDOWN, FormManager.HTCAPTION, 0);
            }
        }

        private void SkillDiary_ForeColorChanged(object sender, EventArgs e)
        {

        }
    }
}
