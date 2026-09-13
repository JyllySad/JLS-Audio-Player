using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JLS.Models;
using JLS.Properties;
using JLS.Services;
using Microsoft.Win32;     
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using static JLS.ViewModels.PlayHistoryViewModel;


namespace JLS.ViewModels
{
    public enum WaveformStyle
    {
        Solid,    
        Stereo,      
        Dj,         
        Mountains,
        SolidBars,
        StereoBars,
        MountainBars
    }

    public enum PlayerControlStyle
    {
        Waveform,      
        Classic,        
        Animated       
    }

    public enum RepeatMode
    {
        Off,
        All,
        One
    }

    public enum TransitionMode
    {
        Normal,
        FadeInOut,
        Crossfade
    }

    public enum WindowTitleMode
    {
        AppName,
        NowPlaying
    }

    public enum LyricsStyle
    {
        Compact,
        Beautiful
    }

    public enum BeautifulLyricsFont
    {
        Default,
        Bold
    }

    public enum VisualizerStyle
    {
        ClassicBars,
        Radial,
        MirroredMountains,
            SmoothBars,
        Oscilloscope,
        SplitSmoothBars,      
        PerspectiveRoad       
    }

    public enum TimeDisplayMode
    {
        Full,          
        Elapsed,     
        Remaining    
    }

    public enum BrowserAlternatingStyle
    {
        None,
        Columns,
        Rows,
        Both
    }

    public enum TitleBarColorMode
    {
        Default,
        FromCover,
        FromCoverInNowPlaying
    }

    public enum BottomPanelLayout
    {
        Volume_VU_Waveform,
        VU_Volume_Waveform,
        Volume_Waveform_VU,
        VU_Waveform_Volume,
        Waveform_Volume_VU,
        Waveform_VU_Volume
    }

    public enum TitleBarTextMode
    {
        AppName,
        TrackInfo,
        None
    }

    public enum ColorExtractionAlgorithm
    {
        SmartAccent,
        AverageOverallColor,
        FixedColor
    }

    public partial class HotkeyItem : ObservableObject
    {
        public int Id { get; set; }
        public string ActionName { get; set; } = string.Empty;
        public uint Modifiers { get; set; }
        public uint Key { get; set; }

        [ObservableProperty]
        private string _displayText = "None";
    }

    public partial class MainViewModel : ObservableObject
    {
        private readonly IAudioPlayer _audioPlayer = null!;
        private readonly DeviceManager _deviceManager = null!;
        private readonly double[] _vuBuffer = new double[2];

        public JLS.ViewModels.EasterEggViewModel EasterEggVM { get; } = new JLS.ViewModels.EasterEggViewModel();

        public PlaylistManager PlaylistManager { get; } = null!;
        public SavedAlbumsManager SavedAlbumsManager { get; } = null!;

