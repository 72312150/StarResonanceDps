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
using StarResonanceDpsAnalysis.Core.Extends.System;
using StarResonanceDpsAnalysis.WinForm.Core;
using StarResonanceDpsAnalysis.WinForm.Core.Module;
using StarResonanceDpsAnalysis.WinForm.Forms.PopUp;
using StarResonanceDpsAnalysis.WinForm.Plugin;

using static StarResonanceDpsAnalysis.WinForm.Core.Module.ModuleCardDisplay;

namespace StarResonanceDpsAnalysis.WinForm.Forms.ModuleForm
{
    public partial class ModuleCalculationForm : BorderlessForm
    {

        public ModuleCalculationForm()
        {
            InitializeComponent();
            FormGui.SetDefaultGUI(this);
        }

        private void ModuleCalculationForm_Load(object sender, EventArgs e)
        {
            FormGui.SetColorMode(this, AppConfig.IsLight);// Apply the configured theme colors
            if (Config.IsLight)
            {
                groupBox1.ForeColor = groupBox3.ForeColor = Color.Black;
            }
            else
            {
                groupBox1.ForeColor = groupBox3.ForeColor = Color.White;

            }
            TitleText.Font = AppConfig.SaoFont;
            label1.Font = groupBox1.Font = AppConfig.ContentFont;
            button1.Font = AppConfig.ContentFont;

            List<Select> selects = new List<Select>
{
    select1,
    select2,
    select4,
    select5,
    select6,
    select7,
    select8,
    select9,
    select10,
    select11,
    select12,
    select3
};
            List<InputNumber> inputNumbers = new List<InputNumber>
{
    inputNumber1,
    inputNumber2,
    inputNumber3,
    inputNumber4,
    inputNumber5
};
            foreach (var sel in selects)
            {
                sel.Font = AppConfig.ContentFont;
            }

            foreach (var num in inputNumbers)
            {
                num.Font = AppConfig.ContentFont;
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
            // TODO: Refactor logic

            throw new NotImplementedException();

            //if (MessageAnalyzer.PayloadBuffer.Length == 0)
            //{
            //    var result = AppMessageBox.ShowMessage("""
            //        Please run a stage once before clicking this button
            //        """, this);
            //    return;
            //}


            //BuildEliteCandidatePool.ParseModuleInfo(MessageAnalyzer.PayloadBuffer);



            virtualPanel1.Waterfall = false;      // Row-first layout (set Align to Start if available)
            virtualPanel1.Items.Clear();

            // Build all cards first without adding them to the panel
            var cards = new List<ModuleCardDisplay.ResultCardSimpleItem>();
            foreach (var dto in ModuleCardDisplay.ModuleResultMemory.GetSnapshot())
            {
                cards.Add(new ModuleCardDisplay.ResultCardSimpleItem
                {
                    RankText = dto.RankText,
                    HighestLevel = dto.HighestLevel,
                    Score = dto.Score,        // Currently a string
                    ModuleRows = dto.ModuleRows,
                    AttrLines = dto.AttrLines
                });
            }

            // ========== Step 1: normalize card width ==========
            float dpi = AntdUI.Config.Dpi;
            int gutterPx = (int)MathF.Round(12 * dpi);     // Column gap
            int minCardW = (int)MathF.Round(240 * dpi);    // Minimum card width
            int maxCols = 3;                              // Maximum number of columns
            int panelW = Math.Max(1, virtualPanel1.ClientSize.Width);

            // Match the fonts used inside the cards
            var baseFont = virtualPanel1.Font ?? SystemFonts.DefaultFont;
            using var fontBody = new Font(baseFont, baseFont.Style);
            using var fontTitle = new Font(baseFont, FontStyle.Bold);

            // Compute preferred width: padding plus the longest row (left + gap + right)
            int ComputePreferredCardWidth(ModuleCardDisplay.ResultCardSimpleItem card)
            {
                int padL = (int)MathF.Round(card.ContentPadding.Left * dpi);
                int padR = (int)MathF.Round(card.ContentPadding.Right * dpi);
                int gap = (int)MathF.Round(card.LeftRightGap * dpi);

                int maxRowContentW = 0;
                foreach (var row in card.ModuleRows ?? Enumerable.Empty<(string Left, string Right)>())
                {
                    var left = row.Left ?? string.Empty;
                    var right = $"[{row.Right ?? string.Empty}]";
                    int leftW = TextRenderer.MeasureText(left, fontBody).Width;
                    int rightW = TextRenderer.MeasureText(right, fontBody).Width;
                    maxRowContentW = Math.Max(maxRowContentW, leftW + gap + rightW);
                }
                int preferred = padL + maxRowContentW + padR;
                preferred = Math.Max(preferred, minCardW);
                preferred = Math.Min(preferred, panelW);
                return preferred;
            }

            int preferredMaxW = cards.Count == 0 ? minCardW : cards.Max(ComputePreferredCardWidth);

            // Determine how many columns fit and derive a unified card width
            int cols = Math.Max(1, Math.Min(maxCols, (panelW + gutterPx) / (preferredMaxW + gutterPx)));
            int unifiedCardW = (panelW - gutterPx * (cols - 1)) / cols;
            unifiedCardW = Math.Max(1, Math.Min(unifiedCardW, panelW));

            // Apply the unified width to every card
            foreach (var c in cards) c.ForceCardWidthPx = unifiedCardW;

            // ========== Step 2: normalize card height ==========
            // Measure each card with the unified width and keep the tallest
            int MeasureCardHeight(ModuleCardDisplay.ResultCardSimpleItem card)
            {
                int padL = (int)MathF.Round(card.ContentPadding.Left * dpi);
                int padT = (int)MathF.Round(card.ContentPadding.Top * dpi);
                int padR = (int)MathF.Round(card.ContentPadding.Right * dpi);
                int padB = (int)MathF.Round(card.ContentPadding.Bottom * dpi);
                int gap = (int)MathF.Round(card.LeftRightGap * dpi);

                int contentW = Math.Max(1, unifiedCardW - padL - padR);
                int y = 0;

                // Title
                y += TextRenderer.MeasureText(card.RankText ?? "", fontTitle,
                      new Size(contentW, int.MaxValue), TextFormatFlags.WordBreak).Height + card.LineGap;

                // Highest trait level
                y += TextRenderer.MeasureText($"Highest trait level {card.HighestLevel}", fontBody,
                      new Size(contentW, int.MaxValue), TextFormatFlags.WordBreak).Height + card.LineGap;

                // Overall score (single line measurement is sufficient)
                string scoreText = string.IsNullOrEmpty(card.Score) ? "—" : card.Score;
                y += TextRenderer.MeasureText($"Overall score: {scoreText}", fontBody,
                      new Size(contentW, int.MaxValue), TextFormatFlags.WordBreak).Height + card.SectionGap;

                // List heading
                y += TextRenderer.MeasureText("Module list:", fontBody,
                      new Size(contentW, int.MaxValue), TextFormatFlags.WordBreak).Height + card.LineGap;

                // Each list row (left width = contentW - right natural width - gap; right column doesn't wrap)
                foreach (var row in card.ModuleRows ?? Enumerable.Empty<(string Left, string Right)>())
                {
                    var left = row.Left ?? string.Empty;
                    var right = $"[{row.Right ?? string.Empty}]";
                    int rightW = TextRenderer.MeasureText(right, fontBody).Width;
                    int leftMaxW = Math.Max(1, contentW - rightW - gap);

                    int hL = TextRenderer.MeasureText(left, fontBody,
                             new Size(leftMaxW, int.MaxValue), TextFormatFlags.WordBreak).Height;
                    int hR = TextRenderer.MeasureText(right, fontBody).Height;

                    y += Math.Max(hL, hR) + 4;
                }

                y += card.SectionGap;

                // Trait distribution heading
                y += TextRenderer.MeasureText("Trait spread:", fontBody,
                      new Size(contentW, int.MaxValue), TextFormatFlags.WordBreak).Height + card.LineGap;

                // Trait distribution rows
                foreach (var (name, val) in card.AttrLines ?? Enumerable.Empty<(string Name, int Value)>())
                {
                    string line = $"{name}:+{val}";
                    y += TextRenderer.MeasureText(line, fontBody,
                         new Size(contentW, int.MaxValue), TextFormatFlags.WordBreak).Height + 4;
                }

                int minH = (int)(card.MinHeightDp * dpi);
                return Math.Max(minH, y + padT + padB);
            }

            int unifiedCardH = cards.Count == 0 ? (int)(160 * dpi) : cards.Max(MeasureCardHeight);
            foreach (var c in cards) c.ForceCardHeightPx = unifiedCardH;

            // ========== Step 3: add and refresh once ==========
            virtualPanel1.SuspendLayout();
            foreach (var c in cards) virtualPanel1.Items.Add(c);
            virtualPanel1.ResumeLayout();
            virtualPanel1.Refresh();              // Single refresh to apply size changes


        }

        private void chkAttackSpeedFocus_CheckedChanged(object sender, BoolEventArgs e)
        {
            var checkbox = (Checkbox)sender;
            if (e.Value)
            {

                BuildEliteCandidatePool.Attributes.Add(checkbox.Text ?? string.Empty);
            }
            else
            {

                BuildEliteCandidatePool.Attributes.Remove(checkbox.Text ?? string.Empty);
            }
        }

        private void select1_SelectedIndexChanged(object sender, IntEventArgs e)
        {
            BuildEliteCandidatePool.type = select1.SelectedValue?.ToString() ?? string.Empty;
        }

        private void button3_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void button4_Click(object sender, EventArgs e)
        {
            MessageBox.Show("For decoration only.");
        }

        private void ModuleCalculationForm_ForeColorChanged(object sender, EventArgs e)
        {
            if (Config.IsLight)
            {
                groupBox1.ForeColor = groupBox3.ForeColor = Color.Black;
            }
            else
            {
                groupBox1.ForeColor = groupBox3.ForeColor = Color.White;

            }

        }
        ModuleExcludeForm? moduleExcludeForm = null;
        private void button2_Click(object sender, EventArgs e)
        {

            if (moduleExcludeForm == null || moduleExcludeForm.IsDisposed)
            {
                moduleExcludeForm = new ModuleExcludeForm();
            }
            moduleExcludeForm.Show();
        }



        private void select2_SelectedIndexChanged(object sender, IntEventArgs e)
        {
            // Shared handler: all five dropdowns route here; use Tag to identify the row
            if (sender is Select combo)
            {
                var rowIndex = combo.Tag.ToInt();

                string selectedAttr = combo.SelectedValue?.ToString() ?? string.Empty;

                // Track the previous attribute for this row so we can clean up state
                string? old = null;
                if (BuildEliteCandidatePool.WhitelistPickByRow.TryGetValue(rowIndex, out var oldName))
                    old = oldName;

                // Update the per-row mapping
                if (!string.IsNullOrEmpty(selectedAttr))
                    BuildEliteCandidatePool.WhitelistPickByRow[rowIndex] = selectedAttr;
                else
                    BuildEliteCandidatePool.WhitelistPickByRow.Remove(rowIndex);

                // Rebuild Attributes (whitelist) from all non-empty row selections
                var newWhitelist = BuildEliteCandidatePool.WhitelistPickByRow.Values
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                BuildEliteCandidatePool.Attributes.Clear();
                BuildEliteCandidatePool.Attributes.AddRange(newWhitelist);  // Used for highest-level display and priority checks

                // Remove desired level for the old attribute to avoid stale data
                if (!string.IsNullOrEmpty(old) && old != selectedAttr)
                {
                    BuildEliteCandidatePool.DesiredLevels.Remove(old);
                }
                // Conflict guard: if the attribute was excluded, remove it from that set and clear the UI
                if (!string.IsNullOrEmpty(selectedAttr)
                    && BuildEliteCandidatePool.ExcludedAttributes.Contains(selectedAttr))
                {
                    // 1) Remove it from the exclusion set
                    BuildEliteCandidatePool.ExcludedAttributes.Remove(selectedAttr);

                    // 2) Clear any exclusion dropdown currently showing this attribute
                    foreach (var exSel in GetAllExcludeSelects())
                    {
                        if (exSel?.SelectedValue?.ToString() == selectedAttr)
                        {
                            exSel.SelectedIndex = -1;
                            exSel.Tag = null; // Reset Tag since the exclusion handler stored the previous value here
                        }
                    }
                }
            }
        }

        private void inputNumber1_ValueChanged(object sender, DecimalEventArgs e)
        {
            if (sender is InputNumber num)
            {
                int rowIndex = num.Tag.ToInt();

                // Locate the whitelist attribute currently bound to this row
                if (!BuildEliteCandidatePool.WhitelistPickByRow.TryGetValue(rowIndex, out var attrName)
                    || string.IsNullOrEmpty(attrName))
                {
                    return; // Ignore rows without a selected attribute
                }

                // Read the decimal Value directly
                int desiredLevel = Convert.ToInt32(num.Value);
                desiredLevel = Math.Max(0, Math.Min(6, desiredLevel));

                if (desiredLevel > 0)
                    BuildEliteCandidatePool.DesiredLevels[attrName] = desiredLevel;
                else
                    BuildEliteCandidatePool.DesiredLevels.Remove(attrName);
            }
        }

        private void select12_SelectedIndexChanged_1(object sender, IntEventArgs e)
        {
            if (sender is AntdUI.Select combo)
            {
                // Remove the previous exclusion value to avoid duplicates
                BuildEliteCandidatePool.ExcludedAttributes.RemoveWhere(x => x == (string)(combo.Tag ?? string.Empty));

                // Apply the new selection
                var selectedAttr = combo.SelectedValue?.ToString() ?? string.Empty;

                if (!string.IsNullOrEmpty(selectedAttr))
                {
                    // Use Tag to note which dropdown this is to avoid overwriting
                    combo.Tag = selectedAttr;
                    BuildEliteCandidatePool.ExcludedAttributes.Add(selectedAttr);
                }
                // Conflict guard: if excluded, clear matching whitelist rows (leave desired levels untouched)
                if (!string.IsNullOrEmpty(selectedAttr))
                {
                    // Find every row that selected the same attribute
                    var hitRows = BuildEliteCandidatePool.WhitelistPickByRow
                        .Where(kv => string.Equals(kv.Value, selectedAttr, StringComparison.Ordinal))
                        .Select(kv => kv.Key)
                        .ToList();

                    if (hitRows.Count > 0)
                    {
                        // 1) Remove those rows from the row-to-attribute map (keep DesiredLevels)
                        foreach (var r in hitRows)
                        {
                            BuildEliteCandidatePool.WhitelistPickByRow.Remove(r);

                            // 2) Clear the target-dropdown UI for those rows
                            var wlSel = GetWhitelistSelectByRow(r);
                            if (wlSel != null) wlSel.SelectedIndex = -1;
                            // Leave the associated InputNumber as-is so user levels remain
                        }

                        // 3) Rebuild the whitelist Attributes collection
                        var newWhitelist = BuildEliteCandidatePool.WhitelistPickByRow.Values
                            .Where(s => !string.IsNullOrEmpty(s))
                            .Distinct(StringComparer.Ordinal)
                            .ToList();
                        BuildEliteCandidatePool.Attributes.Clear();
                        BuildEliteCandidatePool.Attributes.AddRange(newWhitelist);
                    }
                }

            }
        }

        private void select3_SelectedIndexChanged(object sender, IntEventArgs e)
        {

        }

        private void select2_ClearClick(object sender, MouseEventArgs e)
        {

        }

        /// <summary>
        /// Clear an exclusion entry via right-click
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void selectEmptyRule_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;

            if (sender is AntdUI.Select combo)
            {
                // Remove the previously stored excluded attribute
                if (combo.Tag is string old && !string.IsNullOrEmpty(old))
                {
                    BuildEliteCandidatePool.ExcludedAttributes.Remove(old);
                }

                // Clear the dropdown display (if supported by AntdUI.Select)
                combo.SelectedIndex = -1;

                // Reset the Tag
                combo.Tag = null;
            }
        }

