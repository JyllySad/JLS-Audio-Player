using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;

namespace JLS.Models
{
    public partial class LyricLine : ObservableObject
    {
        [ObservableProperty]
        private double _timestamp;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Timestamp))]
        private string _timestampText = string.Empty;

        [ObservableProperty]
        private string _text = string.Empty;

        [ObservableProperty]
        private bool _isActive;

        [ObservableProperty]
        private bool _isPassed;

        [ObservableProperty]
        private bool _isNext;

        partial void OnTimestampTextChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            if (value.StartsWith("[") && value.Contains("]"))
            {
                try
                {
                    int bracketIndex = value.IndexOf(']');
                    string timePart = value.Substring(1, bracketIndex - 1);
                    var parts = timePart.Split(':');

                    if (parts.Length >= 2)
                    {
                        int min = int.Parse(parts[0]);
                        double sec = double.Parse(parts[1].Replace(',', '.'), CultureInfo.InvariantCulture);

                        Timestamp = (min * 60) + sec;
                    }
                }
                catch
                {
                    
                }
            }
        }
    }
}