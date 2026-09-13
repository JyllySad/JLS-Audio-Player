using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using JLS.Views;

namespace JLS
{
    public partial class App : Application
    {
        private static Mutex? _mutex = null;
        private static EventWaitHandle? _instanceWaitHandle = null;

        protected override async void OnStartup(StartupEventArgs e)
        {
            const string appName = "JLS_MediaPlayer_Unique_Mutex_ID";
            bool createdNew;

            _mutex = new Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                try
                {
                    var waitHandle = EventWaitHandle.OpenExisting(appName + "_WaitHandle");
                    waitHandle.Set();
                }
                catch {             }

                Application.Current.Shutdown();
                return;
            }

            _instanceWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, appName + "_WaitHandle");

            _ = Task.Run(() =>
            {
                while (true)
                {
                    _instanceWaitHandle.WaitOne();

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (Application.Current.MainWindow is MainWindow mainWindow)
                        {
                            mainWindow.RestoreWindowFromTray();

                            mainWindow.Topmost = true;
                            mainWindow.Topmost = false;
                        }
                    });
                }
            });

            base.OnStartup(e);

            if (JLS.Properties.Settings.Default.IsFpsLimited)
            {
                int userFps = JLS.Properties.Settings.Default.TargetFps;
                if (userFps < 60) userFps = 60;

                Timeline.DesiredFrameRateProperty.OverrideMetadata(
                    typeof(Timeline),
                    new FrameworkPropertyMetadata { DefaultValue = userFps });
            }

            bool showSplash = true;

            try
            {
                string settingsPath = JLS.Services.AppPaths.AppSettingsJson;

                if (System.IO.File.Exists(settingsPath))
                {
                    string json = System.IO.File.ReadAllText(settingsPath);
                    if (json.Contains("\"ShowSplashScreen\": \"False\"") ||
                        json.Contains("\"ShowSplashScreen\": \"false\""))
                    {
                        showSplash = false;
                    }
                }
            }
            catch {     }

            if (showSplash)
            {
                SplashWindow? splash = null;

                Thread splashThread = new Thread(() =>
                {
                    splash = new SplashWindow();

                    splash.Closing += (s, args) =>
                    {
                        this.Dispatcher.Invoke(() =>
                        {
                            if (this.MainWindow != null)
                            {
                                if (this.MainWindow.WindowState == WindowState.Minimized)
                                    this.MainWindow.WindowState = WindowState.Normal;

                                this.MainWindow.Activate();
                                this.MainWindow.Focus();
                            }
                        });
                    };

                    splash.Closed += (s, args) =>
                    {
                        System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
                    };

                    splash.Show();
                    System.Windows.Threading.Dispatcher.Run();
                });

                splashThread.SetApartmentState(ApartmentState.STA);
                splashThread.IsBackground = true;
                splashThread.Start();

                while (splash == null)
                {
                    await Task.Delay(10);
                }

                var mainWindow = new MainWindow();
                this.MainWindow = mainWindow;
                mainWindow.Show();

                splash.Dispatcher.Invoke(() =>
                {
                    _ = splash.FadeOutAndClose();
                });
            }
            else
            {
                var mainWindow = new MainWindow();
                this.MainWindow = mainWindow;
                mainWindow.Show();
            }
        }
    }
}