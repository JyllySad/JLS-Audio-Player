namespace JLS.Models
{
    public class AudioDevice
    {
        public int DeviceIndex { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DriverType { get; set; } = string.Empty;
        public bool IsDefault { get; set; }

        public override string ToString()
        {
            return $"[{DriverType}] {Name}";
        }
    }
}