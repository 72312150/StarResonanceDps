using System;
using System.Text;

using ClosedXML.Excel;
using StarResonanceDpsAnalysis.WinForm.Plugin.DamageStatistics;

namespace StarResonanceDpsAnalysis.WinForm.Plugin
{
    /// <summary>
    /// Data export helper that supports Excel, CSV, and screenshots.
    /// </summary>
    public static class DataExportService
    {
        #region Excel Export

        /// <summary>
        /// Export DPS data to an Excel file.
        /// </summary>
        /// <param name="players">Player summaries.</param>
        /// <param name="includeSkillDetails">Whether to append skill-level sheets.</param>
        /// <returns>Whether the export succeeded.</returns>
        public static bool ExportToExcel(List<PlayerData> players, bool includeSkillDetails = true)
        {
            try
            {
                using var saveDialog = new SaveFileDialog
                {
                    Filter = "Excel files (*.xlsx)|*.xlsx",
                    DefaultExt = "xlsx",
                    FileName = $"DPS_Report_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.xlsx",
                    Title = "Save DPS report"
                };

                if (saveDialog.ShowDialog() != DialogResult.OK)
                    return false;

                using var workbook = new XLWorkbook();

                // Player overview
                CreatePlayerOverviewSheet(workbook, players);

                if (includeSkillDetails)
                {
                    // Skill details
                    CreateSkillDetailsSheet(workbook, players);

                    // Team skill summary
                    CreateTeamSkillStatsSheet(workbook, players);
                }

                workbook.SaveAs(saveDialog.FileName);

                MessageBox.Show($"Data exported to:\n{saveDialog.FileName}", "Export Successful",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export Excel file:\n{ex.Message}", "Export Failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        /// <summary>
        /// Populate the player overview worksheet.
        /// </summary>
        private static void CreatePlayerOverviewSheet(XLWorkbook workbook, List<PlayerData> players)
        {
            var worksheet = workbook.Worksheets.Add("Player Overview");

            // Headers
            var headers = new[]
            {
                "Player Name", "Class", "Combat Power", "Total Damage", "Total DPS", "Critical Damage", "Lucky Damage",
                "Critical Rate", "Lucky Rate", "Peak Instant DPS", "Total Healing", "Total HPS", "Damage Taken", "Hit Count"
            };

            // Write header row
            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cell(1, i + 1).Value = headers[i];
                worksheet.Cell(1, i + 1).Style.Font.Bold = true;
                worksheet.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightBlue;
            }

            // Data rows
            int row = 2;
            foreach (var player in players.OrderByDescending(p => p.DamageStats.Total))
            {
                worksheet.Cell(row, 1).Value = player.Nickname;
                worksheet.Cell(row, 2).Value = player.Profession;
                worksheet.Cell(row, 3).Value = player.CombatPower;
                worksheet.Cell(row, 4).Value = (double)player.DamageStats.Total;
                worksheet.Cell(row, 5).Value = Math.Round(player.GetTotalDps(), 1);
                worksheet.Cell(row, 6).Value = (double)player.DamageStats.Critical;
                worksheet.Cell(row, 7).Value = (double)player.DamageStats.Lucky;
                worksheet.Cell(row, 8).Value = $"{player.DamageStats.GetCritRate()}%";
                worksheet.Cell(row, 9).Value = $"{player.DamageStats.GetLuckyRate()}%";
                worksheet.Cell(row, 10).Value = (double)player.DamageStats.RealtimeMax;
                worksheet.Cell(row, 11).Value = (double)player.HealingStats.Total;
                worksheet.Cell(row, 12).Value = Math.Round(player.GetTotalHps(), 1);
                worksheet.Cell(row, 13).Value = (double)player.TakenDamage;
                worksheet.Cell(row, 14).Value = player.DamageStats.CountTotal;

                row++;
            }

            // Auto-fit columns
            worksheet.ColumnsUsed().AdjustToContents();

            // Enable filters
            worksheet.Range(1, 1, row - 1, headers.Length).SetAutoFilter();
        }

        /// <summary>
        /// Populate the skill details worksheet.
        /// </summary>
        private static void CreateSkillDetailsSheet(XLWorkbook workbook, List<PlayerData> players)
        {
            var worksheet = workbook.Worksheets.Add("Skill Details");

            // Headers
            var headers = new[]
            {
                "Player Name", "Skill Name", "Total Damage", "Hit Count", "Average Damage",
                "Critical Rate", "Lucky Rate", "Skill DPS", "Damage Share"
            };

            // Header row
            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cell(1, i + 1).Value = headers[i];
                worksheet.Cell(1, i + 1).Style.Font.Bold = true;
                worksheet.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightGreen;
            }

            // Data rows
            int row = 2;
            foreach (var player in players.OrderByDescending(p => p.DamageStats.Total))
            {
                var skills = StatisticData._manager.GetPlayerSkillSummaries(
                    player.Uid, topN: null, orderByTotalDesc: true);

                foreach (var skill in skills)
                {
                    worksheet.Cell(row, 1).Value = player.Nickname;
                    worksheet.Cell(row, 2).Value = skill.SkillName;
                    worksheet.Cell(row, 3).Value = (double)skill.Total;
                    worksheet.Cell(row, 4).Value = skill.HitCount;
                    worksheet.Cell(row, 5).Value = Math.Round(skill.AvgPerHit, 1);
                    worksheet.Cell(row, 6).Value = $"{skill.CritRate * 100:F1}%";
                    worksheet.Cell(row, 7).Value = $"{skill.LuckyRate * 100:F1}%";
                    worksheet.Cell(row, 8).Value = Math.Round(skill.TotalDps, 1);
                    worksheet.Cell(row, 9).Value = $"{skill.ShareOfTotal * 100:F1}%";

                    row++;
                }
            }

            // Auto-fit columns
            worksheet.ColumnsUsed().AdjustToContents();

            // Enable filters if data exists
            if (row > 2)
                worksheet.Range(1, 1, row - 1, headers.Length).SetAutoFilter();
        }

        /// <summary>
        /// Populate the team skill summary worksheet.
        /// </summary>
        private static void CreateTeamSkillStatsSheet(XLWorkbook workbook, List<PlayerData> players)
        {
            var worksheet = workbook.Worksheets.Add("Team Skill Summary");

            // Gather team skill data
            var teamSkills = StatisticData._manager.GetTeamTopSkillsByTotal(50);

            // Headers
            var headers = new[]
            {
                "Skill Name", "Total Damage", "Total Hit Count", "Team Share"
            };

            // Header row
            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cell(1, i + 1).Value = headers[i];
                worksheet.Cell(1, i + 1).Style.Font.Bold = true;
                worksheet.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightYellow;
            }

            // Total damage to calculate percentages
            ulong totalTeamDamage = (ulong)teamSkills.Sum(s => (double)s.Total);

            // Data rows
            int row = 2;
            foreach (var skill in teamSkills)
            {
                worksheet.Cell(row, 1).Value = skill.SkillName;
                worksheet.Cell(row, 2).Value = (double)skill.Total;
                worksheet.Cell(row, 3).Value = skill.HitCount;
                worksheet.Cell(row, 4).Value = totalTeamDamage > 0 ?
                    $"{((double)skill.Total / totalTeamDamage) * 100:F1}%" : "0%";

                row++;
            }

            // Auto-fit columns
            worksheet.ColumnsUsed().AdjustToContents();

            // Enable filters if data exists
            if (row > 2)
                worksheet.Range(1, 1, row - 1, headers.Length).SetAutoFilter();
        }

