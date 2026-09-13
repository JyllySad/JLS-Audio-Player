using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace JLS.ViewModels
{
    public class EasterEggViewModel : INotifyPropertyChanged
    {
        private bool _isEasterEggVisible;
        private int _wallHits;
        private int _cornerHits;

        public bool IsEasterEggVisible
        {
            get => _isEasterEggVisible;
            set { _isEasterEggVisible = value; OnPropertyChanged(); }
        }

        public int WallHits
        {
            get => _wallHits;
            set { _wallHits = value; OnPropertyChanged(); }
        }

        public int CornerHits
        {
            get => _cornerHits;
            set { _cornerHits = value; OnPropertyChanged(); }
        }

        public void ResetStats()
        {
            WallHits = 0;
            CornerHits = 0;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}