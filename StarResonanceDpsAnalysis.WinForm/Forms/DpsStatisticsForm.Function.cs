using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AntdUI;
using SharpPcap;
using StarResonanceDpsAnalysis.Assets;
using StarResonanceDpsAnalysis.Core.Analyze;
using StarResonanceDpsAnalysis.Core.Analyze.Exceptions;
using StarResonanceDpsAnalysis.Core.Data;
using StarResonanceDpsAnalysis.Core.Extends.System;
using StarResonanceDpsAnalysis.WinForm.Control.GDI;
using StarResonanceDpsAnalysis.WinForm.Core;
using StarResonanceDpsAnalysis.WinForm.Forms.PopUp;
using StarResonanceDpsAnalysis.WinForm.Plugin;
using StarResonanceDpsAnalysis.WinForm.Plugin.DamageStatistics;

namespace StarResonanceDpsAnalysis.WinForm.Forms
{
    public partial class DpsStatisticsForm
    {
        private bool _isShowFullData = false;
        private int _stasticsType = 0;

        private void SetDefaultFontFromResources()
        {
            pageHeader_MainHeader.Font = AppConfig.SaoFont;
            pageHeader_MainHeader.SubFont = AppConfig.ContentFont;
            label_CurrentDps.Font = label_CurrentOrder.Font = AppConfig.ContentFont;

            button_TotalDamage.Font = AppConfig.BoldHarmonyFont;
            button_TotalTreatment.Font = AppConfig.BoldHarmonyFont;
            button_AlwaysInjured.Font = AppConfig.BoldHarmonyFont;
            button_NpcTakeDamage.Font = AppConfig.BoldHarmonyFont;
        }

        #region Initialization, capture device setup, statistics bootstrap, capture lifecycle, and shutdown hooks

        #region —— Capture device / statistics —— 

        public static ICaptureDevice? SelectedDevice { get; set; } = null; // Capture device the app selected (may be null until configured)

        /// <summary>
        /// Analyzer used for incoming packets
        /// </summary>
        private PacketAnalyzer PacketAnalyzer { get; } = new(); // Each captured packet is processed through this analyzer
        #endregion

        private void LoadAppConfig() 
        {
            DataStorage.SectionTimeout = TimeSpan.FromSeconds(AppConfig.CombatTimeClearDelaySeconds);
        }

        /// <summary>
        /// Load player cache data
        /// </summary>
        private void LoadPlayerCache()
        {
            try
            {
                DataStorage.LoadPlayerInfoFromFile();
            }
            catch (FileNotFoundException)
            {
                // Cache file not present
            }
            catch (DataTamperedException)
            {
                AppMessageBox.ShowMessage("User cache appears tampered or corrupted. Clearing the cache to keep the tool stable.", this);

                DataStorage.ClearAllPlayerInfos();
                DataStorage.SavePlayerInfoToFile();
            }
        }

        /// <summary>
        /// Load the embedded skill list when the tool starts
        /// </summary>
        private void LoadFromEmbeddedSkillConfig()
        {
            // 1) Consume the int-keyed table first (already parsed)
            foreach (var kv in EmbeddedSkillConfig.AllByInt)
            {
                var id = (long)kv.Key;
                var def = kv.Value;

                // Persist each SkillMeta into the global SkillBook dictionary
                // Using SetOrUpdate ensures existing entries are replaced while new ones are added
                SkillBook.SetOrUpdate(new SkillMeta
                {
                    Id = id,                         // Skill identifier
                    Name = def.Name,                 // Skill name (string such as "Fireball")
                                                     //School = def.Element.ToString(), // Element or school
                                                     //Type = def.Type,                 // Skill type (Damage/Heal/Other)
                                                     // Element = def.Element            // Skill element enum (Fire/Ice/etc.)
                });


            }

            // 2) Fallback to the string-key table for values beyond int range
            foreach (var kv in EmbeddedSkillConfig.AllByString)
            {
                if (kv.Key.TryToInt64(out var id))
                {
                    // Overwrite if already inserted from the int table; that's intentional
                    var def = kv.Value;
                    // Persist each SkillMeta into the global SkillBook dictionary
                    // Using SetOrUpdate ensures existing entries are replaced while new ones are added
                    SkillBook.SetOrUpdate(new SkillMeta
                    {
                        Id = id,                         // Skill identifier
                        Name = def.Name,                 // Skill name (string such as "Fireball")
                        //School = def.Element.ToString(), // Element or school
                        //Type = def.Type,                 // Skill type (Damage/Heal/Other)
                        //Element = def.Element            // Skill element enum (Fire/Ice/etc.)
                    });

                }
            }

            // MonsterNameResolver.Initialize(AppConfig.MonsterNames); // Establish monster ID ↔ name map


            // Optional logging for debugging how many skills were loaded
            // Console.WriteLine($"SkillBook loaded {EmbeddedSkillConfig.AllByInt.Count} + {EmbeddedSkillConfig.AllByString.Count} entries.");
        }