        #endregion

        #region CSV Export

        /// <summary>
        /// Export DPS data to a CSV file.
        /// </summary>
        /// <param name="players">Player summaries.</param>
        /// <returns>Whether the export succeeded.</returns>
        public static bool ExportToCsv(List<PlayerData> players)
        {
            try
            {
                using var saveDialog = new SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv",
                    DefaultExt = "csv",
                    FileName = $"DPS_Report_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv",
                    Title = "Save DPS report"
                };

                if (saveDialog.ShowDialog() != DialogResult.OK)
                    return false;

                var csv = new StringBuilder();

                // Add BOM so Excel opens UTF-8 correctly
                csv.Append('\uFEFF');

                // CSV header
                csv.AppendLine("Player Name,Class,Combat Power,Total Damage,Total DPS,Critical Damage,Lucky Damage,Critical Rate,Lucky Rate,Peak Instant DPS,Total Healing,Total HPS,Damage Taken,Hit Count");

                // Data rows
                foreach (var player in players.OrderByDescending(p => p.DamageStats.Total))
                {
                    csv.AppendLine($"\"{EscapeCsvField(player.Nickname)}\"," +
                                 $"\"{EscapeCsvField(player.Profession)}\"," +
                                 $"{player.CombatPower}," +
                                 $"{player.DamageStats.Total}," +
                                 $"{player.GetTotalDps():F1}," +
                                 $"{player.DamageStats.Critical}," +
                                 $"{player.DamageStats.Lucky}," +
                                 $"{player.DamageStats.GetCritRate()}%," +
                                 $"{player.DamageStats.GetLuckyRate()}%," +
                                 $"{player.DamageStats.RealtimeMax}," +
                                 $"{player.HealingStats.Total}," +
                                 $"{player.GetTotalHps():F1}," +
                                 $"{player.TakenDamage}," +
                                 $"{player.DamageStats.CountTotal}");
                }

                File.WriteAllText(saveDialog.FileName, csv.ToString(), Encoding.UTF8);

                MessageBox.Show($"Data exported to:\n{saveDialog.FileName}", "Export Successful",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export CSV file:\n{ex.Message}", "Export Failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        /// <summary>
        /// Escape CSV fields that contain special characters.
        /// </summary>
        private static string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field))
                return "";

