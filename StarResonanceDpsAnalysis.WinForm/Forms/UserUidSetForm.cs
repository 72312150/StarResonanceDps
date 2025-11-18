using System;
using System.Collections.Generic;
using System.Linq;

using AntdUI;
using StarResonanceDpsAnalysis.WinForm.Plugin;
using StarResonanceDpsAnalysis.WinForm.Plugin.DamageStatistics;

namespace StarResonanceDpsAnalysis.WinForm.Forms
{
    public partial class UserUidSetForm : BorderlessForm
    {
        private static readonly Dictionary<string, string> ProfessionDisplayToInternal = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Marksman"] = "Marksman",
            ["Shield Knight"] = "Shield Knight",
            ["Stormblade"] = "Stormblade",
            ["Frost Mage"] = "Frost Mage",
            ["Wind Knight"] = "Wind Knight",
            ["Verdant Oracle"] = "Verdant Oracle",
            ["Heavy Guardian"] = "Heavy Guardian",
            ["Soul Musician"] = "Soul Musician",
            ["Unknown"] = "Unknown Profession",
            ["Unknown Profession"] = "Unknown Profession"
        };

        private static readonly Dictionary<string, string> ProfessionSynonymsToInternal = new(StringComparer.OrdinalIgnoreCase)
        {
            ["神射手"] = "Marksman",
            ["神盾骑士"] = "Shield Knight",
            ["雷影剑士"] = "Stormblade",
            ["冰魔导师"] = "Frost Mage",
            ["青岚骑士"] = "Wind Knight",
            ["森语者"] = "Verdant Oracle",
            ["巨刃守护者"] = "Heavy Guardian",
            ["灵魂乐手"] = "Soul Musician",
            ["未知职业"] = "Unknown Profession"
        };