        [ObservableProperty]
        private SavedAlbumsViewModel? _savedAlbumsViewModel;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasLyrics))]
        [NotifyPropertyChangedFor(nameof(ShowEnableSyncMessage))]
        [NotifyPropertyChangedFor(nameof(ShowAddSyncedLyricsMessage))]
        private string _currentLyrics = string.Empty;

        [ObservableProperty]
        private bool _isAlbumViewEnabled;

        public bool IsFileBrowserTracksSettingsAllowed => !IsAlbumViewEnabled;

        private TimeSpan _lastFluidUpdateTime = TimeSpan.Zero;

        partial void OnIsAlbumViewEnabledChanged(bool value)
        {
            JLS.Properties.Settings.Default.IsAlbumViewEnabled = value;
            JLS.Properties.Settings.Default.Save();

            if (_fileBrowser != null)
            {
                _fileBrowser.IsAlbumViewEnabled = value;
            }

            OnPropertyChanged(nameof(IsFileBrowserTracksSettingsAllowed));
        }

        [ObservableProperty]
        private WindowTitleMode _windowTitleMode = WindowTitleMode.AppName;

        public Dictionary<WindowTitleMode, string> WindowTitleModes => new()
{
    { WindowTitleMode.AppName, "App Name" },
    { WindowTitleMode.NowPlaying, "Currently Playing Track" }
};

        [ObservableProperty]
        private string _windowTitle = "JLS Audio Player";

        partial void OnWindowTitleModeChanged(WindowTitleMode value)
        {
            JLS.Properties.Settings.Default.WindowTitleMode = value.ToString();
            JLS.Properties.Settings.Default.Save();

            UpdateWindowTitle();    
        }

        private void UpdateWindowTitle()
        {
            if (WindowTitleMode == WindowTitleMode.NowPlaying && !string.IsNullOrEmpty(_currentTrackPath))
            {
                WindowTitle = $"{ArtistName} - {CurrentTrackName}";
            }
            else
            {
                WindowTitle = "JLS Audio Player";
            }
        }

        private void UpdateTitleBarVisuals()
        {
            switch (SelectedTitleBarTextMode)
            {
                case TitleBarTextMode.AppName:
                    TitleBarText = "Just Lossless Sound Audio Player";
                    break;
                case TitleBarTextMode.TrackInfo:
                    if (!string.IsNullOrEmpty(_currentTrackPath))
                        TitleBarText = $"{ArtistName} - {CurrentTrackName}";
                    else
                        TitleBarText = "Just Lossless Sound Audio Player";
                    break;
                case TitleBarTextMode.None:
                default:
                    TitleBarText = string.Empty;
                    break;
            }

            bool isNowPlayingColorActive = IsNowPlayingVisible;
            if (IsNowPlayingVisible && DateTime.Now < _titleBarEnableColorTime)
            {
                isNowPlayingColorActive = false;       
            }

            bool shouldPaint = SelectedTitleBarColorMode == TitleBarColorMode.FromCover ||
                               (SelectedTitleBarColorMode == TitleBarColorMode.FromCoverInNowPlaying && isNowPlayingColorActive) ||
                               (IsCleanModeEnabled && isNowPlayingColorActive);       

            if (shouldPaint && AlbumArt != null)
            {
                _targetTitleBarBg = _targetPrimary;       
                _targetTitleBarFg = AdaptiveTextColor == Brushes.Black ? Colors.Black : Colors.White;
            }
            else
            {
                _targetTitleBarBg = Color.FromRgb(24, 24, 24);       
                _targetTitleBarFg = Color.FromRgb(176, 176, 176);
            }
        }

        [ObservableProperty]
        private BrowserAlternatingStyle _selectedBrowserAlternatingStyle = BrowserAlternatingStyle.None;

        public Dictionary<BrowserAlternatingStyle, string> BrowserAlternatingStyles => new()
{
    { BrowserAlternatingStyle.None, "Default" },
    { BrowserAlternatingStyle.Columns, "Columns Only" },
    { BrowserAlternatingStyle.Rows, "Rows Only" },
    { BrowserAlternatingStyle.Both, "Rows & Columns" }
};

        partial void OnSelectedBrowserAlternatingStyleChanged(BrowserAlternatingStyle value)
        {
            Settings.Default.BrowserAlternatingStyle = value.ToString();
            Settings.Default.Save();
        }

        [ObservableProperty]
        private BrowserAlternatingStyle _selectedPlaylistAlternatingStyle = BrowserAlternatingStyle.None;

        partial void OnSelectedPlaylistAlternatingStyleChanged(BrowserAlternatingStyle value)
        {
            Settings.Default.PlaylistAlternatingStyle = value.ToString();
            Settings.Default.Save();
        }

        [ObservableProperty]
        private bool _isLyricsEditorOpen = false;

        [ObservableProperty]
        private string _editableLyrics = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowEnableSyncMessage))]
        [NotifyPropertyChangedFor(nameof(ShowAddSyncedLyricsMessage))]
        private bool _isSyncedLyricsMode;       

        public bool ShowAddSyncedLyricsMessage => !string.IsNullOrWhiteSpace(CurrentLyrics) && (SyncedLyrics == null || SyncedLyrics.Count == 0) && IsSyncedLyricsMode;

        [ObservableProperty]
        private ObservableCollection<LyricLine> _syncedLyrics = new();       

        [ObservableProperty]
        private ObservableCollection<LyricLine> _editorSyncedLyrics = new();       

        partial void OnIsSyncedLyricsModeChanged(bool value)
        {
            if (IsLyricsEditorOpen)
            {
                if (value)
                {
                    string syncedRaw = JLS.Services.LyricsService.GetSyncedLyrics(_currentTrackPath);
                    SyncedLyrics.Clear();
                    ParseLrcToCollection(syncedRaw);

                    PrepareEditorLyricsGrid();
                    EvaluateCountdown();
                }
                else
                {
                    EditableLyrics = JLS.Services.LyricsService.GetPlainLyrics(_currentTrackPath);
                }
            }

            OnPropertyChanged(nameof(IsExportReady));

        }

        public event EventHandler<LyricLine>? ActiveLyricChanged;
        private LyricLine? _currentActiveLyric;
        private LyricLine? _currentActiveEditorLyric;

        [RelayCommand]
        private void SeekToLyric(double timestamp)
        {
            CurrentPosition = timestamp;
        }

        [RelayCommand]
        private void OpenLyricsEditor()
        {
            EditableLyrics = CurrentLyrics;

            if (IsSyncedLyricsMode)
            {
                PrepareEditorLyricsGrid();
            }

            IsLyricsEditorOpen = true;
            IsLyricsOptionsMenuOpen = false;
        }

        [RelayCommand]
        private void CloseLyricsEditor()
        {
            IsLyricsEditorOpen = false;
        }

        [RelayCommand]
        private void SaveLyrics()
        {
            if (string.IsNullOrEmpty(_currentTrackPath)) return;

            JLS.Services.LyricsService.SaveSyncedModePreference(_currentTrackPath, IsSyncedLyricsMode);

            if (IsSyncedLyricsMode)
            {
                var activeLines = EditorSyncedLyrics.Where(l => !string.IsNullOrWhiteSpace(l.TimestampText)).ToList();

                string syncedTextToSave = string.Join(Environment.NewLine, activeLines.Select(l =>
                    $"{l.TimestampText} {l.Text.Replace("\r\n", "\\n").Replace("\n", "\\n")}"));

                JLS.Services.LyricsService.SaveSyncedLyrics(_currentTrackPath, syncedTextToSave);

                SyncedLyrics = new ObservableCollection<LyricLine>(activeLines);
                EvaluateCountdown();
            }
            else
            {
                JLS.Services.LyricsService.SavePlainLyrics(_currentTrackPath, EditableLyrics);
                CurrentLyrics = EditableLyrics;
            }

            IsLyricsEditorOpen = false;

            OnPropertyChanged(nameof(HasLyrics));
            OnPropertyChanged(nameof(ShowEnableSyncMessage));
            OnPropertyChanged(nameof(ShowAddSyncedLyricsMessage));
        }

        [RelayCommand]
        private void ExportLrcFile()
        {
            if (string.IsNullOrEmpty(_currentTrackPath)) return;

            var activeLines = EditorSyncedLyrics.Where(l => !string.IsNullOrWhiteSpace(l.TimestampText)).ToList();
            if (activeLines.Count == 0) return;

            string defaultName;
            if (!string.IsNullOrWhiteSpace(ArtistName) && ArtistName != "Unknown Artist" &&
                !string.IsNullOrWhiteSpace(CurrentTrackName) && CurrentTrackName != "No track selected")
            {
                defaultName = $"{CurrentTrackName} - {ArtistName}.lrc";
            }
            else
            {
                defaultName = Path.GetFileNameWithoutExtension(_currentTrackPath) + ".lrc";
            }

            foreach (var c in Path.GetInvalidFileNameChars())
            {
                defaultName = defaultName.Replace(c.ToString(), "_");
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export .lrc file",
                Filter = "LRC Lyrics (*.lrc)|*.lrc",
                FileName = defaultName
            };

            if (dialog.ShowDialog() == true)
            {
                string syncedTextToSave = string.Join(Environment.NewLine, activeLines.Select(l =>
                    $"{l.TimestampText} {l.Text.Replace("\r\n", "\\n").Replace("\n", "\\n")}"));

                try
                {
                    File.WriteAllText(dialog.FileName, syncedTextToSave);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Export error: {ex.Message}");
                }
            }
        }

        [RelayCommand]
        private void SearchLyricsOnWeb()
        {
            try
            {
                string searchSuffix = IsSyncedLyricsMode ? ".lrc" : "lyrics";

                string query = Uri.EscapeDataString($"{ArtistName} {CurrentTrackName} {searchSuffix}");
                string url = $"https://www.google.com/search?q={query}";

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Ошибка при открытии браузера: " + ex.Message);
            }
        }

        public bool HasLyrics => !string.IsNullOrWhiteSpace(CurrentLyrics) || (SyncedLyrics != null && SyncedLyrics.Count > 0);

        public bool ShowEnableSyncMessage => string.IsNullOrWhiteSpace(CurrentLyrics) && SyncedLyrics != null && SyncedLyrics.Count > 0 && !IsSyncedLyricsMode;

        public bool IsExportReady => IsSyncedLyricsMode && EditorSyncedLyrics.Count > 0;

        [ObservableProperty]
        private StatisticsViewModel _statistics = new StatisticsViewModel();

        [ObservableProperty]
        private bool _isInfoPanelVisible = false;

        [RelayCommand]
        private void OpenInfoPanel()
        {
            IsInfoPanelVisible = true;
            IsSettingsVisible = false;       

            Statistics.LoadStatistics();

            UpdateActiveSidebarItem();
        }

        [RelayCommand]
        private void CloseInfoPanel()
        {
            IsInfoPanelVisible = false;
            UpdateActiveSidebarItem();
        }

        [ObservableProperty]
        private bool _isStatisticsTabSelected = true;

        [ObservableProperty]
        private bool _isInfoTabSelected = false;

        [RelayCommand]
        private void SelectStatisticsTab()
        {
            IsStatisticsTabSelected = true;
            IsInfoTabSelected = false;
        }

        [RelayCommand]
        private void SelectInfoTab()
        {
            IsStatisticsTabSelected = false;
            IsInfoTabSelected = true;
        }

        [ObservableProperty]
        private bool _isTrackInfoOpen = false;

        [ObservableProperty]
        private string _trackYear = "Unknown Year";

        [ObservableProperty]
        private string _trackFileSize = "0 MB";

        [ObservableProperty]
        private string _trackFullPath = string.Empty;

        [ObservableProperty]
        private string _genre = "Unknown Genre";

        [ObservableProperty]
        private string _audioFormat = "---";

        [ObservableProperty]
        private string _audioBitDepth = "---";

        [ObservableProperty]
        private string _audioSampleRate = "---";

        [ObservableProperty]
        private string _audioBitrate = "---";

        [ObservableProperty]
        private string _trackNumber = "---";

        [ObservableProperty]
        private bool _isAudioFormatVisible = false;

        [ObservableProperty]
        private bool _isAudioBitDepthVisible = false;

        [ObservableProperty]
        private bool _isAudioSampleRateVisible = false;

        [ObservableProperty]
        private bool _isAudioBitrateVisible = false;

        public BulkTrackInfoViewModel BulkTrackInfo { get; } = new BulkTrackInfoViewModel();

        [RelayCommand]
        private void OpenTrackInfo()
        {
            IsTrackInfoOpen = true;
            IsLyricsOptionsMenuOpen = false;
        }

        [RelayCommand]
        private void CloseTrackInfo()
        {
            IsTrackInfoOpen = false;
        }

        [ObservableProperty]
        private ObservableCollection<AudioDevice> _availableDevices;

        [ObservableProperty]
        private AudioDevice? _selectedDevice;

        public bool IsWasapiOrAsio => SelectedDevice != null && SelectedDevice.DriverType != "Standard";

        public bool IsExplorerActive => CurrentContent is FileBrowserViewModel;
        public bool IsPlaylistsActive => CurrentContent is PlaylistsViewModel;
        public bool IsSavedAlbumsActive => CurrentContent is SavedAlbumsViewModel;

        private void RefreshTabVisibility()
        {
            OnPropertyChanged(nameof(IsExplorerActive));
            OnPropertyChanged(nameof(IsPlaylistsActive));
            OnPropertyChanged(nameof(IsSavedAlbumsActive));
        }



        [ObservableProperty]
        private string _currentTrackName = "No track selected";

        private string _currentTrackPath = string.Empty;

        [ObservableProperty]
        private FileBrowserViewModel? _fileBrowser;

        [ObservableProperty]
        private PlaylistsViewModel? _playlistsViewModel;

        private ObservableCollection<FileSystemItem> _currentPlaylist = new();

        [ObservableProperty]
        private double _volume = 0.5;     

        [ObservableProperty]
        private bool _isDraggingTrack;

        [ObservableProperty]
        private double _currentPosition = 0;      

        [ObservableProperty]
        private double _totalDuration = 1;            

        private DispatcherTimer? _resizeTimer;

        private JLS.Views.OsdWindow? _osdWindow;

        private bool _isAppStarting = true;       

        private string _lastTrackedPath = string.Empty;

        private double _lastRenderedPosition = 0;
        private bool _isInternalSeekUpdate = false;
        private DateTime _lastUserSeekTime = DateTime.MinValue;

        private TimeSpan _lastFrameTime = TimeSpan.Zero;
        private TimeSpan _lastUiUpdateTime = TimeSpan.Zero;
        private double _precisePosition = 0;     
        private double _actualSecondsListened = 0;
        private double _targetSeekPosition = -1;
        private double _dragTargetPosition = -1;
        private bool _isCatchingUp = false;

        private double _lastRenderedVolume = 0.5;
        private bool _isInternalVolumeUpdate = false;
        private DateTime _lastUserVolumeTime = DateTime.MinValue;
        private double _targetVolume = 0.5;

        [ObservableProperty]
        private bool _isPlaying;

        [ObservableProperty]
        private ImageSource? _albumArt;

        [ObservableProperty]
        private string _timeDisplay = "00:00 / 00:00";

        [ObservableProperty]
        private TimeDisplayMode _timeDisplayMode = TimeDisplayMode.Full;

        public Array TimeDisplayModes => Enum.GetValues(typeof(TimeDisplayMode));

        public TrackInfoViewModel TrackInfo { get; } = new TrackInfoViewModel();

        public PlayHistoryViewModel PlayHistoryVM { get; } = new PlayHistoryViewModel();

        [RelayCommand]
        private void OpenPlayHistory()
        {
            var historyCopy = _playHistory.AsEnumerable().Reverse().ToList();
            PlayHistoryVM.Open(historyCopy);
        }

        private double _savedPositionBeforeTagEdit = 0;
        private bool _wasPlayingBeforeTagEdit = false;

        [RelayCommand]
        private void ShowTrackInfo(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            IsLyricsOptionsMenuOpen = false;

            TrackInfo.Open(filePath);
        }

        [RelayCommand]
        private void OpenTrackInfoFromTray()
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.RestoreWindowFromTray();
            }

            if (!string.IsNullOrEmpty(TrackFullPath))
            {
                ShowTrackInfo(TrackFullPath);
            }
        }

        partial void OnTimeDisplayModeChanged(TimeDisplayMode value)
        {
            

            _lastTimeDisplayCache = string.Empty;       
            _lastTenthsCache = -1;
            UpdateTimeDisplay();
        }

        [RelayCommand]
        private void ToggleTimeDisplayMode()
        {
            int current = (int)TimeDisplayMode;
            current = (current + 1) % 3;
            TimeDisplayMode = (TimeDisplayMode)current;
        }

        [ObservableProperty]
        private string _artistName = "";

        [ObservableProperty]
        private string _albumName = "";

        [ObservableProperty]
        private string _technicalInfo = "--- | -- kHz | --- kbps | -- bit";

        [ObservableProperty]
        private bool _isSoundSettingsSelected = true;

        [ObservableProperty]
        private bool _isHotkeysSettingsSelected = false;

        [ObservableProperty]
        private bool _isMiniPlayerEnabled = false;

        partial void OnIsMiniPlayerEnabledChanged(bool value)
        {
            JLS.Properties.Settings.Default.IsMiniPlayerEnabled = value;
            JLS.Properties.Settings.Default.Save();
        }

        [ObservableProperty]
        private bool _isMiniPlayerActive = false;

        [RelayCommand]
        private void RestoreFromMiniPlayer()
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.RestoreWindowFromTray();
            }
        }

        [RelayCommand]
        private void SelectSoundSettings()
        {
            IsSoundSettingsSelected = true;
            IsHotkeysSettingsSelected = false;
        }

        [RelayCommand]
        private void SelectInterfaceSettings()
        {
            IsSoundSettingsSelected = false;
            IsHotkeysSettingsSelected = false;
        }

        [RelayCommand]
        private void SelectHotkeysSettings()
        {
            IsSoundSettingsSelected = false;
            IsHotkeysSettingsSelected = true;
        }

        [ObservableProperty]
        private RepeatMode _repeatMode = RepeatMode.Off;

        [ObservableProperty]
        private bool _isShuffleActive;

        
        [ObservableProperty]
        private Brush _primaryColor = Brushes.White;

        [ObservableProperty]
        private Brush _lightColor = Brushes.White;

        [ObservableProperty]
        private Brush _darkColor = new SolidColorBrush(Color.FromRgb(45, 45, 48));

        
        [ObservableProperty]
        private Brush _bottomPanelBackground = new SolidColorBrush(Color.FromRgb(20, 20, 20));

        [ObservableProperty]
        private Brush _adaptiveTextColor = Brushes.White;

        [ObservableProperty]
        private string _currentTimeSimple = "00:00";     

        [ObservableProperty]
        private string _totalTimeSimple = "00:00";       

        [ObservableProperty]
        private ColorExtractionAlgorithm _selectedColorExtractionAlgorithm = ColorExtractionAlgorithm.SmartAccent;

        public Dictionary<ColorExtractionAlgorithm, string> ColorExtractionAlgorithms => new()
        {
            { ColorExtractionAlgorithm.SmartAccent, "Smart Accent" },
            { ColorExtractionAlgorithm.AverageOverallColor, "Average Color" },
            { ColorExtractionAlgorithm.FixedColor, "Fixed Color" }
        };

        [ObservableProperty]
        private Color _fixedPrimaryColor = Color.FromRgb(100, 150, 200);      

        [ObservableProperty]
        private double _fixedColorHue = 210;     

        [ObservableProperty]
        private double _fixedColorSaturation = 0.475;     

        [ObservableProperty]
        private double _fixedColorLightness = 0.588;     

        [ObservableProperty]
        private bool _isFixedColorPopupOpen;

        private double _backupFixedHue;
        private double _backupFixedSaturation;
        private double _backupFixedLightness;

        partial void OnFixedColorHueChanged(double value) => UpdateFixedColors();
        partial void OnFixedColorSaturationChanged(double value) => UpdateFixedColors();
        partial void OnFixedColorLightnessChanged(double value) => UpdateFixedColors();

        [RelayCommand]
        private void OpenFixedColorPopup()
        {
            UpdateHslFromRgb(FixedPrimaryColor);
            
            _backupFixedHue = FixedColorHue;
            _backupFixedSaturation = FixedColorSaturation;
            _backupFixedLightness = FixedColorLightness;
            
            IsFixedColorPopupOpen = true;
        }

        [RelayCommand]
        private void SaveFixedColor()
        {
            IsFixedColorPopupOpen = false;
        }

        [RelayCommand]
        private void CancelFixedColor()
        {
            FixedColorHue = _backupFixedHue;
            FixedColorSaturation = _backupFixedSaturation;
            FixedColorLightness = _backupFixedLightness;
            
            IsFixedColorPopupOpen = false;
        }

        private void UpdateFixedColors()
        {
            if (!IsFixedColorPopupOpen) return; 

            FixedPrimaryColor = HslToRgb(FixedColorHue, FixedColorSaturation, FixedColorLightness);
        }

        private void UpdateHslFromRgb(Color color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));

            double h = 0, s = 0, l = (max + min) / 2.0;

            if (max != min)
            {
                double d = max - min;
                s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

                if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
                else if (max == g) h = (b - r) / d + 2;
                else if (max == b) h = (r - g) / d + 4;

                h *= 60;
                if (h < 0) h += 360;
            }

            FixedColorHue = h;
            FixedColorSaturation = s;
            FixedColorLightness = l;

            OnPropertyChanged(nameof(FixedColorHue));
            OnPropertyChanged(nameof(FixedColorSaturation));
            OnPropertyChanged(nameof(FixedColorLightness));
        }

        private static Color HslToRgb(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = l - c / 2;

            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        public Brush FixedPrimaryColorBrush => new SolidColorBrush(FixedPrimaryColor);

        public bool IsFixedColorSelected => SelectedColorExtractionAlgorithm == ColorExtractionAlgorithm.FixedColor;

        partial void OnFixedPrimaryColorChanged(Color value)
        {
            OnPropertyChanged(nameof(FixedPrimaryColorBrush));   

            JLS.Properties.Settings.Default.FixedPrimaryColor = value.ToString();
            JLS.Properties.Settings.Default.Save();

            if (!string.IsNullOrEmpty(_currentTrackPath))
            {
                UpdateMetadata(_currentTrackPath, forceUpdate: true);
                _fileBrowser?.RefreshAlbumColors();
            }
        }

        partial void OnSelectedColorExtractionAlgorithmChanged(ColorExtractionAlgorithm value)
        {
            OnPropertyChanged(nameof(IsFixedColorSelected));   

            JLS.Properties.Settings.Default.ColorExtractionAlgorithm = value.ToString();
            JLS.Properties.Settings.Default.Save();

            if (!string.IsNullOrEmpty(_currentTrackPath))
            {
                UpdateMetadata(_currentTrackPath, forceUpdate: true);
                _fileBrowser?.RefreshAlbumColors();
                _fileBrowser?.UpdateItemMetadata(_currentTrackPath);
                _playlistsViewModel?.UpdateItemMetadata(_currentTrackPath);
                _savedAlbumsViewModel?.UpdateItemMetadata(_currentTrackPath);
            }
        }

        

        private Color _currentTitleBarBg = Color.FromRgb(24, 24, 24);
        private Color _targetTitleBarBg = Color.FromRgb(24, 24, 24);
        private Color _currentTitleBarFg = Color.FromRgb(176, 176, 176);
        private Color _targetTitleBarFg = Color.FromRgb(176, 176, 176);
        private DateTime _titleBarEnableColorTime = DateTime.MinValue;

        private Color _targetPrimary = Colors.White;
        private Color _targetLight = Colors.White;
        private Color _targetDark = Color.FromRgb(45, 45, 48);
        private Color _targetPanel = Color.FromRgb(20, 20, 20);

        private Color _currentPrimary = Colors.White;
        private Color _currentLight = Colors.White;
        private Color _currentDark = Color.FromRgb(45, 45, 48);
        private Color _currentPanel = Color.FromRgb(20, 20, 20);

        [ObservableProperty]
        private double _vuLeft = 0;

        [ObservableProperty]
        private double _vuRight = 0;

        [ObservableProperty]
        private bool _isVuMetersFrozen = false;
        private CancellationTokenSource? _vuFreezeCts;

        [ObservableProperty]
        private bool _isNowPlayingFluidBackgroundEnabled = true;

        partial void OnIsNowPlayingFluidBackgroundEnabledChanged(bool value)
        {
            JLS.Properties.Settings.Default.IsNowPlayingFluidBackgroundEnabled = value;
            JLS.Properties.Settings.Default.Save();

            if (!value)
            {
                IsNowPlayingFluidAudioReactiveEnabled = false;
            }
        }

        [ObservableProperty]
        private bool _isNowPlayingFluidAudioReactiveEnabled = true;

        partial void OnIsNowPlayingFluidAudioReactiveEnabledChanged(bool value)
        {
            JLS.Properties.Settings.Default.IsNowPlayingFluidAudioReactiveEnabled = value;
            JLS.Properties.Settings.Default.Save();
        }

        [ObservableProperty]
        private double _fluidTime = 0;

        [ObservableProperty]
        private double _fluidIntensity = 0;

        [ObservableProperty]
        private double _fluidDeformation = 0;

        [ObservableProperty]
        private double _fluidFlash = 0;

        private double _smoothedBass = 0.0;

        private double _currentDeformation = 0.0;

        private double _macroEnergy = 0.0;          
        private double _microEnergy = 0.0;          
        private double _beatConfidence = 0.0;        
        private DateTime _lastDropTime = DateTime.MinValue;
        private double _currentFlash = 0.0;

        [ObservableProperty]
        private double _fluidRandomSeed = 0;      
        private double _currentFluidSpeed = 0.03;      

        [ObservableProperty]
        private bool _isCleanModeEnabled = false;

        partial void OnIsCleanModeEnabledChanged(bool value)
        {
            JLS.Properties.Settings.Default.IsCleanModeEnabled = value;
            JLS.Properties.Settings.Default.Save();
        }

        [ObservableProperty]
        private bool _isAlwaysShowTrackTimingsEnabled = false;

        partial void OnIsAlwaysShowTrackTimingsEnabledChanged(bool value)
        {
            JLS.Properties.Settings.Default.IsAlwaysShowTrackTimingsEnabled = value;
            JLS.Properties.Settings.Default.Save();
        }

        [ObservableProperty]
        private bool _isSmoothScrollEnabled = JLS.Properties.Settings.Default.IsSmoothScrollEnabled;

        partial void OnIsSmoothScrollEnabledChanged(bool value)
        {
            JLS.Properties.Settings.Default.IsSmoothScrollEnabled = value;
            JLS.Properties.Settings.Default.Save();
        }

        [ObservableProperty]
        private bool _isFullscreenF10Enabled = JLS.Properties.Settings.Default.IsFullscreenF10Enabled;

        partial void OnIsFullscreenF10EnabledChanged(bool value)
        {
            JLS.Properties.Settings.Default.IsFullscreenF10Enabled = value;
            JLS.Properties.Settings.Default.Save();
        }

        private CancellationTokenSource? _waveformCts;      
        private readonly Dictionary<string, (float[] Left, float[] Right)> _waveformCache = new();   

        [ObservableProperty]
        private float[]? _leftWaveform;

        [ObservableProperty]
        private float[]? _rightWaveform;

        [ObservableProperty]
        private double _waveformProgress;        

        [ObservableProperty]
        private string _currentTimeOnly = "00:00.0";      

        [ObservableProperty]
        private double _bubbleX;     

        [ObservableProperty]
        private double _waveformWidth;     

        [ObservableProperty]
        private PlayerControlStyle _selectedControlStyle = PlayerControlStyle.Waveform;

        [ObservableProperty]
        private PlayerControlStyle _nowPlayingControlStyle = PlayerControlStyle.Waveform;

        [ObservableProperty]
        private WaveformStyle _nowPlayingWaveformStyle = WaveformStyle.Dj;

        [ObservableProperty]
        private TitleBarColorMode _selectedTitleBarColorMode = TitleBarColorMode.Default;

        [ObservableProperty]
        private TitleBarTextMode _selectedTitleBarTextMode = TitleBarTextMode.AppName;

        [ObservableProperty]
        private Brush _titleBarBackground = new SolidColorBrush(Color.FromRgb(24, 24, 24));    

        [ObservableProperty]
        private Brush _titleBarForeground = new SolidColorBrush(Color.FromRgb(176, 176, 176));    

        [ObservableProperty]
        private string _titleBarText = "Just Lossless Sound Audio Player";

        public Dictionary<TitleBarColorMode, string> TitleBarColorModes => new()
{
    { TitleBarColorMode.Default, "Default (Gray)" },
    { TitleBarColorMode.FromCover, "Always Color from Cover Art" },
    { TitleBarColorMode.FromCoverInNowPlaying, "Color from Cover in Now Playing Only" }
};

        public Dictionary<TitleBarTextMode, string> TitleBarTextModes => new()
{
    { TitleBarTextMode.AppName, "App Name" },
    { TitleBarTextMode.TrackInfo, "Currently Playing Track" },
    { TitleBarTextMode.None, "Clear" }
};

        partial void OnSelectedTitleBarColorModeChanged(TitleBarColorMode value)
        {
            Settings.Default.SelectedTitleBarColorMode = value.ToString();
            Settings.Default.Save();
        }

        partial void OnSelectedTitleBarTextModeChanged(TitleBarTextMode value)
        {
            Settings.Default.SelectedTitleBarTextMode = value.ToString();
            Settings.Default.Save();
        }

        

        [ObservableProperty]
        private bool _isGlobalHotkeysEnabled = true;

        [ObservableProperty]
        private bool _isMediaKeysEnabled = true;    

        partial void OnIsMediaKeysEnabledChanged(bool value)
        {
            JLS.Properties.Settings.Default.IsMediaKeysEnabled = value;
            JLS.Properties.Settings.Default.Save();

            SetupAllHotkeys();
        }

        partial void OnIsGlobalHotkeysEnabledChanged(bool value)
        {
            Settings.Default.GlobalHotkeysEnabled = value;
            Settings.Default.Save();

            if (value) SetupAllHotkeys();
            else (Application.Current.MainWindow as MainWindow)?.UnregisterGlobalHotkeys();
        }

        partial void OnNowPlayingControlStyleChanged(PlayerControlStyle value)
        {
            Settings.Default.NowPlayingControlStyle = value.ToString();
            Settings.Default.Save();

            if (value == PlayerControlStyle.Waveform && LeftWaveform == null && !string.IsNullOrEmpty(_currentTrackPath))
            {
                GenerateWaveform(_currentTrackPath);
            }

            OnPropertyChanged(nameof(IsNowPlayingWaveformStyleEnabled));
        }

        partial void OnNowPlayingWaveformStyleChanged(WaveformStyle value)
        {
            Settings.Default.NowPlayingWaveformStyle = value.ToString();
            Settings.Default.Save();
        }

        [ObservableProperty]
        private bool _isFullscreenVisualizerEnabled = true;

        partial void OnIsFullscreenVisualizerEnabledChanged(bool value)
        {
            Settings.Default.IsFullscreenVisualizerEnabled = value;
            Settings.Default.Save();
            OnPropertyChanged(nameof(IsVisualizerStyleEnabled));
        }

        [ObservableProperty]
        private VisualizerStyle _selectedVisualizerStyle = VisualizerStyle.ClassicBars;

        public Dictionary<VisualizerStyle, string> VisualizerStyles
        {
            get
            {
                var styles = new Dictionary<VisualizerStyle, string>
        {
            { VisualizerStyle.ClassicBars, "Classic Bars" },
            { VisualizerStyle.Radial, "Radial Spectrum" },
            { VisualizerStyle.MirroredMountains, "Mirrored Mountains" },
            { VisualizerStyle.SmoothBars, "Smooth Bars" }
        };

                if (IsWasapiOrAsio)
                {
                    styles.Add(VisualizerStyle.Oscilloscope, "Oscilloscope Wave");
                }

                styles.Add(VisualizerStyle.SplitSmoothBars, "Split Smooth Bars");
                styles.Add(VisualizerStyle.PerspectiveRoad, "Perspective Road");

                return styles;
            }
        }

        partial void OnSelectedVisualizerStyleChanged(VisualizerStyle value)
        {
            Settings.Default.VisualizerStyle = value.ToString();
            Settings.Default.Save();
        }

        [ObservableProperty]
        private bool _isOverlayVisualizerEnabled = true;

        partial void OnIsOverlayVisualizerEnabledChanged(bool value)
        {
            Settings.Default.IsOverlayVisualizerEnabled = value;
            Settings.Default.Save();

            if (!value) IsOverlayVisualizerVisible = false;

            OnPropertyChanged(nameof(IsVisualizerStyleEnabled));
        }

        [ObservableProperty]
        private bool _isOverlayVisualizerVisible = false;

        [RelayCommand]
        private void ToggleOverlayVisualizer()
        {
            if (IsOverlayVisualizerEnabled)
            {
                IsOverlayVisualizerVisible = !IsOverlayVisualizerVisible;
            }
        }

        [ObservableProperty]
        private float[]? _fftData;

        [ObservableProperty]
        private float[]? _stereoFftData;

        [ObservableProperty]
        private float[]? _waveData;

        public Array ControlStyles => Enum.GetValues(typeof(PlayerControlStyle));

        [ObservableProperty]
        private bool _isFpsLimited = false;       

        [ObservableProperty]
        private bool _isOptimizedWindowModeEnabled = false;

        partial void OnIsOptimizedWindowModeEnabledChanged(bool value)
        {
            Settings.Default.IsOptimizedWindowModeEnabled = value;
            Settings.Default.Save();
        }

        partial void OnIsFpsLimitedChanged(bool value)
        {
            Settings.Default.IsFpsLimited = value;
            Settings.Default.Save();
        }

        [ObservableProperty]
        private int _targetFps = 60;    

        partial void OnTargetFpsChanged(int value)
        {
            Settings.Default.TargetFps = value;
            Settings.Default.Save();
        }

        [RelayCommand]
        private void IncreaseFps()
        {
            if (TargetFps < 360) TargetFps += 10;
        }

        [RelayCommand]
        private void DecreaseFps()
        {
            if (TargetFps > 60) TargetFps -= 10;
        }

        [ObservableProperty]
        private BottomPanelLayout _selectedBottomLayout = BottomPanelLayout.Volume_VU_Waveform;

        public enum VuMeterStyle
        {
            Split,        
            Minimal       
        }

        public Dictionary<BottomPanelLayout, string> BottomPanelLayouts => new()
{
    { BottomPanelLayout.Volume_VU_Waveform, "Volume - VU - Waveform" },
    { BottomPanelLayout.VU_Volume_Waveform, "VU - Volume - Waveform" },
    { BottomPanelLayout.Volume_Waveform_VU, "Volume - Waveform - VU" },
    { BottomPanelLayout.VU_Waveform_Volume, "VU - Waveform - Volume" },
    { BottomPanelLayout.Waveform_Volume_VU, "Waveform - Volume - VU" },
    { BottomPanelLayout.Waveform_VU_Volume, "Waveform - VU - Volume" }
};

        [ObservableProperty] private int _volumeColumn;
        [ObservableProperty] private int _vuMeterColumn;
        [ObservableProperty] private int _waveformColumn;

        [ObservableProperty] private GridLength _column0Width;
        [ObservableProperty] private GridLength _column1Width;
        [ObservableProperty] private GridLength _column2Width;

        partial void OnSelectedBottomLayoutChanged(BottomPanelLayout value)
        {
            JLS.Properties.Settings.Default.BottomPanelLayout = value.ToString();
            JLS.Properties.Settings.Default.Save();
            UpdateLayoutColumns();
        }

        private void UpdateLayoutColumns()
        {
            switch (SelectedBottomLayout)
            {
                case BottomPanelLayout.Volume_VU_Waveform:
                    VolumeColumn = 0; VuMeterColumn = 1; WaveformColumn = 2;
                    Column0Width = GridLength.Auto; Column1Width = GridLength.Auto; Column2Width = new GridLength(1, GridUnitType.Star);
                    break;
                case BottomPanelLayout.VU_Volume_Waveform:
                    VuMeterColumn = 0; VolumeColumn = 1; WaveformColumn = 2;
                    Column0Width = GridLength.Auto; Column1Width = GridLength.Auto; Column2Width = new GridLength(1, GridUnitType.Star);
                    break;
                case BottomPanelLayout.Volume_Waveform_VU:
                    VolumeColumn = 0; WaveformColumn = 1; VuMeterColumn = 2;
                    Column0Width = GridLength.Auto; Column1Width = new GridLength(1, GridUnitType.Star); Column2Width = GridLength.Auto;
                    break;
                case BottomPanelLayout.VU_Waveform_Volume:
                    VuMeterColumn = 0; WaveformColumn = 1; VolumeColumn = 2;
                    Column0Width = GridLength.Auto; Column1Width = new GridLength(1, GridUnitType.Star); Column2Width = GridLength.Auto;
                    break;
                case BottomPanelLayout.Waveform_Volume_VU:
                    WaveformColumn = 0; VolumeColumn = 1; VuMeterColumn = 2;
                    Column0Width = new GridLength(1, GridUnitType.Star); Column1Width = GridLength.Auto; Column2Width = GridLength.Auto;
                    break;
                case BottomPanelLayout.Waveform_VU_Volume:
                    WaveformColumn = 0; VuMeterColumn = 1; VolumeColumn = 2;
                    Column0Width = new GridLength(1, GridUnitType.Star); Column1Width = GridLength.Auto; Column2Width = GridLength.Auto;
                    break;
            }
        }

        [ObservableProperty]
        private bool _isVuMeterVisible = true;

        [ObservableProperty]
        private VuMeterStyle _selectedVuMeterStyle = VuMeterStyle.Split;

        [ObservableProperty]
        private bool _isAutoHideControlsEnabled = false;

        partial void OnIsAutoHideControlsEnabledChanged(bool value)
        {
            JLS.Properties.Settings.Default.IsAutoHideControlsEnabled = value;
            JLS.Properties.Settings.Default.Save();
        }

        [ObservableProperty]
        private bool _isPlaylistMinimalModeEnabled;

        public bool IsPlaylistTracksSettingsAllowed => !IsPlaylistMinimalModeEnabled;

        partial void OnIsPlaylistMinimalModeEnabledChanged(bool value)
        {
            JLS.Properties.Settings.Default.IsPlaylistMinimalModeEnabled = value;
            JLS.Properties.Settings.Default.Save();

            OnPropertyChanged(nameof(IsPlaylistTracksSettingsAllowed));
        }

        [ObservableProperty]
        private bool _isMinimalBottomPanelEnabled = false;

        [ObservableProperty]
        private bool _isGlassModeEnabled = false;    

        partial void OnIsGlassModeEnabledChanged(bool value)
        {
            JLS.Properties.Settings.Default.IsGlassModeEnabled = value;
            JLS.Properties.Settings.Default.Save();
        }

        public bool IsBottomPanelAdvancedSettingsAllowed => !IsMinimalBottomPanelEnabled;

        public bool IsVisualizerStyleEnabled => IsFullscreenVisualizerEnabled || IsOverlayVisualizerEnabled;
        public bool IsVuMeterStyleEnabled => IsBottomPanelAdvancedSettingsAllowed && IsVuMeterVisible;
        public bool IsLayoutOrderEnabled => IsBottomPanelAdvancedSettingsAllowed && !(!IsVuMeterVisible && IsHorizontalVolumeEnabled);

        public bool IsBottomPanelWaveformStyleEnabled =>
            IsBottomPanelAdvancedSettingsAllowed && SelectedControlStyle == PlayerControlStyle.Waveform;

        public bool IsNowPlayingWaveformStyleEnabled =>
            NowPlayingControlStyle == PlayerControlStyle.Waveform;

        public bool IsBeautifulLyricsFontEnabled => SelectedLyricsStyle == LyricsStyle.Beautiful;

        partial void OnIsMinimalBottomPanelEnabledChanged(bool value)
        {
            JLS.Properties.Settings.Default.IsMinimalBottomPanelEnabled = value;
            JLS.Properties.Settings.Default.Save();

            OnPropertyChanged(nameof(IsBottomPanelAdvancedSettingsAllowed));

            OnPropertyChanged(nameof(IsBottomPanelWaveformStyleEnabled));

            OnPropertyChanged(nameof(IsVuMeterStyleEnabled));
            OnPropertyChanged(nameof(IsLayoutOrderEnabled));
        }

        public Dictionary<VuMeterStyle, string> VuMeterStyles => new()
        {
            { VuMeterStyle.Split, "Split" },
            { VuMeterStyle.Minimal, "Minimalistic" }
        };

        partial void OnSelectedVuMeterStyleChanged(VuMeterStyle value)
        {
            JLS.Properties.Settings.Default.VuMeterStyle = value.ToString();
            JLS.Properties.Settings.Default.Save();
        }

        [ObservableProperty]
        private bool _isHorizontalVolumeEnabled;

        partial void OnIsHorizontalVolumeEnabledChanged(bool value)
        {
            Settings.Default.IsHorizontalVolumeEnabled = value;
            Settings.Default.Save();
            OnPropertyChanged(nameof(IsLayoutOrderEnabled));
        }

        [ObservableProperty]
        private bool _isShowTenthsEnabled = true;    

        partial void OnIsShowTenthsEnabledChanged(bool value)
        {
            _lastTimeDisplayCache = string.Empty;
            _lastTenthsCache = -1;
            UpdateTimeDisplay();
        }

        [ObservableProperty]
        private bool _isOsdEnabled = true;

        partial void OnIsOsdEnabledChanged(bool value)
        {
            Settings.Default.IsOsdEnabled = value;
            Settings.Default.Save();
        }

        [ObservableProperty]
        private string _osdPosition = "Top-Left";

        public Dictionary<string, string> OsdPositions => new()
{
    { "Top-Left", "Top Left" },
    { "Top-Right", "Top Right" },
    { "Bottom-Left", "Bottom Left" },
    { "Bottom-Right", "Bottom Right" }
};

        partial void OnOsdPositionChanged(string value)
        {
            Settings.Default.OsdPosition = value;
            Settings.Default.Save();
        }

        [ObservableProperty]
        private bool _isGaplessPlaybackEnabled;

        partial void OnIsGaplessPlaybackEnabledChanged(bool value)
        {
            Settings.Default.IsGaplessPlaybackEnabled = value;
            Settings.Default.Save();

            if (_audioPlayer != null)
            {
                _audioPlayer.IsGaplessEnabled = value;
            }
        }

        [ObservableProperty]
        private bool _isSkipSilenceEnabled;

        partial void OnIsSkipSilenceEnabledChanged(bool value)
        {
            Settings.Default.IsSkipSilenceEnabled = value;
            Settings.Default.Save();

            if (_audioPlayer is BassAudioPlayer bassPlayer)
            {
                bassPlayer.IsSkipSilenceEnabled = value;
            }
        }

        [ObservableProperty]
        private TransitionMode _selectedTransitionMode = TransitionMode.Normal;

        public Dictionary<TransitionMode, string> TransitionModes => new()
{
    { TransitionMode.Normal, "Normal" },
    { TransitionMode.FadeInOut, "Fade In / Fade Out (Smooth edges)" },
    { TransitionMode.Crossfade, "Crossfade (Overlapping tracks)" }
};

        [ObservableProperty]
        private bool _isTransitionDurationVisible = false;

        [ObservableProperty]
        private double _transitionDuration = 3.0;     

        partial void OnTransitionDurationChanged(double value)
        {
            Settings.Default.TransitionDuration = value;
            Settings.Default.Save();

            if (_audioPlayer is BassAudioPlayer bassPlayer)
            {
                bassPlayer.TransitionDurationMs = (int)(value * 1000);
            }
        }

        [RelayCommand]
        private void IncreaseTransitionDuration()
        {
            if (TransitionDuration < 5.0) TransitionDuration += 0.5;
        }

        [RelayCommand]
        private void DecreaseTransitionDuration()
        {
            if (TransitionDuration > 1.0) TransitionDuration -= 0.5;
        }

        public bool IsGaplessAllowed => SelectedTransitionMode == TransitionMode.Normal && IsWasapiOrAsio;

        partial void OnSelectedTransitionModeChanged(TransitionMode value)
        {
            JLS.Properties.Settings.Default.SelectedTransitionMode = value.ToString();
            JLS.Properties.Settings.Default.Save();

            if (value != TransitionMode.Normal)
            {
                IsGaplessPlaybackEnabled = false;
            }

            IsTransitionDurationVisible = value != TransitionMode.Normal;

            OnPropertyChanged(nameof(IsGaplessAllowed));

            if (_audioPlayer is BassAudioPlayer bassPlayer)
            {
                bassPlayer.TransitionType = value;
            }
        }



        private double _targetVuLeft = 0;
        private double _targetVuRight = 0;
        private string _lastTimeDisplayCache = string.Empty;

        private Random _random = new Random();
        private List<string> _shuffledQueue = new();
        private List<HistoryRecord> _playHistory = new();

        private string _currentPlaybackMode = "Explorer";

        [RelayCommand]
        private void OpenPlaylists()
        {
            if (PlaylistsViewModel == null)
            {
                PlaylistsViewModel = new PlaylistsViewModel(PlaylistManager);
                PlaylistsViewModel.PlayRequested += OnPlaylistPlayRequested;
            }
            CurrentContent = PlaylistsViewModel;
            IsSettingsVisible = false;

            SyncPlayerWithExplorer();
            UpdateActiveSidebarItem();

            RefreshTabVisibility();
        }

        [RelayCommand]
        private void OpenSavedAlbums()
        {
            if (SavedAlbumsViewModel == null)
            {
                SavedAlbumsViewModel = new SavedAlbumsViewModel(SavedAlbumsManager, PlaylistManager);
                SavedAlbumsViewModel.PlayRequested += OnSavedAlbumPlayRequested;
            }
            CurrentContent = SavedAlbumsViewModel;
            IsSettingsVisible = false;

            SyncPlayerWithExplorer();
            UpdateActiveSidebarItem();

            RefreshTabVisibility();
        }

        private void OnPlaylistPlayRequested(string filePath)
        {
            _currentPlaybackMode = "Playlist";
            _currentTrackPath = filePath;

            var plViewModel = PlaylistsViewModel;
            if (plViewModel?.SelectedPlaylist != null)
            {
                _currentPlaylist = new ObservableCollection<FileSystemItem>(plViewModel.SelectedPlaylist.Items);
            }

            SyncPlayerWithExplorer();

            if (IsShuffleActive)
            {
                GenerateShuffleQueue();
            }

            _ = Play();
        }

        private void OnSavedAlbumPlayRequested(FileSystemItem track, ObservableCollection<FileSystemItem> albumTracks)
        {
            _currentPlaybackMode = "SavedAlbum";

            if (!string.IsNullOrEmpty(_currentTrackPath) && _currentTrackPath != track.FullPath)
            {
                AddToHistory(_currentTrackPath);
            }

            _currentTrackPath = track.FullPath;

            _currentPlaylist = new ObservableCollection<FileSystemItem>(albumTracks);

            SyncPlayerWithExplorer();

            if (IsShuffleActive)
            {
                GenerateShuffleQueue();
            }

            _ = Play();
        }

        [ObservableProperty]
        private string _activeSidebarItem = "Explorer";

        private void UpdateActiveSidebarItem()
        {
            if (IsNowPlayingVisible) ActiveSidebarItem = "NowPlaying";
            else if (IsSettingsVisible) ActiveSidebarItem = "Settings";
            else if (IsInfoPanelVisible) ActiveSidebarItem = "Info";
            else if (CurrentContent is FileBrowserViewModel) ActiveSidebarItem = "Explorer";
            else if (CurrentContent is PlaylistsViewModel) ActiveSidebarItem = "Playlists";
            else if (CurrentContent is SavedAlbumsViewModel) ActiveSidebarItem = "SavedAlbums"; 
        }





        [ObservableProperty]
        private DataTemplate? _currentSettingsTemplate;

        [RelayCommand]
        private void ShowSoundSettings()
        {
        }

        public List<string> SpacingOptions { get; } = new() { "Tiny", "Small", "Medium", "Large" };
        public List<string> FontOptions { get; } = new() { "Tiny", "Small", "Medium", "Large" };

        [ObservableProperty] private string _treeSpacing = "Medium";
        [ObservableProperty] private string _treeFontSize = "Medium";
        [ObservableProperty] private string _listSpacing = "Medium";
        [ObservableProperty] private string _listFontSize = "Medium";

        [ObservableProperty] private Thickness _actualTreePadding = new Thickness(4, 12, 4, 12);
        [ObservableProperty] private double _actualTreeFontSize = 16;
        [ObservableProperty] private double _actualListHeight = 44;
        [ObservableProperty] private double _actualListFontSize = 16;

        partial void OnTreeSpacingChanged(string value)
        {
            ActualTreePadding = value switch
            {
                "Tiny" => new Thickness(4, 4, 4, 4),       
                "Small" => new Thickness(4, 8, 4, 8),    
                "Large" => new Thickness(4, 16, 4, 16),    
                _ => new Thickness(4, 12, 4, 12)          
            };
            Settings.Default.TreeSpacing = value;
            Settings.Default.Save();
        }

        partial void OnTreeFontSizeChanged(string value)
        {
            ActualTreeFontSize = value switch { "Tiny" => 12, "Small" => 14, "Large" => 18, _ => 16 };
            Settings.Default.TreeFontSize = value;
            Settings.Default.Save();
        }

        partial void OnListSpacingChanged(string value)
        {
            ActualListHeight = value switch
            {
                "Tiny" => 28,           
                "Small" => 36,
                "Large" => 52,
                _ => 44             
            };
            Settings.Default.ListSpacing = value;
            Settings.Default.Save();
        }

        partial void OnListFontSizeChanged(string value)
        {
            ActualListFontSize = value switch { "Tiny" => 12, "Small" => 14, "Large" => 18, _ => 16 };
            Settings.Default.ListFontSize = value;
            Settings.Default.Save();
        }

        [ObservableProperty] private string _playlistListSpacing = "Medium";
        [ObservableProperty] private string _playlistListFontSize = "Medium";

        [ObservableProperty] private double _actualPlaylistListHeight = 44;
        [ObservableProperty] private double _actualPlaylistListFontSize = 16;

        partial void OnPlaylistListSpacingChanged(string value)
        {
            ActualPlaylistListHeight = value switch
            {
                "Tiny" => 28,
                "Small" => 36,
                "Large" => 52,
                _ => 44  
            };
            Settings.Default.PlaylistListSpacing = value;
            Settings.Default.Save();
        }

        partial void OnPlaylistListFontSizeChanged(string value)
        {
            ActualPlaylistListFontSize = value switch { "Tiny" => 12, "Small" => 14, "Large" => 18, _ => 16 };
            Settings.Default.PlaylistListFontSize = value;
            Settings.Default.Save();
        }


        [ObservableProperty]
        private double _uiScale = 1.0;

        [ObservableProperty]
        private bool _showSplashScreen = true;    

        partial void OnShowSplashScreenChanged(bool value)
        {
            JLS.Properties.Settings.Default.ShowSplashScreen = value;
            JLS.Properties.Settings.Default.Save();
        }

        [ObservableProperty]
        private bool _isMinimizeToTrayEnabled = false;

        partial void OnIsMinimizeToTrayEnabledChanged(bool value)
        {
            JLS.Properties.Settings.Default.IsMinimizeToTrayEnabled = value;
            JLS.Properties.Settings.Default.Save();

            if (!value)
            {
                IsMiniPlayerEnabled = false;
            }

        }

        [ObservableProperty]
        private WaveformStyle _selectedWaveformStyle = WaveformStyle.Dj;        

        public Dictionary<WaveformStyle, string> WaveformStyles => new()
{
    { WaveformStyle.Solid, "Solid" },
    { WaveformStyle.Stereo, "Stereo" },
    { WaveformStyle.Dj, "DJ" },
    { WaveformStyle.Mountains, "Mountain" },
        { WaveformStyle.SolidBars, "Solid Bars" },
            { WaveformStyle.StereoBars, "Stereo Bars" },
    { WaveformStyle.MountainBars, "Mountain Bars" }
};

        [RelayCommand]
        private void IncreaseScale()
        {
            UiScale = Math.Min(1.5, UiScale + 0.02);
        }

        [RelayCommand]
        private void DecreaseScale()
        {
            UiScale = Math.Max(0.5, UiScale - 0.02);
        }

        partial void OnUiScaleChanged(double value)
        {
            Settings.Default.UiScale = value;
            Settings.Default.Save();
        }

        [ObservableProperty]
        private object? _currentContent;      

        [RelayCommand]
        private void OpenExplorer()
        {
            if (FileBrowser == null)
            {
                FileBrowser = new FileBrowserViewModel(PlaylistManager);
                FileBrowser.IsAlbumViewEnabled = IsAlbumViewEnabled;
                FileBrowser.PlayRequested += OnExplorerPlayRequested;
                FileBrowser.InitializeSavedManager(SavedAlbumsManager);
            }
            CurrentContent = FileBrowser;       
            IsSettingsVisible = false;

            SyncPlayerWithExplorer();
            UpdateActiveSidebarItem();

            RefreshTabVisibility();
        }

        private void OnExplorerPlayRequested(string filePath)
        {
            _currentPlaybackMode = "Explorer";
            if (!string.IsNullOrEmpty(_currentTrackPath) && _currentTrackPath != filePath)
            {
                AddToHistory(_currentTrackPath);
            }

            _currentTrackPath = filePath;
            var browser = FileBrowser;
            if (browser != null)
            {
                _currentPlaylist = new ObservableCollection<FileSystemItem>(browser.Files);
                SyncPlayerWithExplorer();
            }

            if (IsShuffleActive)
            {
                GenerateShuffleQueue();
            }

            _ = Play();
        }

        private void AddToHistory(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            if (_actualSecondsListened >= 10 || (TotalDuration > 0 && _actualSecondsListened >= TotalDuration * 0.1))
            {
                var existing = _playHistory.FirstOrDefault(x => x.Path == path);
                if (existing != null)
                {
                    _playHistory.Remove(existing);
                }

                _playHistory.Add(new HistoryRecord { Path = path, PlayedAt = DateTime.Now });

                if (_playHistory.Count > 200)
                {
                    _playHistory.RemoveAt(0);
                }
            }
        }


        [ObservableProperty]
        private bool _isNowPlayingVisible = false;

        [ObservableProperty]
        private bool _isFullscreenVisualizerActive = false;

        private void EvaluateVuFreezeState()
        {
            bool shouldFreeze = IsNowPlayingVisible || IsOverlayVisualizerVisible || IsFullscreenVisualizerActive;

            if (shouldFreeze)
            {
                if (IsVuMetersFrozen) return;

                if (_vuFreezeCts != null && !_vuFreezeCts.IsCancellationRequested) return;

                _vuFreezeCts = new CancellationTokenSource();
                var token = _vuFreezeCts.Token;

                Task.Delay(1000, token).ContinueWith(t =>
                {
                    if (!t.IsCanceled)
                    {
                        IsVuMetersFrozen = true;
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            else
            {
                _vuFreezeCts?.Cancel();
                IsVuMetersFrozen = false;
            }
        }

        partial void OnIsNowPlayingVisibleChanged(bool value)
        {
            if (value)
            {
                _titleBarEnableColorTime = DateTime.Now.AddMilliseconds(380);
            }
            else
            {
                _titleBarEnableColorTime = DateTime.MinValue;
            }

            EvaluateVuFreezeState();
        }

        partial void OnIsOverlayVisualizerVisibleChanged(bool value)
        {
            EvaluateVuFreezeState();
        }

        partial void OnIsFullscreenVisualizerActiveChanged(bool value)
        {
            EvaluateVuFreezeState();
        }

        [RelayCommand]
        private void ShowNowPlaying()
        {
            IsNowPlayingVisible = true;
            UpdateActiveSidebarItem();
        }

        [RelayCommand]
        private void HideNowPlaying()
        {
            IsNowPlayingVisible = false;
            UpdateActiveSidebarItem();
        }

        private void OnTrackEnded()
        {
            _ = JLS.Services.StatsTracker.Instance.CommitCurrentTrackAsync();

            _lastTrackedPath = string.Empty;         
            if (RepeatMode == RepeatMode.One)
            {
                _ = Play();
            }
            else
            {
                _ = PlayNext();
            }
        }


        private string _lastMetadataPath = string.Empty;
        private uint _lastAlbumArtChecksum = 0;

        private void UpdateMetadata(string filePath, bool forceUpdate = false)
        {
            if (filePath == _lastMetadataPath && !forceUpdate) return;

            _lastMetadataPath = filePath;

            _currentFluidSpeed = 0.8;

            try
            {
                using (var file = TagLib.File.Create(PathHelper.GetSafePath(filePath)))
                {
                    ArtistName = !string.IsNullOrEmpty(file.Tag.FirstPerformer)
             ? file.Tag.FirstPerformer
             : "";

                    AlbumName = !string.IsNullOrEmpty(file.Tag.Album)
            ? file.Tag.Album
            : "";

                    CurrentTrackName = !string.IsNullOrEmpty(file.Tag.Title)
                                       ? file.Tag.Title
                                       : System.IO.Path.GetFileNameWithoutExtension(filePath);

                    TrackYear = file.Tag.Year > 0 ? file.Tag.Year.ToString() : "Unknown Year";

                    Genre = !string.IsNullOrEmpty(file.Tag.FirstGenre)
                            ? file.Tag.FirstGenre
                            : "Unknown Genre";

                    TrackNumber = file.Tag.Track > 0 ? file.Tag.Track.ToString() : "---";

                    

                    TrackFullPath = filePath;
                    try
                    {
                        long length = new System.IO.FileInfo(filePath).Length;
                        TrackFileSize = (length / 1048576.0).ToString("0.00") + " MB";     
                    }
                    catch
                    {
                        TrackFileSize = "Unknown Size";
                    }

                    bool coverLoaded = false;
                    uint currentChecksum = 0;
                    byte[]? pictureBytes = null;
                    string? localCoverPath = null;

                    if (file.Tag.Pictures.Length > 0)
                    {
                        var pictureData = file.Tag.Pictures[0].Data;
                        currentChecksum = pictureData.Checksum;
                        pictureBytes = pictureData.Data;
                        coverLoaded = true;
                    }
                    else
                    {
                        string? directory = System.IO.Path.GetDirectoryName(filePath);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            string[] possibleNames = { "cover.jpg", "cover.png", "folder.jpg", "folder.png", "front.jpg", "front.png" };

                            foreach (var name in possibleNames)
                            {
                                string path = System.IO.Path.Combine(directory, name);
                                if (System.IO.File.Exists(path))
                                {
                                    localCoverPath = path;
                                    currentChecksum = (uint)path.GetHashCode();
                                    coverLoaded = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (coverLoaded)
                    {
                        if (currentChecksum != _lastAlbumArtChecksum || forceUpdate)
                        {
                            _lastAlbumArtChecksum = currentChecksum;   

                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();

                            bitmap.DecodePixelWidth = 1024;
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;

                            if (pictureBytes != null)
                            {
                                using (var ms = new System.IO.MemoryStream(pictureBytes))
                                {
                                    bitmap.StreamSource = ms;
                                    bitmap.EndInit();
                                }
                            }
                            else
                            {
                                var imgData = System.IO.File.ReadAllBytes(JLS.Services.PathHelper.GetSafePath(localCoverPath!));
                                using (var ms = new System.IO.MemoryStream(imgData))
                                {
                                    bitmap.StreamSource = ms;
                                    bitmap.EndInit();
                                }
                            }

                            bitmap.Freeze();       

                            AlbumArt = bitmap;

                            if (SelectedColorExtractionAlgorithm == ColorExtractionAlgorithm.FixedColor)
                            {
                                var palette = ColorExtractor.GetPaletteFromFixedColor(FixedPrimaryColor);
                                _targetPrimary = palette.Primary;
                                _targetLight = palette.Light;
                                _targetDark = palette.Dark;
                                _targetPanel = palette.Primary;
                            }
                            else
                            {
                                var palette = ColorExtractor.GetPaletteFromImage(bitmap);
                                _targetPrimary = palette.Primary;
                                _targetLight = palette.Light;
                                _targetDark = palette.Dark;
                                _targetPanel = palette.Primary;
                            }

                            double luminance = (0.299 * _targetPanel.R + 0.587 * _targetPanel.G + 0.114 * _targetPanel.B);
                            AdaptiveTextColor = luminance > 150 ? Brushes.Black : Brushes.White;
                        }
                    }
                    else
                    {
                        if (_lastAlbumArtChecksum != 0 || AlbumArt != null || forceUpdate)
                        {
                            _lastAlbumArtChecksum = 0;
                            AlbumArt = null;

                            if (SelectedColorExtractionAlgorithm == ColorExtractionAlgorithm.FixedColor)
                            {
                                var palette = ColorExtractor.GetPaletteFromFixedColor(FixedPrimaryColor);
                                _targetPrimary = palette.Primary;
                                _targetLight = palette.Light;
                                _targetDark = palette.Dark;
                                _targetPanel = palette.Primary;

                                double luminance = (0.299 * _targetPanel.R + 0.587 * _targetPanel.G + 0.114 * _targetPanel.B);
                                AdaptiveTextColor = luminance > 150 ? Brushes.Black : Brushes.White;
                                BottomPanelBackground = new SolidColorBrush(_targetPanel);
                            }
                            else
                            {
                                _targetPrimary = Colors.White;
                                _targetLight = Colors.White;
                                _targetDark = Color.FromRgb(45, 45, 48);
                                _targetPanel = Color.FromRgb(20, 20, 20);
                                AdaptiveTextColor = Brushes.White;
                                BottomPanelBackground = new SolidColorBrush(Color.FromRgb(20, 20, 20));
                            }
                        }
                    }

                    string format = System.IO.Path.GetExtension(filePath).ToUpper().Replace(".", "");
                    int freq = file.Properties.AudioSampleRate;   
                    int bitrate = file.Properties.AudioBitrate;  
                    int bits = file.Properties.BitsPerSample;       

                    string bitsDisplay = bits > 0 ? $"{bits} bit" : "--- bit";

                    TechnicalInfo = $"{format} | {freq / 1000.0} kHz | {bitrate} kbps | {bitsDisplay}";

                    AudioFormat = format;
                    IsAudioFormatVisible = !string.IsNullOrEmpty(format);     

                    AudioSampleRate = freq > 0 ? $"{freq / 1000.0} kHz" : "---";
                    IsAudioSampleRateVisible = freq > 0;      

                    AudioBitrate = bitrate > 0 ? $"{bitrate} kbps" : "---";
                    IsAudioBitrateVisible = bitrate > 0;      

                    AudioBitDepth = bits > 0 ? $"{bits} Bit" : "---";
                    IsAudioBitDepthVisible = bits > 0;      
                }

                UpdateWindowTitle();

                LeftWaveform = null;
                RightWaveform = null;

                if (SelectedControlStyle == PlayerControlStyle.Waveform || NowPlayingControlStyle == PlayerControlStyle.Waveform ||
                    SelectedControlStyle == PlayerControlStyle.Animated || NowPlayingControlStyle == PlayerControlStyle.Animated)
                {
                    GenerateWaveform(filePath);
                }

            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Metadata error: {ex.Message}");
                ArtistName = "Unknown Artist";
                AlbumName = "Unknown Album";
                TechnicalInfo = "Error loading info";
                AlbumArt = null;
            }


            LoadAndParseLyrics(filePath);

        }

        [ObservableProperty]
        private bool _isLyricsOptionsMenuOpen = false;

        [ObservableProperty]
        private bool _isLyricsOpen = false;

        [ObservableProperty]
        private LyricsStyle _selectedLyricsStyle = LyricsStyle.Compact;

        [ObservableProperty]
        private BeautifulLyricsFont _selectedBeautifulLyricsFont = BeautifulLyricsFont.Default;

        public Dictionary<BeautifulLyricsFont, string> BeautifulLyricsFonts => new()
        {
            { BeautifulLyricsFont.Default, "Default" },
            { BeautifulLyricsFont.Bold, "Bold" }
        };

        partial void OnSelectedBeautifulLyricsFontChanged(BeautifulLyricsFont value)
        {
            Settings.Default.BeautifulLyricsFont = value.ToString();
            Settings.Default.Save();
        }

        public Array LyricsStyles => Enum.GetValues(typeof(LyricsStyle));

        private double _countdownTargetTime = -1;
        private bool _isCountdownArmed = false;

        [ObservableProperty]
        private double _countdownDot1Opacity = 0.0;

        [ObservableProperty]
        private double _countdownDot2Opacity = 0.0;

        [ObservableProperty]
        private double _countdownDot3Opacity = 0.0;

        [ObservableProperty]
        private double _countdownDot1Scale = 1.0;

        [ObservableProperty]
        private double _countdownDot2Scale = 1.0;

        [ObservableProperty]
        private double _countdownDot3Scale = 1.0;

        [ObservableProperty]
        private bool _isCountdownVisible = false;

        private void EvaluateCountdown()
        {
            var fakeLine = SyncedLyrics.FirstOrDefault(l => l.Text == "COUNTDOWN_ANIMATION");
            if (fakeLine != null) SyncedLyrics.Remove(fakeLine);

            while (SyncedLyrics.Count > 0 && string.IsNullOrWhiteSpace(SyncedLyrics[0].Text))
            {
                SyncedLyrics.RemoveAt(0);
            }

            var firstLine = SyncedLyrics.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l.Text));

            if (firstLine != null && firstLine.Timestamp > 7.0)
            {
                _countdownTargetTime = firstLine.Timestamp;
                _isCountdownArmed = true;

                SyncedLyrics.Insert(0, new LyricLine
                {
                    Timestamp = 0,
                    TimestampText = "[00:00.00]",
                    Text = "COUNTDOWN_ANIMATION"
                });
            }
            else
            {
                _isCountdownArmed = false;
                _countdownTargetTime = -1;
                IsCountdownVisible = false;
            }
        }

        partial void OnSelectedLyricsStyleChanged(LyricsStyle value)
        {
            Settings.Default.LyricsStyle = value.ToString();
            Settings.Default.Save();

            OnPropertyChanged(nameof(IsBeautifulLyricsFontEnabled));
        }

        [RelayCommand]
        private void ToggleLyrics()
        {
            IsLyricsOpen = !IsLyricsOpen;
            if (IsLyricsOpen)
            {
                IsQueueOpen = false;      
            }
        }

        [RelayCommand]
        private void ToggleLyricsOptionsMenu()
        {
            IsLyricsOptionsMenuOpen = !IsLyricsOptionsMenuOpen;
        }


        [ObservableProperty]
        private bool _isQueueOpen = false;

        [ObservableProperty]
        private ObservableCollection<UnifiedQueueItem> _unifiedQueue = new();

        [RelayCommand]
        private void ToggleQueue()
        {
            IsQueueOpen = !IsQueueOpen;
            if (IsQueueOpen)
            {
                IsLyricsOpen = false;      
                IsLyricsOptionsMenuOpen = false;
                BuildQueue();     
            }
        }

        [RelayCommand]
        private async Task PlayFromQueue(FileSystemItem track)
        {
            if (track == null || track.IsDirectory) return;
            if (_currentTrackPath == track.FullPath) return;

            AddToHistory(_currentTrackPath);

            _currentTrackPath = track.FullPath;
            SyncPlayerWithExplorer();

            await Play();
        }

        private void BuildQueue()
        {
            if (_currentPlaylist == null) return;

            var historyList = new List<FileSystemItem>();
            FileSystemItem? currentItem = null;
            var upcomingList = new List<FileSystemItem>();

            List<string> currentOrder;
            if (IsShuffleActive)
            {
                if (_shuffledQueue.Count == 0) GenerateShuffleQueue();
                currentOrder = _shuffledQueue;
            }
            else
            {
                currentOrder = _currentPlaylist.Where(f => !f.IsDirectory).Select(f => f.FullPath).ToList();
            }

            bool foundCurrent = false;
            foreach (var path in currentOrder)
            {
                var item = _currentPlaylist.FirstOrDefault(x => x.FullPath == path);
                if (item == null) continue;

                if (path == _currentTrackPath)
                {
                    currentItem = item;
                    foundCurrent = true;
                }
                else if (!foundCurrent)
                {
                    historyList.Add(item);          
                }
                else
                {
                    upcomingList.Add(item);      
                }
            }

            var newQueue = new ObservableCollection<UnifiedQueueItem>();

            if (historyList.Count > 0)
            {
                newQueue.Add(new UnifiedQueueItem { IsHeader = true, HeaderText = "PREVIOUS" });
                foreach (var t in historyList) newQueue.Add(new UnifiedQueueItem { IsTrack = true, Track = t });
            }

            if (currentItem != null)
            {
                newQueue.Add(new UnifiedQueueItem { IsHeader = true, HeaderText = "NOW PLAYING" });
                newQueue.Add(new UnifiedQueueItem { IsTrack = true, IsPlaying = true, Track = currentItem });
            }

            if (upcomingList.Count > 0)
            {
                newQueue.Add(new UnifiedQueueItem { IsHeader = true, HeaderText = "UPCOMING" });
                foreach (var t in upcomingList) newQueue.Add(new UnifiedQueueItem { IsTrack = true, Track = t });
            }

            UnifiedQueue = newQueue;
        }



        private void GenerateWaveform(string filePath)
        {
            _waveformCts?.Cancel();
            _waveformCts = new CancellationTokenSource();
            var token = _waveformCts.Token;

            if (_waveformCache.TryGetValue(filePath, out var cachedData))
            {
                LeftWaveform = cachedData.Left;
                RightWaveform = cachedData.Right;
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    var diskCache = await WaveformCacheManager.LoadWaveformAsync(filePath);

                    if (diskCache != null && !token.IsCancellationRequested)
                    {
                        App.Current.Dispatcher.Invoke(() =>
                        {
                            _waveformCache[filePath] = (diskCache.Value.Left, diskCache.Value.Right);
                            LeftWaveform = diskCache.Value.Left;
                            RightWaveform = diskCache.Value.Right;
                        });
                        return;       
                    }

                    var result = await WaveformGenerator.GenerateAsync(filePath, 2000, token);

                    if (result != null && !token.IsCancellationRequested)
                    {
                        _ = WaveformCacheManager.SaveWaveformAsync(filePath, result.Value.Left, result.Value.Right);

                        App.Current.Dispatcher.Invoke(() =>
                        {
                            _waveformCache[filePath] = (result.Value.Left, result.Value.Right);

                            if (_waveformCache.Count > 100)
                            {
                                _waveformCache.Remove(_waveformCache.Keys.First());
                            }

                            LeftWaveform = null;
                            RightWaveform = null;

                            LeftWaveform = result.Value.Left;
                            RightWaveform = result.Value.Right;
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine("Генерация волны прервана юзером.");
                }
            }, token);
        }

        private float[] ResampleWaveform(float[] source, int targetPoints)
        {
            if (source == null || source.Length == 0) return Array.Empty<float>();
            if (targetPoints >= source.Length || targetPoints <= 0) return source;       

            float[] result = new float[targetPoints];

            double chunkSize = (double)source.Length / targetPoints;

            for (int i = 0; i < targetPoints; i++)
            {
                int start = (int)(i * chunkSize);
                int end = (int)((i + 1) * chunkSize);
                if (end > source.Length) end = source.Length;

                float max = 0f;
                for (int j = start; j < end; j++)
                {
                    if (source[j] > max) max = source[j];
                }
                result[i] = max;
            }

            return result;
        }

        partial void OnWaveformWidthChanged(double value)
        {
            if (_resizeTimer != null)
            {
                _resizeTimer.Stop();
                _resizeTimer.Start();
            }
        }

        private void ApplyWaveformResize()
        {
            if (!string.IsNullOrEmpty(_currentTrackPath) && _waveformCache.TryGetValue(_currentTrackPath, out var cachedData))
            {
                int screenPoints = WaveformWidth > 100 ? (int)WaveformWidth : 100;

                var newLeft = ResampleWaveform(cachedData.Left, screenPoints);
                var newRight = ResampleWaveform(cachedData.Right, screenPoints);

                LeftWaveform = null;
                RightWaveform = null;

                LeftWaveform = newLeft;
                RightWaveform = newRight;
            }
        }

        public MainViewModel()
        {
            FluidRandomSeed = new Random().NextDouble() * 1000.0;
            if (DesignerProperties.GetIsInDesignMode(new DependencyObject()))
            {
                AvailableDevices = new ObservableCollection<AudioDevice>
                {
                    new AudioDevice { Name = "Speakers (Design Mode)", DriverType = "Standard" }
                };
                return;
            }

            _uiScale = Settings.Default.UiScale > 0 ? Settings.Default.UiScale : 1.0;

            _ = JLS.Services.StatsTracker.Instance.StartSessionAsync();

            _audioPlayer = new BassAudioPlayer();
            PlaylistManager = new PlaylistManager();
            SavedAlbumsManager = new SavedAlbumsManager();
            _audioPlayer.TrackEnded += OnTrackEnded;

            _deviceManager = new DeviceManager();

            CompositionTarget.Rendering += OnFrameRender;

            AvailableDevices = new ObservableCollection<AudioDevice>(_deviceManager.GetAvailableDevices());

            LoadGlobalSettings();

            if (SelectedDevice == null && AvailableDevices.Count > 0)
            {
                SelectedDevice = AvailableDevices[0];
            }

            _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _resizeTimer.Tick += (s, e) =>
            {
                _resizeTimer.Stop();
                ApplyWaveformResize();
            };

            _ = RestoreLastTrackAsync();

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsGlobalHotkeysEnabled)
                {
                    SetupAllHotkeys();
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            TrackInfo.BeforeSaveHook = async (filePath) =>
            {
                if (_currentTrackPath == filePath)
                {
                    _wasPlayingBeforeTagEdit = IsPlaying;
                    _savedPositionBeforeTagEdit = CurrentPosition;

                    await _audioPlayer.Pause();
                    IsPlaying = false;

                    if (_audioPlayer is BassAudioPlayer bassPlayer)
                    {
                        bassPlayer.FreeStream();
                    }
                }
                await Task.CompletedTask;
            };

            TrackInfo.AfterSaveHook = async (filePath) =>
            {
                JLS.Converters.PathToThumbnailConverter.ClearCacheForFile(filePath);

                if (_currentTrackPath == filePath)
                {
                    UpdateMetadata(filePath, forceUpdate: true);

                    await _audioPlayer.Play(filePath);

                    await _audioPlayer.SetPosition(_savedPositionBeforeTagEdit);

                    _isInternalSeekUpdate = true;
                    CurrentPosition = _savedPositionBeforeTagEdit;
                    _precisePosition = _savedPositionBeforeTagEdit;
                    _lastRenderedPosition = _savedPositionBeforeTagEdit;
                    _isInternalSeekUpdate = false;

                    if (!_wasPlayingBeforeTagEdit)
                    {
                        await _audioPlayer.Pause();
                        IsPlaying = false;
                    }
                    else
                    {
                        IsPlaying = true;
                    }
                }

                if (IsQueueOpen) BuildQueue();

                var itemInPlaylist = _currentPlaylist?.FirstOrDefault(x => x.FullPath == filePath);
                itemInPlaylist?.ReloadMetadata();

                _fileBrowser?.UpdateItemMetadata(filePath);
                _playlistsViewModel?.UpdateItemMetadata(filePath);
                _savedAlbumsViewModel?.UpdateItemMetadata(filePath);

                await Task.CompletedTask;
            };

            BulkTrackInfo.BeforeSaveHook = async (filePath) =>
            {
                if (_currentTrackPath == filePath)
                {
                    _wasPlayingBeforeTagEdit = IsPlaying;
                    _savedPositionBeforeTagEdit = CurrentPosition;

                    await _audioPlayer.Pause();
                    IsPlaying = false;

                    if (_audioPlayer is BassAudioPlayer bassPlayer)
                    {
                        bassPlayer.FreeStream();
                    }
                }
                await Task.CompletedTask;
            };

            BulkTrackInfo.AfterSaveHook = async (filePath) =>
            {
                JLS.Converters.PathToThumbnailConverter.ClearCacheForFile(filePath);

                if (_currentTrackPath == filePath)
                {
                    UpdateMetadata(filePath, forceUpdate: true);

                    await _audioPlayer.Play(filePath);
                    await _audioPlayer.SetPosition(_savedPositionBeforeTagEdit);

                    _isInternalSeekUpdate = true;
                    CurrentPosition = _savedPositionBeforeTagEdit;
                    _precisePosition = _savedPositionBeforeTagEdit;
                    _lastRenderedPosition = _savedPositionBeforeTagEdit;
                    _isInternalSeekUpdate = false;

                    if (!_wasPlayingBeforeTagEdit)
                    {
                        await _audioPlayer.Pause();
                        IsPlaying = false;
                    }
                    else
                    {
                        IsPlaying = true;
                    }
                }

                var itemInPlaylist = _currentPlaylist?.FirstOrDefault(x => x.FullPath == filePath);
                itemInPlaylist?.ReloadMetadata();

                _fileBrowser?.UpdateItemMetadata(filePath);
                _playlistsViewModel?.UpdateItemMetadata(filePath);
                _savedAlbumsViewModel?.UpdateItemMetadata(filePath);

                if (IsQueueOpen) BuildQueue();

                await Task.CompletedTask;
            };

            EditorSyncedLyrics.CollectionChanged += (s, e) => OnPropertyChanged(nameof(IsExportReady));

        }

        private void LoadGlobalSettings()
        {
            if (!string.IsNullOrEmpty(Settings.Default.TreeSpacing)) TreeSpacing = Settings.Default.TreeSpacing;
            if (!string.IsNullOrEmpty(Settings.Default.TreeFontSize)) TreeFontSize = Settings.Default.TreeFontSize;
            if (!string.IsNullOrEmpty(Settings.Default.ListSpacing)) ListSpacing = Settings.Default.ListSpacing;
            if (!string.IsNullOrEmpty(Settings.Default.ListFontSize)) ListFontSize = Settings.Default.ListFontSize;

            if (Enum.TryParse(Settings.Default.SelectedTitleBarColorMode, out TitleBarColorMode tbcm)) SelectedTitleBarColorMode = tbcm;
            if (Enum.TryParse(Settings.Default.SelectedTitleBarTextMode, out TitleBarTextMode tbtm)) SelectedTitleBarTextMode = tbtm;

            if (Enum.TryParse(Settings.Default.WaveformStyle, out WaveformStyle ws)) SelectedWaveformStyle = ws;
            if (Enum.TryParse(Settings.Default.ControlStyle, out PlayerControlStyle pcs)) SelectedControlStyle = pcs;

            if (Enum.TryParse(Settings.Default.ColorExtractionAlgorithm, out ColorExtractionAlgorithm cea))
            {
                SelectedColorExtractionAlgorithm = cea;
            }

            if (!string.IsNullOrEmpty(Settings.Default.FixedPrimaryColor))
            {
                try
                {
                    FixedPrimaryColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString(Settings.Default.FixedPrimaryColor);
                }
                catch {         }
            }

            if (Enum.TryParse(Settings.Default.BottomPanelLayout, out BottomPanelLayout bpl))
            {
                SelectedBottomLayout = bpl;
            }
            UpdateLayoutColumns();

            if (!string.IsNullOrEmpty(Settings.Default.PlaylistListSpacing)) PlaylistListSpacing = Settings.Default.PlaylistListSpacing;
            if (!string.IsNullOrEmpty(Settings.Default.PlaylistListFontSize)) PlaylistListFontSize = Settings.Default.PlaylistListFontSize;

            if (Enum.TryParse(Settings.Default.NowPlayingWaveformStyle, out WaveformStyle npWs)) NowPlayingWaveformStyle = npWs;
            if (Enum.TryParse(Settings.Default.NowPlayingControlStyle, out PlayerControlStyle npCs)) NowPlayingControlStyle = npCs;

            if (Enum.TryParse(Settings.Default.LyricsStyle, out LyricsStyle ls)) SelectedLyricsStyle = ls;

            if (Enum.TryParse(Settings.Default.BrowserAlternatingStyle, out BrowserAlternatingStyle bas))
                SelectedBrowserAlternatingStyle = bas;

            if (Enum.TryParse(Settings.Default.PlaylistAlternatingStyle, out BrowserAlternatingStyle pas))
                SelectedPlaylistAlternatingStyle = pas;

            if (Enum.TryParse(Settings.Default.BeautifulLyricsFont, out BeautifulLyricsFont blf))
                SelectedBeautifulLyricsFont = blf;

            if (Enum.TryParse(Settings.Default.LastTimeDisplayMode, out TimeDisplayMode tdm))
            {
                TimeDisplayMode = tdm;
            }

            if (Enum.TryParse(Settings.Default.VuMeterStyle, out VuMeterStyle vms))
            {
                SelectedVuMeterStyle = vms;
            }

            IsFullscreenVisualizerEnabled = Settings.Default.IsFullscreenVisualizerEnabled;
            if (Enum.TryParse(Settings.Default.VisualizerStyle, out VisualizerStyle vs))
                SelectedVisualizerStyle = vs;

            IsVuMeterVisible = Settings.Default.IsVuMeterVisible;

            IsAutoHideControlsEnabled = Settings.Default.IsAutoHideControlsEnabled;

            IsHorizontalVolumeEnabled = Settings.Default.IsHorizontalVolumeEnabled;

            IsShowTenthsEnabled = Settings.Default.IsShowTenthsEnabled;

            ShowSplashScreen = Settings.Default.ShowSplashScreen;

            IsMinimizeToTrayEnabled = Settings.Default.IsMinimizeToTrayEnabled;

            IsMiniPlayerEnabled = Settings.Default.IsMiniPlayerEnabled;

            IsNowPlayingFluidBackgroundEnabled = Settings.Default.IsNowPlayingFluidBackgroundEnabled;

            IsNowPlayingFluidAudioReactiveEnabled = Settings.Default.IsNowPlayingFluidAudioReactiveEnabled;

            IsCleanModeEnabled = JLS.Properties.Settings.Default.IsCleanModeEnabled;

            IsTaskbarProgressEnabled = JLS.Properties.Settings.Default.IsTaskbarProgressEnabled;

            IsMinimalBottomPanelEnabled = Settings.Default.IsMinimalBottomPanelEnabled;

            IsPlaylistMinimalModeEnabled = JLS.Properties.Settings.Default.IsPlaylistMinimalModeEnabled;

            IsGlassModeEnabled = Settings.Default.IsGlassModeEnabled;

            IsPlayerLogoHidden = Settings.Default.IsPlayerLogoHidden;

            IsAlwaysShowTrackTimingsEnabled = Settings.Default.IsAlwaysShowTrackTimingsEnabled;

            IsOsdEnabled = Settings.Default.IsOsdEnabled;
            if (!string.IsNullOrEmpty(Settings.Default.OsdPosition))
                OsdPosition = Settings.Default.OsdPosition;

            if (Enum.TryParse(Settings.Default.WindowTitleMode, out WindowTitleMode wtm))
            {
                WindowTitleMode = wtm;
            }
            UpdateWindowTitle();      

            if (Enum.TryParse(Settings.Default.SelectedTransitionMode, out TransitionMode tm))
            {
                SelectedTransitionMode = tm;
            }

            if (Settings.Default.TransitionDuration > 0)
            {
                TransitionDuration = Settings.Default.TransitionDuration;
            }
            IsTransitionDurationVisible = SelectedTransitionMode != TransitionMode.Normal;

            IsGaplessPlaybackEnabled = Settings.Default.IsGaplessPlaybackEnabled;

            IsSkipSilenceEnabled = Settings.Default.IsSkipSilenceEnabled;

            IsGlobalHotkeysEnabled = Settings.Default.GlobalHotkeysEnabled;

            IsMediaKeysEnabled = Settings.Default.IsMediaKeysEnabled;

            IsOverlayVisualizerEnabled = Settings.Default.IsOverlayVisualizerEnabled;

            IsAlbumViewEnabled = JLS.Properties.Settings.Default.IsAlbumViewEnabled;

            IsShuffleActive = Properties.Settings.Default.IsShuffleActive;

            if (Enum.TryParse(Properties.Settings.Default.RepeatMode, out RepeatMode rm))
            {
                RepeatMode = rm;
            }

            IsFpsLimited = Settings.Default.IsFpsLimited;
            TargetFps = Settings.Default.TargetFps > 0 ? Settings.Default.TargetFps : 60;
            IsOptimizedWindowModeEnabled = Settings.Default.IsOptimizedWindowModeEnabled;

            HotkeySkipForwardSeconds = Settings.Default.HotkeySkipForwardSeconds > 0 ? Settings.Default.HotkeySkipForwardSeconds : 5;
            HotkeySkipBackwardSeconds = Settings.Default.HotkeySkipBackwardSeconds > 0 ? Settings.Default.HotkeySkipBackwardSeconds : 5;
            HotkeyVolumeUpPercent = Settings.Default.HotkeyVolumeUpPercent > 0 ? Settings.Default.HotkeyVolumeUpPercent : 5;
            HotkeyVolumeDownPercent = Settings.Default.HotkeyVolumeDownPercent > 0 ? Settings.Default.HotkeyVolumeDownPercent : 5;

            Volume = Settings.Default.SavedVolume;

            string savedData = Settings.Default.SelectedDeviceName;
            if (!string.IsNullOrEmpty(savedData))
            {
                string[] parts = savedData.Split(new[] { ":::" }, StringSplitOptions.None);
                string deviceName = parts[0];
                string driverType = parts.Length > 1 ? parts[1] : "";

                AudioDevice? device = null;

                if (!string.IsNullOrEmpty(driverType))
                {
                    device = AvailableDevices.FirstOrDefault(d => d.Name == deviceName && d.DriverType == driverType);
                }

                if (device == null)
                {
                    device = AvailableDevices.FirstOrDefault(d => d.Name == deviceName);
                }

                if (device != null)
                {
                    SelectedDevice = device;
                }
            }

            string lastView = Settings.Default.LastOpenedView ?? "Explorer";
            bool openNowPlaying = false;

            if (lastView == "NowPlaying")
            {
                openNowPlaying = true;
                lastView = "Explorer";      
            }
            else if (lastView.StartsWith("NowPlaying|"))
            {
                openNowPlaying = true;
                lastView = lastView.Split('|')[1];
            }

            switch (lastView)
            {
                case "Playlists":
                    OpenPlaylists();
                    break;
                case "SavedAlbums":
                    OpenSavedAlbums();
                    break;
                default:
                    OpenExplorer();
                    break;
            }

            if (openNowPlaying)
            {
                ShowNowPlaying();
            }
        }

        partial void OnSelectedControlStyleChanged(PlayerControlStyle value)
        {
            JLS.Properties.Settings.Default.ControlStyle = value.ToString();
            JLS.Properties.Settings.Default.Save();

            if ((value == PlayerControlStyle.Waveform || value == PlayerControlStyle.Animated) && LeftWaveform == null && !string.IsNullOrEmpty(_currentTrackPath))
            {
                GenerateWaveform(_currentTrackPath);
            }

            OnPropertyChanged(nameof(IsBottomPanelWaveformStyleEnabled));
        }

        partial void OnIsVuMeterVisibleChanged(bool value)
        {
            Settings.Default.IsVuMeterVisible = value;
            Settings.Default.Save();

            OnPropertyChanged(nameof(IsVuMeterStyleEnabled));
            OnPropertyChanged(nameof(IsLayoutOrderEnabled));
        }
        partial void OnSelectedWaveformStyleChanged(WaveformStyle value)
        {
            Settings.Default.WaveformStyle = value.ToString();
            Settings.Default.Save();
        }

        partial void OnSelectedDeviceChanged(AudioDevice? value)
        {
            if (value != null && _audioPlayer != null)
            {
                Settings.Default.SelectedDeviceName = $"{value.Name}:::{value.DriverType}";
                Settings.Default.Save();

                _audioPlayer.Initialize(value);
                _audioPlayer.SetVolume(Volume);

                OnPropertyChanged(nameof(IsWasapiOrAsio));
                OnPropertyChanged(nameof(IsGaplessAllowed));

                if (value.DriverType == "Standard")
                {
                    IsSkipSilenceEnabled = false;
                    IsGaplessPlaybackEnabled = false;
                }

                OnPropertyChanged(nameof(VisualizerStyles));

                if (!IsWasapiOrAsio && SelectedVisualizerStyle == VisualizerStyle.Oscilloscope)
                {
                    SelectedVisualizerStyle = VisualizerStyle.ClassicBars;
                }
            }
        }

        private TimeSpan _lastTextUpdateTime = TimeSpan.Zero;



        private void PrepareNextTrackInBg()
        {
            if (_currentPlaylist == null || _currentPlaylist.Count == 0) return;

            string nextPath = string.Empty;

            if (RepeatMode == RepeatMode.One)
            {
                nextPath = _currentTrackPath;
            }
            else
            {
                List<string> currentOrder = IsShuffleActive
                    ? _shuffledQueue
                    : _currentPlaylist.Where(f => !f.IsDirectory).Select(f => f.FullPath).ToList();

                if (currentOrder.Count > 0)
                {
                    int index = currentOrder.IndexOf(_currentTrackPath);
                    if (index >= 0 && index + 1 < currentOrder.Count)
                    {
                        nextPath = currentOrder[index + 1];
                    }
                    else if (RepeatMode == RepeatMode.All)
                    {
                        nextPath = currentOrder[0];
                    }
                }
            }

            _audioPlayer.SetNextTrack(nextPath);
        }

        [RelayCommand]
        private void AddLyricLineAtCurrentPosition()
        {
            double currentPos = CurrentPosition;

            TimeSpan t = TimeSpan.FromSeconds(currentPos);
            string timeString = $"[{t.Minutes:D2}:{t.Seconds:D2}.{t.Milliseconds / 10:D2}]";

            var newLine = new LyricLine
            {
                Timestamp = currentPos,
                TimestampText = timeString,
                Text = string.Empty    
            };

            var index = EditorSyncedLyrics.ToList().FindIndex(l => l.Timestamp > currentPos);
            if (index == -1) EditorSyncedLyrics.Add(newLine);
            else EditorSyncedLyrics.Insert(index, newLine);
        }

        [RelayCommand]
        private void DeleteAllLines()
        {

            EditorSyncedLyrics.Clear();
        }

        [RelayCommand]
        private void DeleteLyricLine(LyricLine line)
        {
            if (line != null && EditorSyncedLyrics.Contains(line))
            {
                EditorSyncedLyrics.Remove(line);
            }
        }

        private System.Diagnostics.Stopwatch _mainFpsTimer = System.Diagnostics.Stopwatch.StartNew();

        private void OnFrameRender(object? sender, EventArgs e)
        {
            if (_audioPlayer == null) return;

            if (IsFpsLimited)
            {
                double targetMs = 1000.0 / TargetFps;
                if (_mainFpsTimer.ElapsedMilliseconds < (targetMs - 1.5)) return;
                _mainFpsTimer.Restart();
            }

            if (IsNowPlayingVisible && IsNowPlayingFluidBackgroundEnabled)
            {
                if (e is RenderingEventArgs rArgs && (rArgs.RenderingTime - _lastFluidUpdateTime).TotalMilliseconds >= 33)
                {
                    double rawBassHit = 0.0;

                    if (IsPlaying && _audioPlayer != null && IsNowPlayingFluidAudioReactiveEnabled)
                    {
                        float[] fft = _audioPlayer.GetFFTData();
                        if (fft != null && fft.Length > 10)
                        {
                            rawBassHit = (fft[1] + fft[2] + fft[3] + fft[4]) / 4.0 * 3.0;
                            rawBassHit = Math.Min(1.0, rawBassHit);

                            double currentEnergy = 0;
                            int dropBins = Math.Min(fft.Length, 20);     
                            for (int i = 1; i < dropBins; i++)
                            {
                                currentEnergy += fft[i];
                            }
                            currentEnergy /= dropBins;

                            _microEnergy = _microEnergy * 0.4 + currentEnergy * 0.6;

                            if (_microEnergy > _macroEnergy)
                            {
                                _macroEnergy = _macroEnergy * 0.98 + _microEnergy * 0.02;
                            }
                            else
                            {
                                _macroEnergy = _macroEnergy * 0.998 + _microEnergy * 0.002;
                            }

                            double dropRatio = _macroEnergy > 0.001 ? (_microEnergy / _macroEnergy) : 1.0;

                            if (_microEnergy > _macroEnergy * 0.85)
                            {
                                _beatConfidence += 0.04;
                            }
                            else
                            {
                                _beatConfidence -= 0.01;
                            }
                            _beatConfidence = Math.Max(0.0, Math.Min(1.0, _beatConfidence));

                            double bigDropThreshold = 1.45 + (_beatConfidence * 0.4);

                            double mediumDropThreshold = 1.20 + (_beatConfidence * 0.25);

                            double timeSinceLastDrop = (DateTime.Now - _lastDropTime).TotalSeconds;

                            if (currentEnergy > 0.035 && timeSinceLastDrop > 0.5)
                            {
                                if (dropRatio > bigDropThreshold && timeSinceLastDrop > 1.5)
                                {
                                    _currentFlash = Math.Min(0.5, dropRatio * 0.15);
                                    _currentDeformation += Math.Min(0.20, dropRatio * 0.04);
                                    _lastDropTime = DateTime.Now;

                                    _macroEnergy = _microEnergy * 0.85;
                                    _beatConfidence = 0.5;
                                }
                                else if (dropRatio > mediumDropThreshold && timeSinceLastDrop > 0.6)
                                {
                                    _currentFlash = Math.Min(0.25, dropRatio * 0.09);
                                    _currentDeformation += Math.Min(0.10, dropRatio * 0.02);
                                    _lastDropTime = DateTime.Now;

                                    _macroEnergy = _microEnergy * 0.95;
                                }
                            }
                        }
                    }

                    _currentFlash += (0.0 - _currentFlash) * 0.05;
                    FluidFlash = _currentFlash;

                    if (rawBassHit > _smoothedBass)
                        _smoothedBass += (rawBassHit - _smoothedBass) * 0.16;
                    else
                        _smoothedBass += (rawBassHit - _smoothedBass) * 0.04;

                    double targetFluidSpeed = IsPlaying ? 0.012 : 0.005;

                    if (IsPlaying)
                    {
                        targetFluidSpeed += (_smoothedBass * 0.003);
                    }

                    _currentFluidSpeed += (targetFluidSpeed - _currentFluidSpeed) * 0.1;
                    FluidTime += _currentFluidSpeed;
                    FluidIntensity += _smoothedBass * 0.012;

                    double targetDeformation = Math.Max(0.0, _smoothedBass - 0.5) * 1.5;

                    double deformResponsiveness = (targetDeformation > _currentDeformation) ? 0.02 : 0.01;

                    _currentDeformation += (targetDeformation - _currentDeformation) * deformResponsiveness;
                    FluidDeformation = _currentDeformation;

                    _lastFluidUpdateTime = rArgs.RenderingTime;
                }
            }

            UpdateTitleBarVisuals();

            bool isOsdVisible = _osdWindow != null && _osdWindow.IsVisible && _osdWindow.Opacity > 0;
            bool isVisualizerActive = MainWindow.ActiveVisualizerWindow != null || IsOverlayVisualizerVisible;

            if (Application.Current.MainWindow != null &&
                Application.Current.MainWindow.WindowState == WindowState.Minimized &&
                !isOsdVisible && !isVisualizerActive)
            {
                return;
            }

            double colorStep = 0.01;

            Color newPrimary = LerpColor(_currentPrimary, _targetPrimary, colorStep);
            Color newLight = LerpColor(_currentLight, _targetLight, colorStep);
            Color newDark = LerpColor(_currentDark, _targetDark, colorStep);
            Color newPanel = LerpColor(_currentPanel, _targetPanel, colorStep);

            Color newTitleBarBg = LerpColor(_currentTitleBarBg, _targetTitleBarBg, colorStep);
            Color newTitleBarFg = LerpColor(_currentTitleBarFg, _targetTitleBarFg, colorStep);

            if (_currentPrimary != newPrimary)
            {
                _currentPrimary = newPrimary;
                if (PrimaryColor is SolidColorBrush scb && !scb.IsFrozen)
                {
                    scb.Color = _currentPrimary;
                    OnPropertyChanged(nameof(PrimaryColor));
                }
                else
                {
                    PrimaryColor = new SolidColorBrush(_currentPrimary);
                }
            }

            if (_currentLight != newLight)
            {
                _currentLight = newLight;
                if (LightColor is SolidColorBrush scb && !scb.IsFrozen)
                {
                    scb.Color = _currentLight;
                    OnPropertyChanged(nameof(LightColor));
                }
                else
                {
                    LightColor = new SolidColorBrush(_currentLight);
                }
            }

            if (_currentDark != newDark)
            {
                _currentDark = newDark;
                if (DarkColor is SolidColorBrush scb && !scb.IsFrozen)
                {
                    scb.Color = _currentDark;
                    OnPropertyChanged(nameof(DarkColor));
                }
                else
                {
                    DarkColor = new SolidColorBrush(_currentDark);
                }
            }

            if (_currentPanel != newPanel)
            {
                _currentPanel = newPanel;
                if (BottomPanelBackground is SolidColorBrush scb && !scb.IsFrozen)
                {
                    scb.Color = _currentPanel;
                    OnPropertyChanged(nameof(BottomPanelBackground));
                }
                else
                {
                    BottomPanelBackground = new SolidColorBrush(_currentPanel);
                }
            }

            if (_currentTitleBarBg != newTitleBarBg)
            {
                _currentTitleBarBg = newTitleBarBg;
                if (TitleBarBackground is SolidColorBrush scb && !scb.IsFrozen)
                {
                    scb.Color = _currentTitleBarBg;
                    OnPropertyChanged(nameof(TitleBarBackground));
                }
                else
                {
                    TitleBarBackground = new SolidColorBrush(_currentTitleBarBg);
                }
            }

            if (_currentTitleBarFg != newTitleBarFg)
            {
                _currentTitleBarFg = newTitleBarFg;
                if (TitleBarForeground is SolidColorBrush scb && !scb.IsFrozen)
                {
                    scb.Color = _currentTitleBarFg;
                    OnPropertyChanged(nameof(TitleBarForeground));
                }
                else
                {
                    TitleBarForeground = new SolidColorBrush(_currentTitleBarFg);
                }
            }

            if (e is RenderingEventArgs renderingArgs)
            {
                double dt = _lastFrameTime == TimeSpan.Zero ? 0 : (renderingArgs.RenderingTime - _lastFrameTime).TotalSeconds;
                _lastFrameTime = renderingArgs.RenderingTime;

                if (dt > 0.1) dt = 0.016;

                if (IsPlaying)
                {
                    _actualSecondsListened += dt;
                }

                double realPos = _audioPlayer != null ? _audioPlayer.GetPosition() : 0;

                if (_targetSeekPosition >= 0)
                {
                    if ((DateTime.Now - _lastUserSeekTime).TotalMilliseconds < 300 && Math.Abs(realPos - _targetSeekPosition) > 0.5)
                    {
                        realPos = _targetSeekPosition;
                    }
                    else
                    {
                        _targetSeekPosition = -1;
                    }
                }
                if (!IsDraggingTrack)
                {
                    double diff = realPos - _precisePosition;

                    if (Math.Abs(diff) > 0.5)
                    {
                        _isCatchingUp = true;
                    }

                    if (_isCatchingUp)
                    {
                        double baseSpeed = IsPlaying ? dt : 0;
                        _precisePosition += baseSpeed + (diff * 15.0 * dt);

                        if (Math.Abs(diff) < 0.08)
                        {
                            _precisePosition = realPos;
                            _isCatchingUp = false;
                        }
                    }
                    else if (IsPlaying)
                    {
                        _precisePosition += dt + (diff * 0.05);
                    }
                    else if (Math.Abs(diff) > 0.001)
                    {
                        _precisePosition += diff * 15.0 * dt;
                    }
                }

                double currentThrottleLimit = (_isCatchingUp || IsDraggingTrack) ? 0 : 20;

                if ((renderingArgs.RenderingTime - _lastUiUpdateTime).TotalMilliseconds >= currentThrottleLimit)
                {
                    _lastUiUpdateTime = renderingArgs.RenderingTime;

                    if (!IsDraggingTrack)
                    {
                        _isInternalSeekUpdate = true;
                        CurrentPosition = _precisePosition;
                        _isInternalSeekUpdate = false;

                        _lastRenderedPosition = CurrentPosition;

                        WaveformProgress = TotalDuration > 0 ? CurrentPosition / TotalDuration : 0;
                        BubbleX = WaveformWidth * WaveformProgress;
                    }
                    else
                    {
                        if (_dragTargetPosition >= 0)
                        {
                            double dragDiff = _dragTargetPosition - _precisePosition;

                            double snapThreshold = Math.Max(0.1, Math.Min(TotalDuration * 0.005, 0.5));

                            if (Math.Abs(dragDiff) <= snapThreshold)
                            {
                                _precisePosition = _dragTargetPosition;
                                _dragTargetPosition = -1;       
                            }
                            else
                            {
                                double speed = 0.25;
                                double step = dragDiff * speed;

                                _precisePosition += Math.Sign(dragDiff) * Math.Max(Math.Abs(step), snapThreshold);
                            }

                            _isInternalSeekUpdate = true;
                            CurrentPosition = _precisePosition;
                            _isInternalSeekUpdate = false;
                        }

                        WaveformProgress = TotalDuration > 0 ? CurrentPosition / TotalDuration : 0;
                        BubbleX = WaveformWidth * WaveformProgress;
                    }

                    if (IsSyncedLyricsMode && SyncedLyrics.Count > 0)
                    {
                        double syncPosition = (_audioPlayer?.GetPosition() ?? 0) + 0.44;
                        var activeLine = SyncedLyrics.LastOrDefault(l => l.Timestamp <= syncPosition);

                        if (activeLine != _currentActiveLyric)
                        {
                            int activeIndex = activeLine != null ? SyncedLyrics.IndexOf(activeLine) : -1;

                            for (int i = 0; i < SyncedLyrics.Count; i++)
                            {
                                var line = SyncedLyrics[i];

                                line.IsPassed = i < activeIndex;

                                line.IsActive = i == activeIndex;

                                line.IsNext = i == activeIndex + 1;
                            }

                            _currentActiveLyric = activeLine;

                            if (activeLine != null)
                                ActiveLyricChanged?.Invoke(this, activeLine);
                        }
                    }

                    if (_isCountdownArmed && _countdownTargetTime > 0)
                    {
                        double t = _precisePosition;
                        double target = _countdownTargetTime;

                        if (t >= 0 && t <= target)
                        {
                            IsCountdownVisible = true;

                            if (t <= target - 3.6)
                            {
                                double timeUntilFade = (target - 3.6) - t;
                                double fadeIn = Math.Min(1.0, t / 1.0);     

                                double damping = Math.Min(1.0, timeUntilFade);

                                double speed = 4.0;

                                double wave1 = (Math.Sin(t * speed) + 1.0) / 2.0;
                                double wave2 = (Math.Sin(t * speed - 1.2) + 1.0) / 2.0;
                                double wave3 = (Math.Sin(t * speed - 2.4) + 1.0) / 2.0;

                                CountdownDot1Scale = 1.0 + (0.5 * wave1 * damping);
                                CountdownDot1Opacity = fadeIn * (1.0 - (0.6 * (1.0 - wave1) * damping));

                                CountdownDot2Scale = 1.0 + (0.5 * wave2 * damping);
                                CountdownDot2Opacity = fadeIn * (1.0 - (0.6 * (1.0 - wave2) * damping));

                                CountdownDot3Scale = 1.0 + (0.5 * wave3 * damping);
                                CountdownDot3Opacity = fadeIn * (1.0 - (0.6 * (1.0 - wave3) * damping));
                            }
                            else
                            {
                                CountdownDot1Scale = 1.0;
                                CountdownDot2Scale = 1.0;
                                CountdownDot3Scale = 1.0;

                                double p = t - (target - 3.6);

                                CountdownDot1Opacity = Math.Max(0.0, 1.0 - p);

                                CountdownDot2Opacity = Math.Max(0.0, Math.Min(1.0, 1.0 - (p - 1.0)));

                                CountdownDot3Opacity = Math.Max(0.0, Math.Min(1.0, 1.0 - (p - 2.0)));

                            }
                        }
                        else
                        {
                            IsCountdownVisible = false;
                            CountdownDot1Opacity = 0; CountdownDot1Scale = 1.0;
                            CountdownDot2Opacity = 0; CountdownDot2Scale = 1.0;
                            CountdownDot3Opacity = 0; CountdownDot3Scale = 1.0;
                        }
                    }



                    if (IsLyricsEditorOpen && IsSyncedLyricsMode && EditorSyncedLyrics.Count > 0)
                    {
                        double syncPosition = (_audioPlayer?.GetPosition() ?? 0) + 0.44;

                        var activeEditorLine = EditorSyncedLyrics.LastOrDefault(l => l.Timestamp <= syncPosition);

                        if (activeEditorLine != null && activeEditorLine != _currentActiveEditorLyric)
                        {
                            if (_currentActiveEditorLyric != null)
                                _currentActiveEditorLyric.IsActive = false;

                            activeEditorLine.IsActive = true;
                            _currentActiveEditorLyric = activeEditorLine;
                        }
                    }
                }

                if (Math.Abs(_targetVolume - Volume) > 0.001)
                {
                    _isInternalVolumeUpdate = true;

                    Volume += (_targetVolume - Volume) * 15.0 * dt;

                    if (Math.Abs(_targetVolume - Volume) < 0.005)
                    {
                        Volume = _targetVolume;
                    }
                    _isInternalVolumeUpdate = false;
                }
                _lastRenderedVolume = Volume;

                if ((renderingArgs.RenderingTime - _lastTextUpdateTime).TotalMilliseconds >= 50)
                {
                    UpdateTimeDisplay();
                    _lastTextUpdateTime = renderingArgs.RenderingTime;
                }
            }

            bool titleBarBgReached = _currentTitleBarBg == _targetTitleBarBg;
            bool titleBarFgReached = _currentTitleBarFg == _targetTitleBarFg;

            bool colorsReached = _currentPrimary == _targetPrimary && _currentLight == _targetLight && _currentDark == _targetDark;
            bool sliderReached = Math.Abs(CurrentPosition - _audioPlayer!.GetPosition()) < 0.01;
            bool volumeReached = Math.Abs(Volume - _targetVolume) < 0.005;     

            bool isVuResting = IsVuMetersFrozen || (VuLeft == 0 && VuRight == 0);
            if (!IsPlaying && isVuResting && colorsReached && sliderReached && volumeReached) return;

            if (IsPlaying)
            {
                _audioPlayer.GetVULevels(_vuBuffer);
                _targetVuLeft = _vuBuffer[0];
                _targetVuRight = _vuBuffer[1];
            }
            else
            {
                _targetVuLeft = 0;
                _targetVuRight = 0;
            }

            if (!IsVuMetersFrozen)
            {
                double attack = 0.22;
                double release = 0.04;

                VuLeft += (_targetVuLeft - VuLeft) * (_targetVuLeft > VuLeft ? attack : release);
                VuRight += (_targetVuRight - VuRight) * (_targetVuRight > VuRight ? attack : release);

                if (VuLeft < 0.1) VuLeft = 0;
                if (VuRight < 0.1) VuRight = 0;
            }



            bool needsFft = isVisualizerActive ||
                SelectedControlStyle == PlayerControlStyle.Animated ||
                (NowPlayingControlStyle == PlayerControlStyle.Animated && IsNowPlayingVisible);

            if (needsFft && IsPlaying)
            {
                FftData = _audioPlayer.GetFFTData();
                OnPropertyChanged(nameof(FftData));

                if (isVisualizerActive)
                {
                    StereoFftData = _audioPlayer.GetStereoFFTData();
                    OnPropertyChanged(nameof(StereoFftData));

                    if (SelectedVisualizerStyle == VisualizerStyle.Oscilloscope)
                    {
                        WaveData = _audioPlayer.GetWaveData();
                        OnPropertyChanged(nameof(WaveData));
                    }
                }
            }

        }

        partial void OnVolumeChanged(double value)
        {
            if (_isInternalVolumeUpdate) return;

            if (_audioPlayer != null)
            {
                _targetVolume = value;
                _audioPlayer.SetVolume(value);      

                if (!_isAppStarting && (DateTime.Now - _lastUserVolumeTime).TotalMilliseconds > 250)
                {
                    _isInternalVolumeUpdate = true;
                    Volume = _lastRenderedVolume;    
                    _isInternalVolumeUpdate = false;
                }

                _lastUserVolumeTime = DateTime.Now;
                ShowOsdIfNeeded();
            }
        }

        partial void OnCurrentPositionChanged(double value)
        {
            if (_isInternalSeekUpdate) return;

            if (IsDraggingTrack)
            {
                UpdateTimeDisplay();
                return;
            }

            if (_audioPlayer != null)
            {
                double realPos = _audioPlayer.GetPosition();

                if (Math.Abs(value - realPos) > 0.05)
                {
                    _audioPlayer.SetPosition(value);

                    if ((DateTime.Now - _lastUserSeekTime).TotalMilliseconds > 250)
                    {
                        _isInternalSeekUpdate = true;
                        CurrentPosition = _lastRenderedPosition;
                        _precisePosition = _lastRenderedPosition;   
                        _isInternalSeekUpdate = false;
                    }

                    _lastUserSeekTime = DateTime.Now;
                }
            }

            UpdateTimeDisplay();
        }

        private int _lastTenthsCache = -1;

        private void UpdateTimeDisplay()
        {
            int currentTenths = (int)(CurrentPosition * 20);
            if (_lastTenthsCache == currentTenths) return;
            _lastTenthsCache = currentTenths;

            var current = TimeSpan.FromSeconds(CurrentPosition);
            var total = TimeSpan.FromSeconds(TotalDuration);

            string format = TotalDuration >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss";
            CurrentTimeSimple = current.ToString(format);
            TotalTimeSimple = total.ToString(format);

            string curFormat = IsShowTenthsEnabled
                ? (TotalDuration >= 3600 ? @"hh\:mm\:ss\.f" : @"mm\:ss\.f")
                : (TotalDuration >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss");

            string totFormat = IsShowTenthsEnabled
                ? (TotalDuration >= 3600 ? @"hh\:mm\:ss\.f" : @"mm\:ss\.f")
                : (TotalDuration >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss");

            CurrentTimeOnly = current.ToString(curFormat);

            string newDisplay = "";
            switch (TimeDisplayMode)
            {
                case TimeDisplayMode.Elapsed:
                    newDisplay = current.ToString(curFormat);
                    break;
                case TimeDisplayMode.Remaining:
                    var remaining = TimeSpan.FromSeconds(Math.Max(0, TotalDuration - CurrentPosition));
                    newDisplay = $"-{remaining.ToString(curFormat)}";
                    break;
                case TimeDisplayMode.Full:
                default:
                    newDisplay = $"{current.ToString(curFormat)} / {total.ToString(totFormat)}";
                    break;
            }

            if (_lastTimeDisplayCache != newDisplay)
            {
                _lastTimeDisplayCache = newDisplay;
                TimeDisplay = newDisplay;
            }
        }

        [ObservableProperty]
        private int _hotkeySkipForwardSeconds = 5;

        partial void OnHotkeySkipForwardSecondsChanged(int value)
        {
            JLS.Properties.Settings.Default.HotkeySkipForwardSeconds = value;
            JLS.Properties.Settings.Default.Save();
        }

        [RelayCommand]
        private void IncreaseHotkeySkipForward()
        {
            if (HotkeySkipForwardSeconds < 30) HotkeySkipForwardSeconds += 1;
        }

        [RelayCommand]
        private void DecreaseHotkeySkipForward()
        {
            if (HotkeySkipForwardSeconds > 1) HotkeySkipForwardSeconds -= 1;
        }

        [ObservableProperty]
        private int _hotkeySkipBackwardSeconds = 5;

        partial void OnHotkeySkipBackwardSecondsChanged(int value)
        {
            JLS.Properties.Settings.Default.HotkeySkipBackwardSeconds = value;
            JLS.Properties.Settings.Default.Save();
        }

        [RelayCommand]
        private void IncreaseHotkeySkipBackward()
        {
            if (HotkeySkipBackwardSeconds < 30) HotkeySkipBackwardSeconds += 1;
        }

        [RelayCommand]
        private void DecreaseHotkeySkipBackward()
        {
            if (HotkeySkipBackwardSeconds > 1) HotkeySkipBackwardSeconds -= 1;
        }

        [ObservableProperty]
        private int _hotkeyVolumeUpPercent = 5;

        partial void OnHotkeyVolumeUpPercentChanged(int value)
        {
            JLS.Properties.Settings.Default.HotkeyVolumeUpPercent = value;
            JLS.Properties.Settings.Default.Save();
        }

        [RelayCommand]
        private void IncreaseHotkeyVolumeUp()
        {
            if (HotkeyVolumeUpPercent < 20) HotkeyVolumeUpPercent += 1;
        }

        [RelayCommand]
        private void DecreaseHotkeyVolumeUp()
        {
            if (HotkeyVolumeUpPercent > 1) HotkeyVolumeUpPercent -= 1;
        }

        [ObservableProperty]
        private int _hotkeyVolumeDownPercent = 5;

        partial void OnHotkeyVolumeDownPercentChanged(int value)
        {
            JLS.Properties.Settings.Default.HotkeyVolumeDownPercent = value;
            JLS.Properties.Settings.Default.Save();
        }

        [RelayCommand]
        private void IncreaseHotkeyVolumeDown()
        {
            if (HotkeyVolumeDownPercent < 20) HotkeyVolumeDownPercent += 1;
        }

        [RelayCommand]
        private void DecreaseHotkeyVolumeDown()
        {
            if (HotkeyVolumeDownPercent > 1) HotkeyVolumeDownPercent -= 1;
        }

        [RelayCommand]
        private void VolumeUp()
        {
            _targetVolume = Math.Min(1.0, _targetVolume + (HotkeyVolumeUpPercent / 100.0));
            if (_audioPlayer != null) _audioPlayer.SetVolume(_targetVolume);
            ShowOsdIfNeeded();
        }

        [RelayCommand]
        private void VolumeDown()
        {
            _targetVolume = Math.Max(0.0, _targetVolume - (HotkeyVolumeDownPercent / 100.0));
            if (_audioPlayer != null) _audioPlayer.SetVolume(_targetVolume);
            ShowOsdIfNeeded();
        }

        [RelayCommand]
        private void SeekForward()
        {
            if (IsPlaying || CurrentPosition > 0)
            {
                _audioPlayer.SetPosition(Math.Min(TotalDuration, _audioPlayer.GetPosition() + HotkeySkipForwardSeconds));
                ShowOsdIfNeeded();
            }
        }

        [RelayCommand]
        private void SeekBackward()
        {
            if (IsPlaying || CurrentPosition > 0)
            {
                _audioPlayer.SetPosition(Math.Max(0.0, _audioPlayer.GetPosition() - HotkeySkipBackwardSeconds));
                ShowOsdIfNeeded();
            }
        }

        [RelayCommand]
        private void OpenTrackLocationInExplorer()
        {
            if (!string.IsNullOrEmpty(_currentTrackPath) && File.Exists(_currentTrackPath))
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_currentTrackPath}\"");
            }
        }

        [RelayCommand]
        private void OpenInExplorer(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;     
            JLS.Services.ExplorerHelper.OpenFolderAndSelectFile(path);
        }

        [RelayCommand]
        private void TogglePlaylist(object[]? parameters)
        {
            if (parameters != null && parameters.Length == 2 &&
                parameters[0] is Playlist playlist &&
                parameters[1] is FileSystemItem track)
            {
                if (playlist.TrackPaths.Contains(track.FullPath))
                {
                    PlaylistManager.RemoveTrackFromPlaylist(playlist, track);
                }
                else
                {
                    PlaylistManager.AddTrackToPlaylist(playlist, track);
                }
            }
        }

        [RelayCommand]
        private void CloseApplication()
        {
            Application.Current.MainWindow?.Close();
        }

        [RelayCommand]
        private async Task PlayNext()
        {
            if (_currentPlaylist == null || _currentPlaylist.Count == 0) return;

            AddToHistory(_currentTrackPath);            

            List<string> currentOrder = IsShuffleActive
                ? _shuffledQueue
                : _currentPlaylist.Where(f => !f.IsDirectory).Select(f => f.FullPath).ToList();

            if (currentOrder.Count == 0) return;

            int index = currentOrder.IndexOf(_currentTrackPath);
            bool foundNext = false;

            if (index >= 0 && index + 1 < currentOrder.Count)
            {
                _currentTrackPath = currentOrder[index + 1];
                SyncPlayerWithExplorer();
                await Play();
                foundNext = true;
            }

            if (!foundNext)
            {
                if (RepeatMode == RepeatMode.All && currentOrder.Count > 0)
                {
                    _currentTrackPath = currentOrder[0];
                    SyncPlayerWithExplorer();
                    await Play();
                    return;
                }

                await Pause();
                if (_audioPlayer != null) await _audioPlayer.SetPosition(0);
                _isInternalSeekUpdate = true;
                CurrentPosition = 0;
                _precisePosition = 0;
                _lastRenderedPosition = 0;
                WaveformProgress = 0;
                BubbleX = 0;
                _isInternalSeekUpdate = false;
                UpdateTimeDisplay();
            }
        }

        private void GenerateShuffleQueue()
        {
            if (_currentPlaylist == null) return;

            var audioFiles = _currentPlaylist.Where(f => !f.IsDirectory).Select(f => f.FullPath).ToList();

            _shuffledQueue = audioFiles.OrderBy(x => _random.Next()).ToList();

            if (!string.IsNullOrEmpty(_currentTrackPath) && _shuffledQueue.Contains(_currentTrackPath))
            {
                _shuffledQueue.Remove(_currentTrackPath);
                _shuffledQueue.Insert(0, _currentTrackPath);
            }
        }



        private void SyncPlayerWithExplorer()
        {
            if (FileBrowser != null)
            {
                FileBrowser.MarkAsPlaying(_currentTrackPath);
            }

            if (PlaylistsViewModel != null)
            {
                PlaylistsViewModel.MarkAsPlaying(_currentTrackPath);
            }

            if (SavedAlbumsViewModel != null)
            {
                SavedAlbumsViewModel.MarkAsPlaying(_currentTrackPath);
            }
        }

        private Color LerpColor(Color current, Color target, double t)
        {
            return Color.FromRgb(
                LerpByte(current.R, target.R, t),
                LerpByte(current.G, target.G, t),
                LerpByte(current.B, target.B, t)
            );
        }

        private byte LerpByte(byte current, byte target, double t)
        {
            if (current == target) return current;

            double diff = target - current;
            double step = diff * t;

            if (Math.Abs(step) < 1.0)
            {
                step = Math.Sign(diff);
            }

            return (byte)(current + step);
        }

        [RelayCommand]
        private async Task PlayPrev()
        {
            if (_currentPlaylist == null || _currentPlaylist.Count == 0) return;

            List<string> currentOrder = IsShuffleActive
                ? _shuffledQueue
                : _currentPlaylist.Where(f => !f.IsDirectory).Select(f => f.FullPath).ToList();

            if (currentOrder.Count == 0) return;

            int index = currentOrder.IndexOf(_currentTrackPath);

            if (index > 0)
            {
                AddToHistory(_currentTrackPath);
                _currentTrackPath = currentOrder[index - 1];       
                SyncPlayerWithExplorer();
                await Play();
            }
            else
            {
                await Play();
            }
        }

        private void ShowOsdIfNeeded()
        {
            if (!IsOsdEnabled || _isAppStarting || IsMiniPlayerActive) return;

            var mainWindow = Application.Current.MainWindow;
            if (mainWindow != null && (mainWindow.WindowState == WindowState.Minimized || !mainWindow.IsActive))
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_osdWindow == null)
                    {
                        _osdWindow = new JLS.Views.OsdWindow();
                        _osdWindow.DataContext = this;      
                    }

                    _osdWindow.UpdatePosition();
                    _osdWindow.Show();        
                    _osdWindow.ResetTimer();
                });
            }
        }

        [RelayCommand]
        private async Task TogglePlayPause()
        {
            if (IsPlaying)
                await Pause();
            else
                await Play();
        }

        [ObservableProperty]
        private bool _isSettingsVisible = false;     

        [RelayCommand]
        private void OpenSettings()
        {
            IsSettingsVisible = true;
            UpdateActiveSidebarItem();
        }

        [RelayCommand]
        private void CloseSettings()
        {
            IsSettingsVisible = false;
            UpdateActiveSidebarItem();
        }

        private async Task RestoreLastTrackAsync()
        {
            _currentPlaybackMode = Settings.Default.PlaybackMode;
            if (string.IsNullOrEmpty(_currentPlaybackMode)) _currentPlaybackMode = "Explorer";

            try
            {
                if (!string.IsNullOrEmpty(Settings.Default.SavedPlayHistory))
                {
                    try
                    {
                        var hist = System.Text.Json.JsonSerializer.Deserialize<List<HistoryRecord>>(Settings.Default.SavedPlayHistory);
                        if (hist != null) _playHistory = hist;
                    }
                    catch
                    {
                        try
                        {
                            var oldHist = System.Text.Json.JsonSerializer.Deserialize<List<string>>(Settings.Default.SavedPlayHistory);
                            if (oldHist != null)
                            {
                                _playHistory = oldHist.Select(p => new HistoryRecord { Path = p, PlayedAt = DateTime.Now }).ToList();
                            }
                        }
                        catch {        }
                    }
                }

                if (!string.IsNullOrEmpty(Settings.Default.SavedShuffledQueue))
                {
                    var shuf = System.Text.Json.JsonSerializer.Deserialize<List<string>>(Settings.Default.SavedShuffledQueue);
                    if (shuf != null) _shuffledQueue = shuf;
                }
            }
            catch {       }


            string lastTrack = Settings.Default.LastTrackPath;
            if (!string.IsNullOrEmpty(lastTrack) && System.IO.File.Exists(lastTrack))
            {
                _currentTrackPath = lastTrack;


                try
                {
                    if (_currentPlaybackMode == "Playlist")
                    {
                        var targetPlaylist = PlaylistManager.Playlists.FirstOrDefault(p =>
                            p.Name == Settings.Default.LastSelectedPlaylistName && p.TrackPaths.Contains(lastTrack));

                        if (targetPlaylist == null)
                            targetPlaylist = PlaylistManager.Playlists.FirstOrDefault(p => p.TrackPaths.Contains(lastTrack));

                        if (targetPlaylist != null)
                        {
                            _currentPlaylist = new ObservableCollection<FileSystemItem>(targetPlaylist.Items);
                        }
                    }
                    else
                    {
                        string? dir = System.IO.Path.GetDirectoryName(lastTrack);
                        if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                        {
                            var files = FileSystemService.GetDirectoryContents(dir);
                            _currentPlaylist = new ObservableCollection<FileSystemItem>(files.Where(f => !f.IsDirectory));
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Ошибка восстановления очереди: " + ex.Message);
                }
                UpdateMetadata(lastTrack);     

                _audioPlayer.SetVolume(0);

                await _audioPlayer.Play(lastTrack);

                await _audioPlayer.Pause();
                IsPlaying = false;

                double savedPos = Settings.Default.LastTrackPosition;
                if (savedPos > 0)
                {
                    await _audioPlayer.SetPosition(savedPos);

                    _isInternalSeekUpdate = true;
                    CurrentPosition = savedPos;
                    _precisePosition = savedPos;
                    _lastRenderedPosition = savedPos;
                    _isInternalSeekUpdate = false;
                    _lastTrackedPath = _currentTrackPath;
                }

                TotalDuration = _audioPlayer.GetDuration();
                UpdateTimeDisplay();

                _audioPlayer.SetVolume(Volume);

                SyncPlayerWithExplorer();
            }
            _isAppStarting = false;
        }

        public void SavePlaybackState()
        {
            FileBrowser?.SaveTreeState();

            Settings.Default.LastTrackPath = _currentTrackPath ?? "";
            Settings.Default.LastTrackPosition = CurrentPosition;
            Settings.Default.SavedVolume = Volume;

            string baseView = "Explorer";
            if (CurrentContent is PlaylistsViewModel) baseView = "Playlists";
            else if (CurrentContent is SavedAlbumsViewModel) baseView = "SavedAlbums";

            string viewToSave = IsNowPlayingVisible ? $"NowPlaying|{baseView}" : baseView;

            Settings.Default.LastOpenedView = viewToSave;

            Settings.Default.PlaybackMode = _currentPlaybackMode;
            Settings.Default.SavedPlayHistory = System.Text.Json.JsonSerializer.Serialize(_playHistory);
            Settings.Default.SavedShuffledQueue = System.Text.Json.JsonSerializer.Serialize(_shuffledQueue);
            Settings.Default.LastTimeDisplayMode = TimeDisplayMode.ToString();
            Settings.Default.IsShowTenthsEnabled = IsShowTenthsEnabled;

            Settings.Default.Save();
        }

        [RelayCommand]
        private async Task Play()
        {
            if (!string.IsNullOrEmpty(_currentTrackPath))
            {
                UpdateMetadata(_currentTrackPath);
                await _audioPlayer.Play(_currentTrackPath);
                TotalDuration = _audioPlayer.GetDuration();
                IsPlaying = true;
                UpdateTimeDisplay();

                bool isNewTrack = _currentTrackPath != _lastTrackedPath;

                if (isNewTrack)
                {
                    _isInternalSeekUpdate = true;
                    CurrentPosition = 0;
                    _precisePosition = 0;
                    _lastRenderedPosition = 0;
                    _lastTenthsCache = -1;
                    _isInternalSeekUpdate = false;
                    _actualSecondsListened = 0;

                    string format = System.IO.Path.GetExtension(_currentTrackPath).ToUpper().Replace(".", "");
                    int year = 0;
                    int.TryParse(TrackYear, out year);

                    JLS.Services.StatsTracker.Instance.SetTrack(_currentTrackPath, CurrentTrackName, ArtistName, AlbumName, format, TotalDuration, year);
                    _lastTrackedPath = _currentTrackPath;
                }

                JLS.Services.StatsTracker.Instance.Play();

                if (IsQueueOpen && isNewTrack)
                {
                    BuildQueue();
                }


                PrepareNextTrackInBg();
                ShowOsdIfNeeded();
            }
        }

        [RelayCommand]
        private async Task Pause()
        {
            await _audioPlayer.Pause();
            IsPlaying = false;

            JLS.Services.StatsTracker.Instance.Pause();     
            ShowOsdIfNeeded();
        }

        [ObservableProperty]
        private bool _isPlayerLogoHidden = false;

        partial void OnIsPlayerLogoHiddenChanged(bool value)
        {
            JLS.Properties.Settings.Default.IsPlayerLogoHidden = value;
            JLS.Properties.Settings.Default.Save();
        }

        [ObservableProperty]
        private System.Windows.Shell.TaskbarItemProgressState _taskbarProgressState = System.Windows.Shell.TaskbarItemProgressState.None;

        [ObservableProperty]
        private bool _isTaskbarProgressEnabled = true;

        partial void OnIsTaskbarProgressEnabledChanged(bool value)
        {
            JLS.Properties.Settings.Default.IsTaskbarProgressEnabled = value;
            JLS.Properties.Settings.Default.Save();
            UpdateTaskbarProgressState();
        }

        public void UpdateTaskbarProgressState()
        {
            if (IsTaskbarProgressEnabled)
            {
                TaskbarProgressState = IsPlaying ?
                    System.Windows.Shell.TaskbarItemProgressState.Normal :
                    System.Windows.Shell.TaskbarItemProgressState.Paused;
            }
            else
            {
                TaskbarProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
            }
        }

        public ImageSource PlayPauseIcon => IsPlaying
            ? (ImageSource)Application.Current.TryFindResource("TaskbarPauseIcon")
            : (ImageSource)Application.Current.TryFindResource("TaskbarPlayIcon");

        public string PlayPauseDescription => IsPlaying ? "Pause" : "Play";

        partial void OnIsPlayingChanged(bool value)
        {
            UpdateTaskbarProgressState();

            OnPropertyChanged(nameof(PlayPauseIcon));
            OnPropertyChanged(nameof(PlayPauseDescription));
        }

        public ImageSource RepeatIcon => RepeatMode switch
        {
            RepeatMode.One => (ImageSource)Application.Current.TryFindResource("TaskbarRepeatOneIcon"),
            RepeatMode.All => (ImageSource)Application.Current.TryFindResource("TaskbarRepeatActiveIcon"),
            _ => (ImageSource)Application.Current.TryFindResource("TaskbarRepeatIcon")
        };

        [RelayCommand]
        private void ToggleRepeat()
        {
            int nextMode = ((int)RepeatMode + 1) % 3;
            RepeatMode = (RepeatMode)nextMode;
            ShowOsdIfNeeded();
        }

        partial void OnRepeatModeChanged(RepeatMode value)
        {
            OnPropertyChanged(nameof(RepeatIcon));

            JLS.Properties.Settings.Default.RepeatMode = value.ToString();
            JLS.Properties.Settings.Default.Save();

            PrepareNextTrackInBg();
        }

        public ImageSource ShuffleIcon => IsShuffleActive
            ? (ImageSource)Application.Current.TryFindResource("TaskbarShuffleActiveIcon")
            : (ImageSource)Application.Current.TryFindResource("TaskbarShuffleIcon");

        [RelayCommand]
        private void ToggleShuffle()
        {
            IsShuffleActive = !IsShuffleActive;
            ShowOsdIfNeeded();
        }

        partial void OnIsShuffleActiveChanged(bool value)
        {
            if (value)
                GenerateShuffleQueue();
            else
                _shuffledQueue.Clear();

            OnPropertyChanged(nameof(ShuffleIcon));

            JLS.Properties.Settings.Default.IsShuffleActive = value;
            JLS.Properties.Settings.Default.Save();

            PrepareNextTrackInBg();

            if (IsQueueOpen)
            {
                BuildQueue();
            }
        }

        [ObservableProperty]
        private ObservableCollection<HotkeyItem> _hotkeysList = new();

        public HotkeyItem? RecordingItem { get; set; }

        [RelayCommand]
        private void StartRecordingHotkey(HotkeyItem item)
        {
            RecordingItem = item;
            item.DisplayText = "Press keys... (ESC to cancel)";
        }

        public void CancelRecording()
        {
            if (RecordingItem != null)
            {
                UpdateDisplayForHotkey(RecordingItem);
                RecordingItem = null;
            }
        }

        public void FinishRecording(uint modifiers, uint key, string display)
        {
            if (RecordingItem != null)
            {
                RecordingItem.Modifiers = modifiers;
                RecordingItem.Key = key;
                RecordingItem.DisplayText = display;

                SaveHotkeys();
                SetupAllHotkeys();      
                RecordingItem = null;
            }
        }

        private void UpdateDisplayForHotkey(HotkeyItem item)
        {
            if (item.Key == 0) item.DisplayText = "Unassigned";
            else
            {
                string display = "";
                if ((item.Modifiers & 0x0002) != 0) display += "Ctrl + ";
                if ((item.Modifiers & 0x0001) != 0) display += "Alt + ";
                if ((item.Modifiers & 0x0004) != 0) display += "Shift + ";
                display += System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)item.Key).ToString();
                item.DisplayText = display;
            }
        }

        private void SaveHotkeys()
        {
            var json = System.Text.Json.JsonSerializer.Serialize(HotkeysList);
            Settings.Default.HotkeysJson = json;
            Settings.Default.Save();
        }

        private const int HK_PLAY_PAUSE = 100;
        private const int HK_NEXT = 101;
        private const int HK_PREV = 102;
        private const int HK_VOL_UP = 103;
        private const int HK_VOL_DOWN = 104;
        private const int HK_SEEK_FWD = 105;
        private const int HK_SEEK_BWD = 106;
        private const int HK_OPEN_EXPLORER = 107;
        private const int HK_REPEAT = 108;
        private const int HK_SHUFFLE = 109;
        private const int HK_CLOSE = 110;

        public void SetupAllHotkeys()
        {
            if (HotkeysList.Count == 0) LoadHotkeys();

            var win = Application.Current.MainWindow as MainWindow;
            if (win == null) return;

            win.UnregisterGlobalHotkeys();   

            if (IsMediaKeysEnabled)
            {
                win.RegisterGlobalHotkey(900, 0, 0xB3);  
                win.RegisterGlobalHotkey(901, 0, 0xB0);   
                win.RegisterGlobalHotkey(902, 0, 0xB1);   
                win.RegisterGlobalHotkey(903, 0, 0xB2);  
            }

            if (IsGlobalHotkeysEnabled)
            {
                foreach (var hk in HotkeysList)
                {
                    if (hk.Key != 0)    
                        win.RegisterGlobalHotkey(hk.Id, hk.Modifiers, hk.Key);
                }
            }
        }

        public class UnifiedQueueItem
        {
            public bool IsHeader { get; set; }
            public string HeaderText { get; set; } = string.Empty;

            public bool IsTrack { get; set; }
            public bool IsPlaying { get; set; }
            public FileSystemItem? Track { get; set; }
        }

        private void LoadHotkeys()
        {
            string json = Settings.Default.HotkeysJson;
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var savedList = System.Text.Json.JsonSerializer.Deserialize<ObservableCollection<HotkeyItem>>(json);
                    if (savedList != null && savedList.Count > 0)
                    {
                        HotkeysList = savedList;
                        foreach (var hk in HotkeysList) UpdateDisplayForHotkey(hk);
                        return;
                    }
                }
                catch {        }
            }

            HotkeysList = new ObservableCollection<HotkeyItem>
            {
                new HotkeyItem { Id = HK_PLAY_PAUSE, ActionName = "Play / Pause", Modifiers = 0x0002 | 0x0001, Key = 0x20, DisplayText = "Ctrl + Alt + Space" },
                new HotkeyItem { Id = HK_NEXT, ActionName = "Next Track", Modifiers = 0x0002 | 0x0001, Key = 0x27, DisplayText = "Ctrl + Alt + Right" },
                new HotkeyItem { Id = HK_PREV, ActionName = "Previous Track", Modifiers = 0x0002 | 0x0001, Key = 0x25, DisplayText = "Ctrl + Alt + Left" },
                new HotkeyItem { Id = HK_VOL_UP, ActionName = "Volume Up", Modifiers = 0x0002 | 0x0001, Key = 0x26, DisplayText = "Ctrl + Alt + Up" },
                new HotkeyItem { Id = HK_VOL_DOWN, ActionName = "Volume Down", Modifiers = 0x0002 | 0x0001, Key = 0x28, DisplayText = "Ctrl + Alt + Down" },
                new HotkeyItem { Id = HK_SEEK_FWD, ActionName = "Seek Forward", Modifiers = 0, Key = 0, DisplayText = "Unassigned" },
                new HotkeyItem { Id = HK_SEEK_BWD, ActionName = "Seek Backward", Modifiers = 0, Key = 0, DisplayText = "Unassigned" },
                new HotkeyItem { Id = HK_OPEN_EXPLORER, ActionName = "Open in Explorer", Modifiers = 0, Key = 0, DisplayText = "Unassigned" },
                new HotkeyItem { Id = HK_REPEAT, ActionName = "Toggle Repeat", Modifiers = 0, Key = 0, DisplayText = "Unassigned" },
                new HotkeyItem { Id = HK_SHUFFLE, ActionName = "Toggle Shuffle", Modifiers = 0, Key = 0, DisplayText = "Unassigned" },
                new HotkeyItem { Id = HK_CLOSE, ActionName = "Close Player", Modifiers = 0, Key = 0, DisplayText = "Unassigned" }
            };
        }

        public void HandleGlobalHotkey(int id)
        {
            switch (id)
            {
                case 900: TogglePlayPauseCommand.Execute(null); break;
                case 901: PlayNextCommand.Execute(null); break;
                case 902: PlayPrevCommand.Execute(null); break;
                case 903: if (IsPlaying) TogglePlayPauseCommand.Execute(null); break;

                case HK_PLAY_PAUSE: TogglePlayPauseCommand.Execute(null); break;
                case HK_NEXT: PlayNextCommand.Execute(null); break;
                case HK_PREV: PlayPrevCommand.Execute(null); break;
                case HK_VOL_UP: VolumeUpCommand.Execute(null); break;
                case HK_VOL_DOWN: VolumeDownCommand.Execute(null); break;
                case HK_SEEK_FWD: SeekForwardCommand.Execute(null); break;
                case HK_SEEK_BWD: SeekBackwardCommand.Execute(null); break;
                case HK_OPEN_EXPLORER: OpenTrackLocationInExplorerCommand.Execute(null); break;
                case HK_REPEAT: ToggleRepeatCommand.Execute(null); break;
                case HK_SHUFFLE: ToggleShuffleCommand.Execute(null); break;
                case HK_CLOSE: CloseApplicationCommand.Execute(null); break;
            }
        }

        public void StartDragging(double clickPosition)
        {
            IsDraggingTrack = true;
            _dragTargetPosition = clickPosition;        
            _isCatchingUp = false; 
        }

        public void UpdateDragPosition(double newPosition)
        {
            _dragTargetPosition = -1;           
            _precisePosition = newPosition;
            _lastRenderedPosition = newPosition;

            _isInternalSeekUpdate = true;
            CurrentPosition = newPosition;      
            _isInternalSeekUpdate = false;
        }

        public void SyncPrecisePosition(double pos)
        {
            _precisePosition = pos;
            _lastRenderedPosition = pos;
        }

        public void ApplySeek(double newPosition, bool isSourceDragging)
        {
            _dragTargetPosition = -1;

            if (_audioPlayer != null)
            {
                _audioPlayer.SetPosition(newPosition);

                _lastUserSeekTime = DateTime.Now;
                _targetSeekPosition = newPosition;

                if (isSourceDragging)
                {
                    _isInternalSeekUpdate = true;
                    CurrentPosition = newPosition;
                    _precisePosition = newPosition;
                    _lastRenderedPosition = newPosition;
                    _isInternalSeekUpdate = false;

                    WaveformProgress = TotalDuration > 0 ? newPosition / TotalDuration : 0;
                    BubbleX = WaveformWidth * WaveformProgress;

                    _isCatchingUp = false;      
                }
                else
                {
                    _isCatchingUp = true;
                }
            }
        }

        

        private void LoadAndParseLyrics(string trackPath)
        {
            string plainText = JLS.Services.LyricsService.GetPlainLyrics(trackPath);
            string syncedRaw = JLS.Services.LyricsService.GetSyncedLyrics(trackPath);
            bool preferSynced = JLS.Services.LyricsService.GetSyncedModePreference(trackPath);

            SyncedLyrics.Clear();
            if (!string.IsNullOrWhiteSpace(syncedRaw))
            {
                ParseLrcToCollection(syncedRaw);
            }

            EditableLyrics = plainText ?? string.Empty;
            CurrentLyrics = plainText ?? string.Empty;

            bool hasPlain = !string.IsNullOrWhiteSpace(plainText);
            bool hasSynced = SyncedLyrics.Count > 0;

            if (hasSynced && !hasPlain)
            {
                IsSyncedLyricsMode = true;
            }
            else if (hasPlain && !hasSynced)
            {
                IsSyncedLyricsMode = false;
            }
            else
            {
                IsSyncedLyricsMode = preferSynced;
            }

            OnPropertyChanged(nameof(HasLyrics));
            OnPropertyChanged(nameof(ShowEnableSyncMessage));
            OnPropertyChanged(nameof(ShowAddSyncedLyricsMessage));
            EvaluateCountdown();
        }

        [RelayCommand]
        private void ImportLrcFile()
        {
            if (string.IsNullOrEmpty(_currentTrackPath)) return;

            try
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "LRC files (*.lrc)|*.lrc",
                    Title = "Select LRC File"
                };

                string? trackDirectory = System.IO.Path.GetDirectoryName(_currentTrackPath);
                if (!string.IsNullOrEmpty(trackDirectory) && System.IO.Directory.Exists(trackDirectory))
                {
                    openFileDialog.InitialDirectory = trackDirectory;
                }

                if (openFileDialog.ShowDialog() == true)
                {
                    string fileContent = System.IO.File.ReadAllText(openFileDialog.FileName);

                    EditorSyncedLyrics.Clear();

                    ParseLrcToCollection(fileContent, isImporting: true);

                    IsSyncedLyricsMode = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Ошибка импорта LRC файла: " + ex.Message);
            }
        }

        private void ParseLrcToCollection(string lrcText, bool isImporting = false)
        {
            var lines = lrcText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("[") && line.IndexOf(']') > 0)
                {
                    try
                    {
                        int bracketIndex = line.IndexOf(']');
                        string timePart = line.Substring(1, bracketIndex - 1);
                        string textPart = line.Substring(bracketIndex + 1).Trim();

                        var parts = timePart.Split(':');
                        if (parts.Length >= 2)
                        {
                            int min = int.Parse(parts[0]);
                            double sec = double.Parse(parts[1].Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
                            double totalSec = (min * 60) + sec;

                            textPart = textPart.Replace("\\n", "\n");

                            var newLyricLine = new LyricLine
                            {
                                Timestamp = totalSec,
                                TimestampText = $"[{timePart}]",
                                Text = textPart
                            };

                            if (isImporting)
                            {
                                EditorSyncedLyrics.Add(newLyricLine);      
                            }
                            else
                            {
                                SyncedLyrics.Add(newLyricLine);      
                            }
                        }
                    }
                    catch {      }
                }
            }
        }

        private void PrepareEditorLyricsGrid()
        {
            EditorSyncedLyrics.Clear();
            foreach (var line in SyncedLyrics)
            {
                if (line.Text == "COUNTDOWN_ANIMATION") continue;

                EditorSyncedLyrics.Add(new LyricLine
                {
                    Timestamp = line.Timestamp,
                    TimestampText = line.TimestampText,
                    Text = line.Text
                });
            }
        }

        [ObservableProperty]
        private bool _isResetPopupOpen = false;

        [RelayCommand]
        private void ToggleResetPopup()
        {
            IsResetPopupOpen = !IsResetPopupOpen;
        }

        [RelayCommand]
        private void ResetAudio()
        {
            IsResetPopupOpen = false;   

            var dialog = new JLS.Views.ConfirmDialog("Reset Audio Settings", "Are you sure you want to reset audio settings to default?", "Reset");
            dialog.Owner = Application.Current.MainWindow;
            if (dialog.ShowDialog() == true)
            {
                Volume = 0.5;
                SelectedTransitionMode = TransitionMode.Normal;
                TransitionDuration = 3.0;
                IsGaplessPlaybackEnabled = false;
                IsSkipSilenceEnabled = false;

                Properties.Settings.Default.SavedVolume = 0.5;
                Properties.Settings.Default.SelectedTransitionMode = TransitionMode.Normal.ToString();
                Properties.Settings.Default.TransitionDuration = 3.0;
                Properties.Settings.Default.IsGaplessPlaybackEnabled = false;
                Properties.Settings.Default.IsSkipSilenceEnabled = false;
                Properties.Settings.Default.Save();

                if (_audioPlayer != null) _audioPlayer.SetVolume(0.5);
            }
        }

        [RelayCommand]
        private void ResetInterface()
        {
            IsResetPopupOpen = false;

            var dialog = new JLS.Views.ConfirmDialog("Reset Interface", "Are you sure you want to reset all interface settings to default?", "Reset");
            dialog.Owner = Application.Current.MainWindow;
            if (dialog.ShowDialog() == true)
            {
                UiScale = 1.0;
                SelectedColorExtractionAlgorithm = ColorExtractionAlgorithm.SmartAccent;
                IsSmoothScrollEnabled = true;
                IsFullscreenF10Enabled = true;
                IsFpsLimited = false;
                TargetFps = 60;
                IsOptimizedWindowModeEnabled = false;
                IsMinimalBottomPanelEnabled = false;
                IsPlaylistMinimalModeEnabled = false;
                IsGlassModeEnabled = false;
                IsVuMeterVisible = true;
                IsHorizontalVolumeEnabled = false;
                IsShowTenthsEnabled = true;
                IsAutoHideControlsEnabled = false;
                SelectedControlStyle = PlayerControlStyle.Waveform;
                SelectedWaveformStyle = WaveformStyle.Dj;
                IsNowPlayingFluidBackgroundEnabled = true;
                IsNowPlayingFluidAudioReactiveEnabled = true;
                IsCleanModeEnabled = false;
                IsPlayerLogoHidden = false;
                SelectedBeautifulLyricsFont = BeautifulLyricsFont.Default;
                IsAlwaysShowTrackTimingsEnabled = false;

                TreeSpacing = "Medium";
                TreeFontSize = "Medium";
                ListSpacing = "Medium";
                ListFontSize = "Medium";

                Properties.Settings.Default.Save();
            }
        }

        [RelayCommand]
        private void ResetHotkeys()
        {
            IsResetPopupOpen = false;

            var dialog = new JLS.Views.ConfirmDialog("Reset Hotkeys", "Are you sure you want to reset all hotkeys to their default bindings?", "Reset");
            dialog.Owner = Application.Current.MainWindow;
            if (dialog.ShowDialog() == true)
            {
                IsGlobalHotkeysEnabled = true;
                IsMediaKeysEnabled = true;

                HotkeySkipForwardSeconds = 5;
                HotkeySkipBackwardSeconds = 5;
                HotkeyVolumeUpPercent = 5;
                HotkeyVolumeDownPercent = 5;

                Properties.Settings.Default.HotkeysJson = string.Empty;
                Properties.Settings.Default.Save();

                LoadHotkeys();
                SetupAllHotkeys();
            }
        }

    }
}