            // Quote the field and escape embedded quotes if needed.
            if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
            {
                return field.Replace("\"", "\"\"");
            }

            return field;
        }

        #endregion

        #region Screenshot Support

        /// <summary>
        /// Save a screenshot of the specified form.
        /// </summary>
        /// <param name="form">The window to capture.</param>
        /// <returns>Whether the screenshot succeeded.</returns>
        public static bool SaveScreenshot(Form form)
        {
            try
            {
                using var saveDialog = new SaveFileDialog
                {
                    Filter = "PNG images (*.png)|*.png|JPEG images (*.jpg)|*.jpg",
                    DefaultExt = "png",
                    FileName = $"DPS_Screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png",
                    Title = "Save DPS UI screenshot"
                };

                if (saveDialog.ShowDialog() != DialogResult.OK)
                    return false;

                // Render bitmap at window size
                var bounds = form.Bounds;
                using var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
                using var graphics = System.Drawing.Graphics.FromImage(bitmap);

                // Capture window contents
                graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);

                // Save using the requested format
                var extension = Path.GetExtension(saveDialog.FileName).ToLower();
                var format = extension switch
                {
                    ".jpg" or ".jpeg" => System.Drawing.Imaging.ImageFormat.Jpeg,
                    _ => System.Drawing.Imaging.ImageFormat.Png
                };

                bitmap.Save(saveDialog.FileName, format);

                MessageBox.Show($"Screenshot saved to:\n{saveDialog.FileName}", "Screenshot Saved",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save screenshot:\n{ex.Message}", "Screenshot Failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Gather players that currently have combat data.
        /// </summary>
        /// <returns>The player list.</returns>
        public static List<PlayerData> GetCurrentPlayerData()
        {
            return StatisticData._manager
                .GetPlayersWithCombatData()
                .ToList();
        }

        /// <summary>
        /// Check whether there is any data to export.
        /// </summary>
        /// <returns>True when data exists.</returns>
        public static bool HasDataToExport()
        {
            return GetCurrentPlayerData().Count > 0;
        }

        #endregion
    }
}