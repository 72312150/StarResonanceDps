using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace StarResonanceDpsAnalysis.WPF.Views
{
    /// <summary>
    /// Interaction logic for PersonalDpsView.xaml
    /// </summary>
    public partial class PersonalDpsView : Window
    {
        public PersonalDpsView()
        {
            InitializeComponent();
        }

        private void TabSelector_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton tb || !int.TryParse(tb.Tag?.ToString(), out var index)) return;

            //TODO: index == 0 ? Close() : Open();
        }

        private void ViewBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                Window.GetWindow(this)?.DragMove();
        }
    }
}