        /// <summary>
        /// Clear attributes for every row
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void selectClearSelection_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            if (sender is AntdUI.Select combo)
            {
                if (!(combo.Tag is string tagStr) || !int.TryParse(tagStr, out int row)) return;

                // 1) Find the whitelist attribute bound to this row
                if (BuildEliteCandidatePool.WhitelistPickByRow.TryGetValue(row, out var attrName)
                    && !string.IsNullOrEmpty(attrName))
                {
                    // 2) Remove the desired level entry
                    BuildEliteCandidatePool.DesiredLevels.Remove(attrName);

                    // 3) Remove the row binding
                    BuildEliteCandidatePool.WhitelistPickByRow.Remove(row);
                }

                // 4) Rebuild Attributes from all non-empty row selections
                var newWhitelist = BuildEliteCandidatePool.WhitelistPickByRow.Values
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                BuildEliteCandidatePool.Attributes.Clear();
                BuildEliteCandidatePool.Attributes.AddRange(newWhitelist);

                // 5) Reset the dropdown and numeric input for this row
                combo.SelectedIndex = -1;
                var inputNumber = GetDesiredLevelControlByRow(row);
                if (inputNumber != null)
                {
                    inputNumber.Value = 0;
                }

                // (Keep combo.Tag as the row index for continued use)
            }
        }
        private InputNumber? GetDesiredLevelControlByRow(int row)
        {
            return row switch
            {
                0 => inputNumber1,
                1 => inputNumber2,
                2 => inputNumber3,
                3 => inputNumber4,
                4 => inputNumber5,
                _ => null
            };
        }

