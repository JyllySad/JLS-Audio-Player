using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Threading.Tasks;

namespace JLS.Views
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
            SetDynamicGreeting();
        }

        private void SetDynamicGreeting()
        {
            int hour = DateTime.Now.Hour;
            string greeting;

            if (hour >= 5 && hour < 12)
            {
                greeting = "Good Morning";
            }
            else if (hour >= 12 && hour < 17)
            {
                greeting = "Good Day";
            }
            else if (hour >= 17 && hour < 22)
            {
                greeting = "Good Evening";
            }
            else
            {
                greeting = "Good Night";
            }

            GreetingLabel.Text = greeting;
        }

        public async Task FadeOutAndClose()
        {
            DoubleAnimation fadeAnim = new DoubleAnimation(0, TimeSpan.FromSeconds(1));
            fadeAnim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };

            this.BeginAnimation(OpacityProperty, fadeAnim);

            await Task.Delay(1000);

            this.Close();
        }
    }
}
   