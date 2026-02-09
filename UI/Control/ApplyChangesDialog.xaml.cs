
using System.Windows;
using Microsoft.VisualStudio.PlatformUI;

namespace vs2026_plugin.UI.Control
{
    public partial class ApplyChangesDialog : DialogWindow
    {
        public bool SaveClicked { get; private set; }

        public ApplyChangesDialog()
        {
            InitializeComponent();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveClicked = true;
            Close();
        }

        private void Reject_Click(object sender, RoutedEventArgs e)
        {
            SaveClicked = false;
            Close();
        }
    }
}