        private Select? GetWhitelistSelectByRow(int row)
        {
            return row switch
            {
                0 => select2,
                1 => select4,
                2 => select5,
                3 => select6,
                4 => select7,
                _ => null
            };
        }

        private IEnumerable<AntdUI.Select> GetAllExcludeSelects()
        {
            // Hook up your exclusion dropdowns (adjust names as needed)
            yield return select8;
            yield return select9;
            yield return select10;
            yield return select11;
            yield return select12;
        }

        private void select3_SelectedIndexChanged_1(object sender, IntEventArgs e)
        {
            // e.Value is the selected index
            BuildEliteCandidatePool.SortBy = (e.Value == 0)
                ? ModuleOptimizer.SortMode.ByTotalAttr
                : ModuleOptimizer.SortMode.ByScore;
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // 1) Clear all target/exclusion/level selections in the UI
            BuildEliteCandidatePool.ResetSelections(keepSortMode: false);
            // keepSortMode=false: also reset the sort mode to the default defined above.
            // Pass true if you prefer to preserve the previous sort mode.

            // 2) (Optional) Clear cached/visible cards in the UI
            try
            {
                virtualPanel1?.Items?.Clear();
                virtualPanel1?.Refresh();
            }
            catch { /* Ignore disposal-time exceptions when controls are already destroyed */ }

            base.OnFormClosed(e);
        }

    }
}