        public void SetStyle()
        {
            // UI styling applied during startup; appearance only, no data logic
            // ======= Appearance for the individual progress bar (textProgressBar1) =======
            sortedProgressBarList_MainList.OrderImageOffset = new RenderContent.ContentOffset { X = 6, Y = 0 };
            sortedProgressBarList_MainList.OrderImageRenderSize = new Size(22, 22);
            sortedProgressBarList_MainList.OrderOffset = new RenderContent.ContentOffset { X = 32, Y = 0 };
            sortedProgressBarList_MainList.OrderCallback = (i) => $"{i:d2}.";
            sortedProgressBarList_MainList.OrderImages = [HandledAssets.皇冠];


            sortedProgressBarList_MainList.OrderColor =
                Config.IsLight ? Color.Black : Color.White;

            sortedProgressBarList_MainList.OrderFont = AppConfig.SaoFont;

            // ======= Initialization and appearance for the progress bar list (sortedProgressBarList1) =======
            sortedProgressBarList_MainList.ProgressBarHeight = AppConfig.ProgressBarHeight;  // Row height
        }

        /// <summary>
        /// Display a standard tooltip
        /// </summary>
        /// <param name="control"></param>
        /// <param name="text"></param>
        /// <remarks>
        /// Helper wrapper that attaches a tooltip to the requested control
        /// </remarks>
        private void ToolTip(System.Windows.Forms.Control control, string text)
        {
            var tooltip = new TooltipComponent()
            {
                Font = HandledAssets.HarmonyOS_Sans(8),
                ArrowAlign = TAlign.TL
            };
            tooltip.SetTip(control, text);
        }
        #region StartCapture() - capture lifecycle events and statistics
        /// <summary>
        /// 开始抓包
        /// </summary>
        public void StartCapture()
        {
            // 检查是否有可抓包设备
            var devices = CaptureDeviceList.Instance;
            if (devices == null || devices.Count == 0)
            {
                AppMessageBox.ShowMessage("No usable network capture device found. Please verify your system configuration.", this);
                return;
            }

            var netcardName = AppConfig.NetworkCardName;
            int netcardIndex;
            // 检查是否设置过网卡设备
            if (string.IsNullOrEmpty(netcardName))
            {
                // 首次自动设置网卡设备

                netcardIndex = CaptureDeviceHelper.GetBestNetworkCardIndex(devices);
                if (netcardIndex < 0)
                {
                    AppMessageBox.ShowMessage("Unable to auto-select a network adapter. Please configure it manually in Settings.", this);
                    return;
                }

                AppConfig.NetworkCardName = devices[netcardIndex].Description;
            }
            else
            {
                // Adapter already configured previously
                netcardIndex = AppConfig.GetNetworkCardIndex(devices);
            }

            // Detect adapter changes
            // (Initial configuration returns early on failure; this guard handles changes afterwards)
            if (netcardIndex < 0)
            {
                netcardIndex = CaptureDeviceHelper.GetBestNetworkCardIndex(devices);
                if (netcardIndex < 0)
                {
                    AppMessageBox.ShowMessage("Network adapter details changed. Auto-selection failed; please configure the adapter manually in Settings.", this);
                    return;
                }
                else
                {
                    AppMessageBox.ShowMessage("Network adapter details changed. A new adapter was selected automatically; if issues persist, please reconfigure manually.", this);
                }
            }

            // Lock in the chosen adapter
            SelectedDevice = devices[netcardIndex];
            if (SelectedDevice == null)
            {
                AppMessageBox.ShowMessage($"Failed to acquire the network adapter. [Index]Name: [{netcardIndex}]{netcardName}", this);
                return;
            }

            // Open the device and begin capture — configure callbacks and filters
            SelectedDevice.Open(new DeviceConfiguration
            {
                Mode = DeviceModes.Promiscuous,
                Immediate = true,
                ReadTimeout = 1000,
                BufferSize = 1024 * 1024 * 4
            });
            SelectedDevice.Filter = "ip and tcp";
            SelectedDevice.OnPacketArrival += new PacketArrivalEventHandler(Device_OnPacketArrival);
            SelectedDevice.StartCapture();

            Console.WriteLine("Begin capturing packets...");
        }

        #endregion
        #endregion

        private void HandleMouseThrough()
        {
            if (!MousePenetrationHelper.IsPenetrating(this.Handle))
            {
                // Approach O: AppConfig.Transparency now represents opacity percentage
                MousePenetrationHelper.SetMousePenetrate(this, enable: true, opacityPercent: AppConfig.Transparency);
            }
            else
            {
                MousePenetrationHelper.SetMousePenetrate(this, enable: false);
            }
        }

        private void HandleClearAllData()
        {
            DataStorage.ClearAllDpsData();

            _fullBattleTimer.Reset();
            _battleTimer.Reset();
        }

        private void HandleClearData()
        {
            DataStorage.ClearDpsData();

            _battleTimer.Reset();
        }

        private void UpdateBattleTimerText()
        {
            label_BattleTimeText.Text = TimeSpan.FromTicks(InUsingTimer.ElapsedTicks).ToString(@"hh\:mm\:ss");
        }

    }
}
