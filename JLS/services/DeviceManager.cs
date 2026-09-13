using System.Collections.Generic;
using ManagedBass;
using ManagedBass.Wasapi;
using ManagedBass.Asio;
using JLS.Models;          

namespace JLS.Services
{
    public class DeviceManager
    {
        public List<AudioDevice> GetAvailableDevices()
        {
            var devices = new List<AudioDevice>();

            for (int i = 1; i < Bass.DeviceCount; i++)
            {
                var info = Bass.GetDeviceInfo(i);
                if (info.IsEnabled)
                {
                    devices.Add(new AudioDevice
                    {
                        DeviceIndex = i,
                        Name = info.Name,
                        DriverType = "Standard",
                        IsDefault = info.IsDefault
                    });
                }
            }

            for (int i = 0; i < BassWasapi.DeviceCount; i++)
            {
                var info = BassWasapi.GetDeviceInfo(i);
                if (info.IsEnabled && !info.IsLoopback && !info.IsInput)
                {
                    devices.Add(new AudioDevice
                    {
                        DeviceIndex = i,
                        Name = info.Name,
                        DriverType = "WASAPI (Shared)",
                        IsDefault = info.IsDefault
                    });

                    devices.Add(new AudioDevice
                    {
                        DeviceIndex = i,
                        Name = info.Name,
                        DriverType = "WASAPI (Exclusive)",
                        IsDefault = false
                    });
                }
            }

            try
            {
                for (int i = 0; i < BassAsio.DeviceCount; i++)
                {
                    var info = BassAsio.GetDeviceInfo(i);
                    devices.Add(new AudioDevice
                    {
                        DeviceIndex = i,
                        Name = info.Name,
                        DriverType = "ASIO",
                        IsDefault = false       
                    });
                }
            }
            catch
            {
            }

            return devices;
        }
    }
}