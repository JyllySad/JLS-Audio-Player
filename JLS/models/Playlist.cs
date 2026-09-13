using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace JLS.Models
{
    public partial class Playlist : ObservableObject
    {
        [ObservableProperty]
        private Guid _id = Guid.NewGuid();

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _colorHex = "#333333"; 

        [ObservableProperty]
        private string _darkBackgroundColorHex = "Transparent";

        [JsonIgnore]
        [ObservableProperty]
        private ObservableCollection<FileSystemItem> _items = new();

        [ObservableProperty]
        private double _hue = 120;

        [ObservableProperty]
        private double _saturation = 1.0; 

        [ObservableProperty]
        private double _lightness = 0.5; 

        [ObservableProperty]
        private string? _coverPath;

        partial void OnCoverPathChanged(string? value)
        {
            OnPropertyChanged(nameof(HasCover));
        }

        public bool HasCover => !string.IsNullOrEmpty(CoverPath);

        [ObservableProperty]
        private bool _useCoverColor = true;

        [ObservableProperty]
        private bool _showCover = true;

        public double SavedUserHue { get; set; } = 120;
        public double SavedUserSaturation { get; set; } = 1.0;
        public double SavedUserLightness { get; set; } = 0.5;

        private bool _isAnimatingColor = false;
        private int _animationId = 0;

        public async System.Threading.Tasks.Task AnimateColorsTo(double targetH, double targetS, double targetL)
        {
            _isAnimatingColor = true;
            int currentAnimId = ++_animationId;

            double startH = Hue;
            double startS = Saturation;
            double startL = Lightness;

            double diffH = targetH - startH;
            if (diffH > 180) diffH -= 360;
            else if (diffH < -180) diffH += 360;

            int steps = 25;
            int delay = 10;

            for (int i = 1; i <= steps; i++)
            {
                if (currentAnimId != _animationId) return;

                double progress = (double)i / steps;
                double easeOut = 1 - Math.Pow(1 - progress, 3);

                double newH = (startH + diffH * easeOut) % 360;
                if (newH < 0) newH += 360;

                Hue = newH;
                Saturation = startS + (targetS - startS) * easeOut;
                Lightness = startL + (targetL - startL) * easeOut;

                await System.Threading.Tasks.Task.Delay(delay);
            }

            if (currentAnimId == _animationId)
            {
                Hue = targetH;
                Saturation = targetS;
                Lightness = targetL;
                _isAnimatingColor = false;
            }
        }

        partial void OnHueChanged(double value)
        {
            if (!UseCoverColor && !_isAnimatingColor) SavedUserHue = value;
            UpdateColors();
        }
        partial void OnSaturationChanged(double value)
        {
            if (!UseCoverColor && !_isAnimatingColor) SavedUserSaturation = value;
            UpdateColors();
        }
        partial void OnLightnessChanged(double value)
        {
            if (!UseCoverColor && !_isAnimatingColor) SavedUserLightness = value;
            UpdateColors();
        }

        public void UpdateColors()
        {
            var mainColor = HslToRgb(Hue, Saturation, Lightness);
            ColorHex = $"#{mainColor.R:X2}{mainColor.G:X2}{mainColor.B:X2}";

            double darkLightness = Math.Min(Lightness, 0.12);
            var darkColor = HslToRgb(Hue, Saturation, darkLightness);
            DarkBackgroundColorHex = $"#{darkColor.R:X2}{darkColor.G:X2}{darkColor.B:X2}";
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

        public List<string> TrackPaths { get; set; } = new();

        public List<PlaylistSavedTrack> SavedTracks { get; set; } = new();
    }

    public class PlaylistSavedTrack
    {
        public string FullPath { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public string? Year { get; set; }
        public int? TrackNumber { get; set; }
    }
}

    
