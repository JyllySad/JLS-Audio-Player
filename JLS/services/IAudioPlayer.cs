namespace JLS.Services
{
    public interface IAudioPlayer
    {
        void Initialize(JLS.Models.AudioDevice device);
        Task Play(string filePath);
        Task Pause();
        void Stop();

        void SetVolume(double volume);
        Task SetPosition(double seconds);
        double GetPosition();
        double GetDuration();
        int GetHandle();
        void GetVULevels(double[] buffer);

        float[] GetFFTData();

        float[] GetWaveData();

        float[] GetStereoFFTData();

        void SetNextTrack(string filePath);    

        event Action TrackEnded;      

        bool IsGaplessEnabled { get; set; }
    }
}