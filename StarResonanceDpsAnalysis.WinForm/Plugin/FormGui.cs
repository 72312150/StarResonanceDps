using AntdUI;

using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace StarResonanceDpsAnalysis.WinForm.Plugin
{
    public class FormGui
    {

        public static void SetDefaultGUI(BorderlessForm BorderlessForm, bool AutoHandDpi = false)
        {
            BorderlessForm.Radius = 6; // rounded corners
            BorderlessForm.Shadow = 10; // drop-shadow size
            BorderlessForm.BorderWidth = 0; // border width
            BorderlessForm.UseDwm = false; // disable system preview
        }

        /// <summary>
        /// Apply light or dark color theme.
        /// </summary>
        /// <param name="window">Parent window.</param>
        /// <param name="isLight">Light theme flag.</param>
        public static void SetColorMode(AntdUI.BorderlessForm window, bool isLight)
        {
            if (window == null || window.IsDisposed) return;

            if (isLight)
            {
                Config.IsLight = true;
                window.BackColor = Color.White;
                window.ForeColor = Color.Black;
            }
            else
            {
                Config.IsDark = true;
                window.BackColor = Color.FromArgb(31, 31, 31);
                window.ForeColor = Color.White;
            }
        }

        /// <summary>
        /// Display a modal prompt.
        /// </summary>
        /// <param name="from"></param>
        /// <param name="title"></param>
        /// <param name="content"></param>
        /// <param name="type"></param>
        public static DialogResult Modal(Form from, string title, string content, string okText = "OK", string cancelText = "Cancel", TType type = TType.Info)
        {
            return AntdUI.Modal.open(new Modal.Config(from, title, content)
            {
                CloseIcon = true,
                Icon = type,
                CancelText = cancelText,
                OkText = okText,
                MaskClosable = false,
            });

        }
    }
}
