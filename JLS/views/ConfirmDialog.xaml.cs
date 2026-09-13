using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace JLS.Views
{
    public partial class ConfirmDialog : Window
    {
        public ConfirmDialog(string title, string message, string actionText = "Delete")
        {
            InitializeComponent();
            TitleText.Text = title;
            MessageText.Text = message;
            ActionButton.Content = actionText;
        }

        public ConfirmDialog(string message) : this("Confirm", message, "Delete")
        {
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            AnimateAndClose(false);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            AnimateAndClose(true);
        }

        private void AnimateAndClose(bool result)
        {
            ActionButton.IsEnabled = false;

            var sb = (Storyboard)FindResource("ExitStoryboard");

            sb.Completed += (s, ev) =>
            {
                this.DialogResult = result;
                this.Close();       
            };

            sb.Begin();
        }
    }
}