        private static readonly Dictionary<string, string> ProfessionInternalToDisplay = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Marksman"] = "Marksman",
            ["Shield Knight"] = "Shield Knight",
            ["Stormblade"] = "Stormblade",
            ["Frost Mage"] = "Frost Mage",
            ["Wind Knight"] = "Wind Knight",
            ["Verdant Oracle"] = "Verdant Oracle",
            ["Heavy Guardian"] = "Heavy Guardian",
            ["Soul Musician"] = "Soul Musician",
            ["Unknown Profession"] = "Unknown"
        };

        public UserUidSetForm()
        {
            InitializeComponent();
            FormGui.SetDefaultGUI(this);
        }

        /// <summary>
        /// Enhanced UID settings workflow with validation and better UX
        /// </summary>
        private void UserUidSet_Load(object sender, EventArgs e)
        {
            // Load persisted settings from AppConfig into the UI
            LoadCurrentSettingsToUI();

            // Enable real-time validation
            InitializeValidation();

            // Display the current user information
            DisplayCurrentUserInfo();
        }

        /// <summary>
        /// Load the current configuration into UI controls
        /// </summary>
        private void LoadCurrentSettingsToUI()
        {
            try
            {
                // Load the saved nickname
                string savedNickname = AppConfig.GetValue("UserConfig", "NickName", "Unknown nickname");
                if (string.Equals(savedNickname, "未知昵称", StringComparison.OrdinalIgnoreCase))
                {
                    savedNickname = "Unknown nickname";
                }
                input2.Text = savedNickname;

                // Load the saved UID safely
                string savedUidStr = AppConfig.GetValue("UserConfig", "Uid", "0");
                if (ulong.TryParse(savedUidStr, out ulong savedUid))
                {
                    inputNumber1.Value = savedUid;
                    Console.WriteLine($"Loaded saved settings - UID: {savedUid}, Nickname: {savedNickname}");
                }
                else
                {
                    inputNumber1.Value = 0;
                    Console.WriteLine($"UID configuration is invalid: {savedUidStr}. Reset to 0.");

                    // Repair corrupted configuration
                    AppConfig.SetValue("UserConfig", "Uid", "0");
                }
                var savedProfessionRaw = AppConfig.GetValue("UserConfig", "Profession", "Unknown Profession");
                var savedProfession = GetProfessionInternal(savedProfessionRaw);
                if (!ProfessionInternalToDisplay.TryGetValue(savedProfession, out var professionDisplay))
                {
                    professionDisplay = "Unknown";
                    savedProfession = "Unknown Profession";
                }
                select1.SelectedValue = professionDisplay;

                // Keep the global AppConfig fields in sync with the UI
                AppConfig.Uid = (long)inputNumber1.Value;
                AppConfig.NickName = input2.Text;
                AppConfig.Profession = savedProfession;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load user settings: {ex.Message}");

                // Fall back to defaults on failure
                inputNumber1.Value = 0;
                input2.Text = "Unknown nickname";
            }
        }

        /// <summary>
        /// Wire up input validation
        /// </summary>
        private void InitializeValidation()
        {
            // UID validation – ensure the value fits within the ulong range
            inputNumber1.ValueChanged += (s, e) =>
            {
                if (inputNumber1.Value > ulong.MaxValue || inputNumber1.Value < 0)
                {
                    inputNumber1.Value = 0;
                    MessageBox.Show($"UID must be between 0 and {ulong.MaxValue}.", "Input Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            // Nickname length guard
            input2.TextChanged += (s, e) =>
            {
                string nickname = input2.Text.Trim();
                if (nickname.Length > 20)
                {
                    input2.Text = nickname.Substring(0, 20);
                    input2.SelectionStart = input2.Text.Length;
                    MessageBox.Show("Nickname cannot exceed 20 characters.", "Input Notice", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
        }

        /// <summary>
        /// Display the currently selected user details
        /// </summary>
        private void DisplayCurrentUserInfo()
        {
            var currentUid = (long)inputNumber1.Value;
            if (currentUid > 0)
            {
                var (nickname, combatPower, profession) = StatisticData._manager.GetPlayerBasicInfo(currentUid);

                // Optional: update a UI region with this information
                Console.WriteLine($"Current user - UID: {currentUid}, Nickname: {nickname}, Power: {combatPower}, Profession: {profession}");

                // If there is a UI label, it could display:
                // lblCurrentInfo.Text = $"Current: {nickname} (Power: {combatPower})";
            }
        }

        /// <summary>
        /// Public entry point to persist the user settings (used by the modal)
        /// </summary>
        public void SaveUserSettings()
        {
            // Validate incoming data
            if (!ValidateInput(out string errorMessage))
            {
                throw new ArgumentException(errorMessage);
            }

            // Gather the values currently shown in the UI
            var newUid = (long)inputNumber1.Value;
            string newNickname = input2.Text.Trim();
            string professionDisplay = select1.SelectedValue?.ToString()?.Trim() ?? string.Empty;
            string professionInternal = GetProfessionInternal(professionDisplay);

            // Retrieve the previous configuration values for comparison
            string oldUidStr = AppConfig.GetValue("UserConfig", "Uid", "0");
            string oldNickname = AppConfig.GetValue("UserConfig", "NickName", "Unknown nickname");
            if (string.Equals(oldNickname, "未知昵称", StringComparison.OrdinalIgnoreCase))
            {
                oldNickname = "Unknown nickname";
            }
            string oldProfessionRaw = AppConfig.GetValue("UserConfig", "Profession", "Unknown Profession");
            string oldProfession = GetProfessionInternal(oldProfessionRaw);

            bool uidChanged = !long.TryParse(oldUidStr, out long oldUid) || oldUid != newUid;
            bool nicknameChanged = oldNickname != newNickname;
            bool professionChanged = !string.Equals(oldProfession, professionInternal, StringComparison.Ordinal);

            // Persist only when something actually changed
            if (!uidChanged && !nicknameChanged && !professionChanged)
            {
                Console.WriteLine("No changes detected; skipping save.");
                return;
            }

            // Write the UI values back to AppConfig
            if (uidChanged)
            {
                AppConfig.SetValue("UserConfig", "Uid", newUid.ToString());
                Console.WriteLine($"UID updated: {oldUid} → {newUid}");
            }

            if (nicknameChanged)
            {
                AppConfig.SetValue("UserConfig", "NickName", newNickname);
                Console.WriteLine($"Nickname updated: {oldNickname} → {newNickname}");
            }

            if (professionChanged)
            {
                AppConfig.SetValue("UserConfig", "Profession", professionInternal);
                Console.WriteLine($"Profession updated: {oldProfession} → {professionInternal}");
            }

            // Update AppConfig globals to keep everything consistent
            AppConfig.Uid = newUid;
            AppConfig.NickName = newNickname;
            AppConfig.Profession = professionInternal;

            // Synchronize with the statistics manager
            StatisticData._manager.SetNickname(newUid, newNickname);
            StatisticData._manager.SetProfession(newUid, professionInternal);

            // If the UID changed, ask whether to clear existing statistics
            if (uidChanged && oldUid != 0)
            {
                var result = MessageBox.Show(
                    $"UID changed from {oldUid} to {newUid}.\n" +
                    "This may affect how statistics are associated.\n" +
                    "Would you like to clear current statistics to avoid confusion?",
                    "UID Change Notice",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    StatisticData._manager.ClearAll(false);
                    Console.WriteLine("Statistics cleared because UID changed.");
                }
            }

            // Provide a success notification
            Console.WriteLine($"Settings saved successfully - UID: {newUid}, Nickname: {newNickname}, Profession: {professionInternal}");
        }

        /// <summary>
        /// Validate user input
        /// </summary>
        private bool ValidateInput(out string errorMessage)
        {
            errorMessage = string.Empty;

            // Validate UID
            if (inputNumber1.Value <= 0)
            {
                errorMessage = "UID must be greater than 0.";
                return false;
            }

            // Validate nickname
            string nickname = input2.Text.Trim();
            if (string.IsNullOrEmpty(nickname))
            {
                errorMessage = "Nickname cannot be empty.";
                return false;
            }

            if (nickname.Length > 20)
            {
                errorMessage = "Nickname cannot exceed 20 characters.";
                return false;
            }

            // Additional validation rules can go here (e.g., special character checks)
            if (nickname.Contains("<") || nickname.Contains(">") || nickname.Contains("&"))
            {
                errorMessage = "Nickname cannot contain special characters < > &.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Original save button logic — retained for backward compatibility
        /// </summary>
        private async void button2_Click(object sender, EventArgs e)
        {
            try
            {
                SaveUserSettings();
                this.Dispose();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save user settings: {ex.Message}", "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Console.WriteLine($"Error saving user settings: {ex}");
            }
        }

        private void UserUidSet_FormClosed(object sender, FormClosedEventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void panel6_Click(object sender, EventArgs e)
        {

        }


        private void TitleText_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                FormManager.ReleaseCapture();
                FormManager.SendMessage(this.Handle, FormManager.WM_NCLBUTTONDOWN, FormManager.HTCAPTION, 0);
            }
        }

        private static string GetProfessionInternal(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return ProfessionDisplayToInternal["Unknown"];
            }

            if (ProfessionDisplayToInternal.TryGetValue(displayName, out var internalName))
            {
                return internalName;
            }

            if (ProfessionSynonymsToInternal.TryGetValue(displayName, out internalName))
            {
                return internalName;
            }

            return displayName;
        }
    }
}
