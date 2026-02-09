
using System.Windows;
using Microsoft.VisualStudio.PlatformUI;
using System;

namespace vs2026_plugin.UI.Control
{
    public partial class ApplyChangesDialog : DialogWindow
    {
        public bool SaveClicked { get; private set; }

        // save clicked event
        public event EventHandler SaveClickedEvent;

        // reject clicked event
        public event EventHandler RejectClickedEvent;

        public ApplyChangesDialog(string title, string content)
        {
            InitializeComponent();           
            Title = title;
            ContentTxt.Text = content;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveClicked = true;
            SaveClickedEvent?.Invoke(this, EventArgs.Empty);
            Close();
        }

        private void Reject_Click(object sender, RoutedEventArgs e)
        {
            SaveClicked = false;
            RejectClickedEvent?.Invoke(this, EventArgs.Empty);
            Close();
        }
    }
}

