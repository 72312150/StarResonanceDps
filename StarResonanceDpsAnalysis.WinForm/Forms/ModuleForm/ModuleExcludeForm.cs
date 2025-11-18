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
using AntdUI.In;
using StarResonanceDpsAnalysis.WinForm.Core.Module;
using StarResonanceDpsAnalysis.WinForm.Plugin;

namespace StarResonanceDpsAnalysis.WinForm.Forms.ModuleForm
{
    public partial class ModuleExcludeForm : BorderlessForm
    {
        public ModuleExcludeForm()
        {
            InitializeComponent();
            FormGui.SetDefaultGUI(this);
        }

        private void ModuleExcludeForm_Load(object sender, EventArgs e)
        {
            FormGui.SetColorMode(this, AppConfig.IsLight); // Apply current theme colors
            AddExclusions();
        }

        private void AddExclusions()
        {
            //flowPanel1.Controls.Clear();

            foreach (var kv in ModuleMaps.MODULE_ATTR_NAMES)
            {
                var checkBox = new AntdUI.Checkbox
                {
                    Text = kv.Value,
                    Tag = kv.Key,
                    AutoSize = true,
                    // Width = 20
                };

                // Pre-select any attributes that already exist in the exclusion set
                if (BuildEliteCandidatePool.ExcludedAttributes.Contains(checkBox.Text))
                {
                    checkBox.Checked = true;
                }

                // Sync CheckedChanged with ExcludedAttributes
                checkBox.CheckedChanged += (s, e) =>
                {
                    if (checkBox.Checked)
                    {
                        BuildEliteCandidatePool.ExcludedAttributes.Add(checkBox.Text);
                    }
                    else
                    {
                        BuildEliteCandidatePool.ExcludedAttributes.Remove(checkBox.Text);
                    }
                };

                //flowPanel1.Controls.Add(checkBox);
            }




        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void TitleText_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                FormManager.ReleaseCapture();
                FormManager.SendMessage(this.Handle, FormManager.WM_NCLBUTTONDOWN, FormManager.HTCAPTION, 0);
            }
        }
    }
}
