using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

using SharpPcap;
using StarResonanceDpsAnalysis.Assets;
using StarResonanceDpsAnalysis.Core.Extends.System;
using StarResonanceDpsAnalysis.WinForm.Plugin;

namespace StarResonanceDpsAnalysis.WinForm.Forms
{
    public partial class SettingsForm
    {
        private bool _netcardChanged = false;
        /// <summary>
        /// Apply fonts sourced from resources
        /// </summary>
        private void SetDefaultFontFromResources()
        {
            label_TitleText.Font = AppConfig.TitleFont;

            label_SettingTitle.Font = AppConfig.HeaderFont;

            label_BasicSetupTitle.Font = label_KeySettingsTitle.Font = label_CombatSettingsTitle.Font =
            select_NetcardSelector.Font = select_DamageDisplayType.Font = label_DpsShowType.Font =
            label_Transparent.Font = input_MouseThroughKey.Font = input_ClearData.Font =
            inputNumber_ClearSectionedDataTime.Font = label_ClearAllDataWhenSwitch.Font = AppConfig.ContentFont;

            var harmonyOsSansFont = HandledAssets.HarmonyOS_Sans(7);
            label_KeySettingsTip.Font = label_BasicSetupTip.Font = label_CombatSettingsTip.Font = harmonyOsSansFont;
        }

        /// <summary>
        /// Apply persisted configuration values to the UI
        /// </summary>
        private void LoadConfigSetUI()
        {
            // Keep LoadDevices and netcardIndex assignment together to avoid adapter changes mid-load
            var devices = CaptureDeviceList.Instance;
            LoadDevices(devices);

            var netcardIndex = AppConfig.GetNetworkCardIndex(devices);
            if (netcardIndex >= 0)
            {
                select_NetcardSelector.SelectedIndex = netcardIndex;
            }

            input_MouseThroughKey.Text = AppConfig.MouseThroughKey?.ToString() ?? string.Empty;
            input_ClearData.Text = AppConfig.ClearDataKey?.ToString() ?? string.Empty;
            inputNumber_ClearSectionedDataTime.Value = AppConfig.CombatTimeClearDelaySeconds;
            switch_ClearAllDataWhenSwitch.Checked = AppConfig.ClearAllDataWhenSwitch;
            select_DamageDisplayType.SelectedIndex = AppConfig.DamageDisplayType;
            slider_Transparency.Value = AppConfig.Transparency;
        }

        /// <summary>
        /// Populate the dropdown with all local network adapters
        /// </summary>
        private void LoadDevices(CaptureDeviceList devices)
        {
            select_NetcardSelector.Items.Clear();

            foreach (var d in devices) select_NetcardSelector.Items.Add(d.Description);
        }

        private void SaveDataToConfig()
        {
            AppConfig.NetworkCardName = select_NetcardSelector.Text;
            AppConfig.CombatTimeClearDelaySeconds = inputNumber_ClearSectionedDataTime.Value.ToInt();
            AppConfig.ClearAllDataWhenSwitch = switch_ClearAllDataWhenSwitch.Checked;
            AppConfig.DamageDisplayType = select_DamageDisplayType.SelectedIndex;
            AppConfig.Transparency = slider_Transparency.Value;
        }
    }
}
