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
            ["Marksman"] = "神射手",
            ["Shield Knight"] = "神盾骑士",
            ["Stormblade"] = "雷影剑士",
            ["Frost Mage"] = "冰魔导师",
            ["Wind Knight"] = "青岚骑士",
            ["Verdant Oracle"] = "森语者",
            ["Heavy Guardian"] = "巨刃守护者",
            ["Soul Musician"] = "灵魂乐手",
            ["Unknown"] = "未知职业"
        };

        private static readonly Dictionary<string, string> ProfessionInternalToDisplay =
            ProfessionDisplayToInternal.ToDictionary(kvp => kvp.Value, kvp => kvp.Key, StringComparer.OrdinalIgnoreCase);

        public UserUidSetForm()
        {
            InitializeComponent();
            FormGui.SetDefaultGUI(this);
        }

        /// <summary>
        /// 优化的UID设置控件 - 增强验证和用户体验
        /// </summary>
        private void UserUidSet_Load(object sender, EventArgs e)
        {
            // 从AppConfig加载已保存的设置到界面
            LoadCurrentSettingsToUI();

            // 添加实时验证
            InitializeValidation();

            // 显示当前用户信息
            DisplayCurrentUserInfo();
        }

        /// <summary>
        /// 从AppConfig加载当前设置到界面控件
        /// </summary>
        private void LoadCurrentSettingsToUI()
        {
            try
            {
                // 加载昵称设置
                string savedNickname = AppConfig.GetValue("UserConfig", "NickName", "Unknown nickname");
                if (string.Equals(savedNickname, "未知昵称", StringComparison.OrdinalIgnoreCase))
                {
                    savedNickname = "Unknown nickname";
                }
                input2.Text = savedNickname;

                // 安全地加载UID设置
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

                    // 修复损坏的配置
                    AppConfig.SetValue("UserConfig", "Uid", "0");
                }
                var savedProfession = AppConfig.GetValue("UserConfig", "Profession", "未知职业");
                if (!ProfessionInternalToDisplay.TryGetValue(savedProfession, out var professionDisplay))
                {
                    professionDisplay = "Unknown";
                }
                select1.SelectedValue = professionDisplay;

                // 确保AppConfig的全局属性与界面同步
                AppConfig.Uid = (long)inputNumber1.Value;
                AppConfig.NickName = input2.Text;
                AppConfig.Profession = savedProfession;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load user settings: {ex.Message}");

                // 出错时设置默认值
                inputNumber1.Value = 0;
                input2.Text = "Unknown nickname";
            }
        }

        /// <summary>
        /// 初始化输入验证
        /// </summary>
        private void InitializeValidation()
        {
            // UID输入验证 - 确保是有效的ulong范围
            inputNumber1.ValueChanged += (s, e) =>
            {
                if (inputNumber1.Value > ulong.MaxValue || inputNumber1.Value < 0)
                {
                    inputNumber1.Value = 0;
                    MessageBox.Show($"UID must be between 0 and {ulong.MaxValue}.", "Input Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            // 昵称输入验证
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
        /// 显示当前用户信息
        /// </summary>
        private void DisplayCurrentUserInfo()
        {
            var currentUid = (long)inputNumber1.Value;
            if (currentUid > 0)
            {
                var (nickname, combatPower, profession) = StatisticData._manager.GetPlayerBasicInfo(currentUid);

                // 可以添加一个信息显示区域
                Console.WriteLine($"Current user - UID: {currentUid}, Nickname: {nickname}, Power: {combatPower}, Profession: {profession}");

                // 如果有UI标签可以显示这些信息
                // lblCurrentInfo.Text = $"当前: {nickname} (战力: {combatPower})";
            }
        }

        /// <summary>
        /// 公开的保存用户设置方法，供Modal调用
        /// </summary>
        public void SaveUserSettings()
        {
            // 验证输入数据
            if (!ValidateInput(out string errorMessage))
            {
                throw new ArgumentException(errorMessage);
            }

            // 从界面获取当前输入的值
            var newUid = (long)inputNumber1.Value;
            string newNickname = input2.Text.Trim();
            string professionDisplay = select1.SelectedValue?.ToString()?.Trim() ?? string.Empty;
            string professionInternal = GetProfessionInternal(professionDisplay);

            // 获取原始配置值用于比较
            string oldUidStr = AppConfig.GetValue("UserConfig", "Uid", "0");
            string oldNickname = AppConfig.GetValue("UserConfig", "NickName", "Unknown nickname");
            if (string.Equals(oldNickname, "未知昵称", StringComparison.OrdinalIgnoreCase))
            {
                oldNickname = "Unknown nickname";
            }
            string oldProfession = AppConfig.GetValue("UserConfig", "Profession", "未知职业");


            bool uidChanged = !long.TryParse(oldUidStr, out long oldUid) || oldUid != newUid;
            bool nicknameChanged = oldNickname != newNickname;
            bool professionChanged = !string.Equals(oldProfession, professionInternal, StringComparison.Ordinal);

            // 只有当值真正发生变化时才保存
            if (!uidChanged && !nicknameChanged && !professionChanged)
            {
                Console.WriteLine("No changes detected; skipping save.");
                return;
            }

            // 保存界面配置到AppConfig
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

            // 更新全局AppConfig属性以保持一致性
            AppConfig.Uid = newUid;
            AppConfig.NickName = newNickname;
            AppConfig.Profession = professionInternal;

            // 同步到统计数据管理器
            StatisticData._manager.SetNickname(newUid, newNickname);
            StatisticData._manager.SetProfession(newUid, professionInternal);

            // 如果UID发生变化，询问用户是否清空统计数据
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

            // 显示保存成功的反馈
            Console.WriteLine($"Settings saved successfully - UID: {newUid}, Nickname: {newNickname}, Profession: {professionInternal}");
        }

        /// <summary>
        /// 验证用户输入
        /// </summary>
        private bool ValidateInput(out string errorMessage)
        {
            errorMessage = string.Empty;

            // 验证UID
            if (inputNumber1.Value <= 0)
            {
                errorMessage = "UID must be greater than 0.";
                return false;
            }

            // 验证昵称
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

            // 可以添加更多验证规则，如特殊字符检查
            if (nickname.Contains("<") || nickname.Contains(">") || nickname.Contains("&"))
            {
                errorMessage = "Nickname cannot contain special characters < > &.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 原始的保存按钮逻辑 - 保留向后兼容性
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

            return ProfessionDisplayToInternal.TryGetValue(displayName, out var internalName)
                ? internalName
                : displayName;
        }
    }
}
