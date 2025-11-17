using System;
using System.Runtime.InteropServices;

using StarResonanceDpsAnalysis.WinForm.Control;
using StarResonanceDpsAnalysis.WinForm.Forms.AuxiliaryForms;
using StarResonanceDpsAnalysis.WinForm.Forms.ModuleForm;

namespace StarResonanceDpsAnalysis.WinForm.Forms
{
    public class FormManager
    {
        public const string APP_NAME = "Don't Check My DPS";
        public static string AppVersion { get => $"v{Application.ProductVersion.Split('+')[0]}"; }

        public static bool IsMouseThrough { get; set; } = false;

        private static SettingsForm? _settingsForm = null;
        private static DpsStatisticsForm? _dpsStatisticsForm = null;
        private static UserUidSetForm? _userUidSetForm = null;
        private static MainForm? _mainForm = null;

        public static SettingsForm SettingsForm
        {
            get
            {
                if (_settingsForm == null || _settingsForm.IsDisposed)
                {
                    _settingsForm = new();
                }

                return _settingsForm;
            }
        }

        public static DpsStatisticsForm DpsStatistics 
        {
            get 
            {
                if (_dpsStatisticsForm == null || _dpsStatisticsForm.IsDisposed)
                {
                    _dpsStatisticsForm = new();
                }

                return _dpsStatisticsForm;
            }   
        }

        public static UserUidSetForm UserUidSetForm
        {
            get 
            {
                if (_userUidSetForm == null || _userUidSetForm.IsDisposed)
                {
                    _userUidSetForm = new();
                }

                return _userUidSetForm;
            }
        }

        public static MainForm MainForm 
        {
            get 
            {
                if (_mainForm == null || _mainForm.IsDisposed)
                {
                    _mainForm = new();
                }

                return _mainForm;
            }
        }

        private static Form[] SameSettingForms => [SettingsForm, DpsStatistics, UserUidSetForm];
        /// <summary>
        /// Apply the same TopMost setting to the shared window set
        /// </summary>
        /// <param name="topMost"></param>
        public static void SetTopMost(bool topMost) 
        {
            foreach (var form in SameSettingForms)
            {
                try
                {
                    form.TopMost = topMost;
                }
                catch (Exception) { }
            }
        }
        /// <summary>
        /// Apply a unified transparency value across shared windows
        /// </summary>
        /// <param name="opacity"></param>
        public static void FullFormTransparency(double opacity, bool force = false)
        {
            foreach (var form in SameSettingForms)
            {
                try
                {
                    if (IsMouseThrough || force)
                    {
                        form.Opacity = opacity;
                    }
                }
                catch (Exception) { }
            }
        }

        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HTCAPTION = 0x2;
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    }
}
