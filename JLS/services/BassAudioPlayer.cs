using JLS.Models;
using JLS.ViewModels;
using ManagedBass;
using ManagedBass.Asio;
using ManagedBass.Mix;
using ManagedBass.Wasapi;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace JLS.Services
{
    public class BassAudioPlayer : IAudioPlayer
    {
        private int _streamHandle;
        private int _mixerHandle;
        private AudioDevice? _currentDevice;
        private string _currentFilePath = string.Empty;

        private WasapiProcedure _wasapiProcedure;
        private AsioProcedure _asioProcedure;
        private WasapiNotifyProcedure _wasapiNotifyProc;
        public event Action? TrackEnded;
        private SyncProcedure _endSyncProc;
        private SyncProcedure _cleanUpSyncProc;

        private float _currentVolume = 0.5f;     
        private bool _isSeeking = false;          

        private int _nextStreamHandle;
        private string _nextFilePath = string.Empty;
        private int _fadingStreamHandle;

        private readonly float[] _fftBuffer = new float[1024];
        private readonly float[] _waveBuffer = new float[2048];

        private readonly float[] _stereoFftBuffer = new float[2048];

        public TransitionMode TransitionType { get; set; } = TransitionMode.Normal;
        public int TransitionDurationMs { get; set; } = 3000;      

        private SyncProcedure _transitionSyncProc;
        private int _transitionSyncHandle = 0;

        public float[] GetStereoFFTData()
        {
            if (_streamHandle == 0) return _stereoFftBuffer;

            int flags = (int)DataFlags.FFT2048 | (int)DataFlags.FFTIndividual;

            if (_currentDevice?.DriverType == "Standard")
                Bass.ChannelGetData(_streamHandle, _stereoFftBuffer, flags);
            else
                BassMix.ChannelGetData(_streamHandle, _stereoFftBuffer, flags);

            return _stereoFftBuffer;
        }

        public bool IsGaplessEnabled { get; set; } = false;

        public bool IsSkipSilenceEnabled { get; set; } = false;

        private float _silenceThreshold = 0.0001f;      

        private double _skipSilencePreRoll = 0.15;        
        private double _skipSilencePostRoll = 0.7;           

        private long _currentEffectiveStartBytes = 0;
        private long _currentEffectiveEndBytes = 0;
        private long _currentActualAudioEndBytes = 0;
        private long _nextEffectiveStartBytes = 0;
        private long _nextEffectiveEndBytes = 0;
        private long _nextActualAudioEndBytes = 0;

        public BassAudioPlayer()
        {
            Bass.Configure(Configuration.AsyncFileBufferLength, 2097152);

            Bass.PluginLoad("bassflac.dll");
            Bass.PluginLoad("bass_aac.dll");     
            Bass.PluginLoad("basswma.dll");    
            Bass.PluginLoad("bass_alac.dll");    
            Bass.PluginLoad("bass_ape.dll");     

            _wasapiProcedure = new WasapiProcedure(ProcessWasapi);
            _asioProcedure = new AsioProcedure(ProcessAsio);
            _wasapiNotifyProc = new WasapiNotifyProcedure(WasapiDeviceNotify);
            _endSyncProc = new SyncProcedure(OnTrackEnd);
            _cleanUpSyncProc = new SyncProcedure(CleanUpOldTrack);
            _transitionSyncProc = new SyncProcedure(OnTransitionTrigger);
        }

        public void Initialize(AudioDevice device)
        {
            Stop();
            FreeDevice();

            _currentDevice = device;
            int deviceFreq = 44100;
            int deviceChans = 2;

            if (device.DriverType == "Standard")
            {
                if (!Bass.Init(device.DeviceIndex, 44100, DeviceInitFlags.Default, IntPtr.Zero))
                    throw new Exception($"BASS Init Error: {Bass.LastError}");

            }
            else
            {
                if (!Bass.Init(0, 44100, DeviceInitFlags.Default, IntPtr.Zero))
                    throw new Exception($"BASS NoSound Init Error: {Bass.LastError}");

                if (device.DriverType.StartsWith("WASAPI"))
                {
                    var flags = WasapiInitFlags.AutoFormat;
                    float bufferLength = 0.05f;
                    float period = 0.01f;

                    if (device.DriverType.Contains("Exclusive"))
                    {
                        flags |= WasapiInitFlags.Exclusive | (WasapiInitFlags)16;
                        bufferLength = 0f;
                        period = 0f;
                    }
                    else { flags |= WasapiInitFlags.Shared; }

                    if (!BassWasapi.Init(device.DeviceIndex, 0, 0, flags, bufferLength, period, _wasapiProcedure, IntPtr.Zero))
                        throw new Exception($"WASAPI Init Error: {Bass.LastError}");

                    BassWasapi.SetNotify(_wasapiNotifyProc);

                    var info = BassWasapi.Info;
                    deviceFreq = info.Frequency;
                    deviceChans = info.Channels;
                }
                else if (device.DriverType == "ASIO")
                {
                    if (!BassAsio.Init(device.DeviceIndex, AsioInitFlags.Thread))
                        throw new Exception($"ASIO Init Error: {Bass.LastError}");

                    deviceFreq = (int)BassAsio.Rate;
                    deviceChans = 2;
                }

                _mixerHandle = BassMix.CreateMixerStream(deviceFreq, deviceChans, BassFlags.Decode | BassFlags.Float);

                if (device.DriverType.StartsWith("WASAPI"))
                {
                    BassWasapi.Start();
                }
                else if (device.DriverType == "ASIO")
                {
                    BassAsio.ChannelEnable(false, 0, _asioProcedure, IntPtr.Zero);
                    BassAsio.ChannelEnable(false, 1, _asioProcedure, IntPtr.Zero);
                    BassAsio.ChannelJoin(false, 1, 0);
                    BassAsio.ChannelSetFormat(false, 0, AsioSampleFormat.Float);
                    BassAsio.ChannelSetRate(false, 0, deviceFreq);
                    BassAsio.Start(0);
                }
            }
        }

        public async Task Play(string filePath)
        {
            if (_streamHandle != 0 && _currentFilePath == filePath)
            {
                if (_currentDevice?.DriverType == "Standard")
                {
                    if (Bass.ChannelIsActive(_streamHandle) == PlaybackState.Paused)
                    {
                        Bass.ChannelSetAttribute(_streamHandle, ChannelAttribute.Volume, 0f);
                        Bass.ChannelPlay(_streamHandle);
                        Bass.ChannelSlideAttribute(_streamHandle, ChannelAttribute.Volume, _currentVolume, 50);
                        return;
                    }
                }
                else
                {
                    int flags = (int)BassMix.ChannelFlags(_streamHandle, 0, 0);
                    if ((flags & (int)BassFlags.MixerChanPause) != 0)
                    {
                        Bass.ChannelSetAttribute(_streamHandle, ChannelAttribute.Volume, 0f);
                        BassMix.ChannelFlags(_streamHandle, BassFlags.Default, BassFlags.MixerChanPause);
                        Bass.ChannelSlideAttribute(_streamHandle, ChannelAttribute.Volume, _currentVolume, 50);
                        return;
                    }
                }
            }

            bool wasAlreadyStartedByGapless = false;
            int newStreamToUse = 0;

            if (filePath == _nextFilePath && _nextStreamHandle != 0)
            {
                newStreamToUse = _nextStreamHandle;

                if (_currentDevice?.DriverType == "Standard")
                {
                    if (Bass.ChannelIsActive(newStreamToUse) == PlaybackState.Playing)
                        wasAlreadyStartedByGapless = true;
                }
                else
                {
                    BassFlags mixFlags = BassMix.ChannelFlags(newStreamToUse, 0, 0);
                    bool isPaused = mixFlags.HasFlag(BassFlags.MixerChanPause);

                    if (BassMix.ChannelGetMixer(newStreamToUse) != 0 && !isPaused)
                    {
                        wasAlreadyStartedByGapless = true;
                    }
                }
            }

            if (_streamHandle != 0)
            {
                int trackToFree = _streamHandle;
                _streamHandle = 0;

                _ = Task.Run(async () =>
                {
                    if (!wasAlreadyStartedByGapless)
                    {
                        Bass.ChannelSlideAttribute(trackToFree, ChannelAttribute.Volume, 0f, 60);
                        await Task.Delay(100);
                    }
                    else
                    {
                        if (TransitionType == TransitionMode.Crossfade)
                        {
                            await Task.Delay(TransitionDurationMs + 200);
                        }
                        else
                        {
                            await Task.Delay(500);
                        }
                    }

                    if (_currentDevice?.DriverType != "Standard") BassMix.MixerRemoveChannel(trackToFree);
                    Bass.StreamFree(trackToFree);       
                });
            }

            _currentFilePath = filePath;

            if (newStreamToUse != 0)
            {
                _streamHandle = newStreamToUse;
                _currentEffectiveStartBytes = _nextEffectiveStartBytes;
                _currentEffectiveEndBytes = _nextEffectiveEndBytes;
                _currentActualAudioEndBytes = _nextActualAudioEndBytes;

                _nextStreamHandle = 0;
                _nextFilePath = string.Empty;
            }
            else
            {
                string safePath = PathHelper.GetSafePath(filePath);

                var scanFlags = BassFlags.Decode | BassFlags.Float | BassFlags.Prescan;
                int scanHandle = Bass.CreateStream(safePath, 0, 0, scanFlags);

                if (scanHandle != 0)
                {
                    if (IsSkipSilenceEnabled)
                    {
                        _currentEffectiveStartBytes = ScanForTrueStart(scanHandle);
                        _currentEffectiveEndBytes = ScanForTrueEnd(scanHandle, out _currentActualAudioEndBytes);
                    }
                    else
                    {
                        _currentEffectiveStartBytes = 0;
                        _currentEffectiveEndBytes = Bass.ChannelGetLength(scanHandle);
                        _currentActualAudioEndBytes = _currentEffectiveEndBytes;
                    }

                    Bass.StreamFree(scanHandle);
                }

                var streamFlags = _currentDevice?.DriverType == "Standard"
                    ? (BassFlags.Default | BassFlags.AsyncFile | BassFlags.Prescan)
                    : (BassFlags.Decode | BassFlags.Float | BassFlags.AsyncFile | BassFlags.Prescan);

                _streamHandle = Bass.CreateStream(safePath, 0, 0, streamFlags);

                if (_streamHandle != 0)
                {
                    Bass.ChannelSetPosition(_streamHandle, _currentEffectiveStartBytes);
                }
            }

            if (_streamHandle == 0) return;

            long lengthBytes = _currentEffectiveEndBytes;
            long actualAudioEndBytes = _currentActualAudioEndBytes;

            if (TransitionType == TransitionMode.Crossfade || TransitionType == TransitionMode.FadeInOut)
            {
                long syncPos = actualAudioEndBytes - Bass.ChannelSeconds2Bytes(_streamHandle, TransitionDurationMs / 1000.0);
                if (syncPos < 0) syncPos = actualAudioEndBytes;

                if (_transitionSyncHandle != 0) Bass.ChannelRemoveSync(_streamHandle, _transitionSyncHandle);
                _transitionSyncHandle = Bass.ChannelSetSync(_streamHandle, SyncFlags.Position | SyncFlags.Mixtime, syncPos, _transitionSyncProc);

                if (IsSkipSilenceEnabled)
                {
                    Bass.ChannelSetSync(_streamHandle, SyncFlags.Position | SyncFlags.Mixtime, actualAudioEndBytes, _endSyncProc);
                }
                else
                {
                    Bass.ChannelSetSync(_streamHandle, SyncFlags.End | SyncFlags.Mixtime, 0, _endSyncProc);
                }
            }

            if (!wasAlreadyStartedByGapless)
            {
                if (TransitionType == TransitionMode.Crossfade || TransitionType == TransitionMode.FadeInOut)
                {
                    Bass.ChannelSetAttribute(_streamHandle, ChannelAttribute.Volume, 0f);
                }
                else if (IsGaplessEnabled)
                {
                    long offset = Bass.ChannelSeconds2Bytes(_streamHandle, 0.015);

                    long syncPos = IsSkipSilenceEnabled ? (_currentActualAudioEndBytes - offset) : (lengthBytes - offset);
                    if (syncPos < 0) syncPos = lengthBytes;

                    Bass.ChannelSetSync(_streamHandle, SyncFlags.Position | SyncFlags.Mixtime, syncPos, _endSyncProc);
                    Bass.ChannelSetAttribute(_streamHandle, ChannelAttribute.Volume, 0f);
                }
                else
                {
                    long offset = Bass.ChannelSeconds2Bytes(_streamHandle, 0.140);

                    long syncPos = IsSkipSilenceEnabled ? (_currentActualAudioEndBytes - offset) : (lengthBytes - offset);
                    if (syncPos < 0) syncPos = lengthBytes;

                    Bass.ChannelSetSync(_streamHandle, SyncFlags.Position, syncPos, _endSyncProc);
                    Bass.ChannelSetAttribute(_streamHandle, ChannelAttribute.Volume, 0f);
                }

                if (_currentDevice?.DriverType == "Standard")
                {
                    Bass.ChannelPlay(_streamHandle);
                }
                else
                {
                    if (BassMix.ChannelGetMixer(_streamHandle) == _mixerHandle)
                    {
                        BassMix.ChannelFlags(_streamHandle, 0, BassFlags.MixerChanPause);
                    }
                    else
                    {
                        var flags = BassFlags.Default | BassFlags.MixerChanBuffer;
                        if (IsGaplessEnabled && TransitionType == TransitionMode.Normal) flags |= (BassFlags)0x800000;  
                        BassMix.MixerAddChannel(_mixerHandle, _streamHandle, flags);
                    }
                }

                if (TransitionType == TransitionMode.Crossfade || TransitionType == TransitionMode.FadeInOut)
                {
                    Bass.ChannelSlideAttribute(_streamHandle, ChannelAttribute.Volume, _currentVolume, TransitionDurationMs);
                }
                else
                {
                    Bass.ChannelSlideAttribute(_streamHandle, ChannelAttribute.Volume, _currentVolume, 60);  
                }
            }
        }

        private void OnTransitionTrigger(int handle, int channel, int data, IntPtr user)
        {
            Bass.ChannelSlideAttribute(channel, ChannelAttribute.Volume, 0f, TransitionDurationMs);

            if (TransitionType == TransitionMode.Crossfade && _nextStreamHandle != 0)
            {
                if (_mixerHandle != 0)
                {
                    BassMix.MixerAddChannel(_mixerHandle, _nextStreamHandle, BassFlags.MixerChanBuffer);
                }
                else if (_currentDevice?.DriverType == "Standard")
                {
                    Bass.ChannelPlay(_nextStreamHandle);
                }

                Bass.ChannelSetAttribute(_nextStreamHandle, ChannelAttribute.Volume, 0f);
                Bass.ChannelSlideAttribute(_nextStreamHandle, ChannelAttribute.Volume, _currentVolume, TransitionDurationMs);

                Application.Current.Dispatcher.BeginInvoke(new Action(() => TrackEnded?.Invoke()));
            }
        }

        public void SetNextTrack(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                if (_nextStreamHandle != 0)
                {
                    Bass.StreamFree(_nextStreamHandle);
                    _nextStreamHandle = 0;
                }
                _nextFilePath = string.Empty;
                return;
            }

            if (filePath == _nextFilePath) return;

            if (_nextStreamHandle != 0)
            {
                Bass.StreamFree(_nextStreamHandle);
            }

            _nextFilePath = filePath;
            string safePath = PathHelper.GetSafePath(filePath);

            var scanFlags = BassFlags.Decode | BassFlags.Float | BassFlags.Prescan;
            int scanHandle = Bass.CreateStream(safePath, 0, 0, scanFlags);

            if (scanHandle != 0)
            {
                if (IsSkipSilenceEnabled)
                {
                    _nextEffectiveStartBytes = ScanForTrueStart(scanHandle);
                    _nextEffectiveEndBytes = ScanForTrueEnd(scanHandle, out _nextActualAudioEndBytes);
                }
                else
                {
                    _nextEffectiveStartBytes = 0;
                    _nextEffectiveEndBytes = Bass.ChannelGetLength(scanHandle);
                    _nextActualAudioEndBytes = _nextEffectiveEndBytes;
                }

                Bass.StreamFree(scanHandle);
            }

            var flags = _currentDevice?.DriverType == "Standard"
                ? (BassFlags.Default | BassFlags.AsyncFile | BassFlags.Prescan)
                : (BassFlags.Decode | BassFlags.Float | BassFlags.AsyncFile | BassFlags.Prescan);

            _nextStreamHandle = Bass.CreateStream(safePath, 0, 0, flags);

            if (_nextStreamHandle != 0)
            {
                Bass.ChannelSetPosition(_nextStreamHandle, _nextEffectiveStartBytes);

                byte[] dummy = new byte[1024];
                Bass.ChannelGetData(_nextStreamHandle, dummy, dummy.Length);

                Bass.ChannelSetPosition(_nextStreamHandle, _nextEffectiveStartBytes);
                Bass.ChannelSetAttribute(_nextStreamHandle, ChannelAttribute.Volume, _currentVolume);
            }
        }

        private void CleanUpOldTrack(int handle, int channel, int data, IntPtr user)
        {
            if (_mixerHandle != 0) BassMix.MixerRemoveChannel(channel);
        }

        private void OnTrackEnd(int handle, int channel, int data, IntPtr user)
        {
            if (channel != _streamHandle) return;

            if (IsGaplessEnabled && _nextStreamHandle != 0 && TransitionType == TransitionMode.Normal)
            {
                if (_mixerHandle != 0)
                {
                    Bass.ChannelSlideAttribute(channel, ChannelAttribute.Volume, 0f, 15);

                    Bass.ChannelSetSync(channel, SyncFlags.End | SyncFlags.Mixtime, 0, _cleanUpSyncProc);

                    BassMix.MixerAddChannel(_mixerHandle, _nextStreamHandle, BassFlags.MixerChanBuffer);

                    Bass.ChannelSetAttribute(_nextStreamHandle, ChannelAttribute.Volume, _currentVolume);

                    long lengthBytes = _nextEffectiveEndBytes;

                    long offset = Bass.ChannelSeconds2Bytes(_nextStreamHandle, 0.015);
                    long syncPos = IsSkipSilenceEnabled ? (_nextActualAudioEndBytes - offset) : (lengthBytes - offset);
                    if (syncPos < 0) syncPos = lengthBytes;

                    Bass.ChannelSetSync(_nextStreamHandle, SyncFlags.Position | SyncFlags.Mixtime, syncPos, _endSyncProc);
                }
                else if (_currentDevice?.DriverType == "Standard")
                {
                    Bass.ChannelPlay(_nextStreamHandle);
                }
            }

            Application.Current.Dispatcher.BeginInvoke(new Action(() => TrackEnded?.Invoke()));
        }

        private int ProcessWasapi(IntPtr buffer, int length, IntPtr user)
        {
            if (_mixerHandle == 0) return 0;

            int bytesRead = Bass.ChannelGetData(_mixerHandle, buffer, length);
            if (bytesRead < 0) return 0;
            return bytesRead;
        }

        private int ProcessAsio(bool input, int channel, IntPtr buffer, int length, IntPtr user)
        {
            if (_mixerHandle == 0) return 0;

            int bytesRead = Bass.ChannelGetData(_mixerHandle, buffer, length);
            if (bytesRead < 0) return 0;
            return bytesRead;
        }

        public async Task Pause()
        {
            if (_streamHandle != 0)
            {
                Bass.ChannelSlideAttribute(_streamHandle, ChannelAttribute.Volume, 0f, 40);

                await Task.Delay(100);

                if (_currentDevice?.DriverType == "Standard")
                {
                    Bass.ChannelPause(_streamHandle);
                }
                else
                {
                    BassMix.ChannelFlags(_streamHandle, BassFlags.MixerChanPause, BassFlags.MixerChanPause);
                }
            }
        }

        public void FreeStream()
        {
            if (_mixerHandle != 0 && _streamHandle != 0)
            {
                BassMix.MixerRemoveChannel(_streamHandle);
            }

            if (_streamHandle != 0)
            {
                Bass.StreamFree(_streamHandle);
                _streamHandle = 0;
            }
        }

        public void Stop()
        {
            if (_mixerHandle != 0 && _streamHandle != 0) BassMix.MixerRemoveChannel(_streamHandle);

            if (_streamHandle != 0)
            {
                Bass.StreamFree(_streamHandle);
                _streamHandle = 0;
            }

            if (_nextStreamHandle != 0)
            {
                if (_mixerHandle != 0) BassMix.MixerRemoveChannel(_nextStreamHandle);
                Bass.StreamFree(_nextStreamHandle);
                _nextStreamHandle = 0;
            }

            _nextFilePath = string.Empty;

            if (_fadingStreamHandle != 0)
            {
                if (_mixerHandle != 0) BassMix.MixerRemoveChannel(_fadingStreamHandle);
                Bass.StreamFree(_fadingStreamHandle);
                _fadingStreamHandle = 0;
            }
        }

        public void SetVolume(double volume)
        {
            _currentVolume = (float)volume;

            if (_streamHandle != 0)
            {
                Bass.ChannelSetAttribute(_streamHandle, ChannelAttribute.Volume, volume);
            }

            if (_nextStreamHandle != 0)
            {
                Bass.ChannelSetAttribute(_nextStreamHandle, ChannelAttribute.Volume, volume);
            }
        }

        public async Task SetPosition(double seconds)
        {
            if (_streamHandle == 0 || _isSeeking) return;

            _isSeeking = true;
            try
            {
                Bass.ChannelSlideAttribute(_streamHandle, ChannelAttribute.Volume, 0f, 30);
                await Task.Delay(100);

                long bytes = Bass.ChannelSeconds2Bytes(_streamHandle, seconds);

                if (IsSkipSilenceEnabled && bytes < _currentEffectiveStartBytes)
                {
                    bytes = _currentEffectiveStartBytes;
                }

                if (TransitionType == TransitionMode.Normal)
                {
                    long offset = Bass.ChannelSeconds2Bytes(_streamHandle, IsGaplessEnabled ? 0.015 : 0.140);
                    long endTriggerBytes = _currentActualAudioEndBytes - offset;

                    long safeRewind = Bass.ChannelSeconds2Bytes(_streamHandle, 0.150);
                    long safeMaxBytes = endTriggerBytes - safeRewind;

                    if (bytes > safeMaxBytes)
                    {
                        bytes = Math.Max(0, safeMaxBytes);
                    }
                }
                else
                {
                    if (IsSkipSilenceEnabled && bytes > _currentActualAudioEndBytes)
                    {
                        bytes = _currentActualAudioEndBytes;
                    }
                }

                long lengthBytes = _currentEffectiveEndBytes;

                long actualAudioEndBytes = _currentActualAudioEndBytes;


                long syncPos = actualAudioEndBytes - Bass.ChannelSeconds2Bytes(_streamHandle, TransitionDurationMs / 1000.0);

                if (_currentDevice?.DriverType == "Standard")
                    Bass.ChannelSetPosition(_streamHandle, bytes);
                else
                    BassMix.ChannelSetPosition(_streamHandle, bytes, PositionFlags.MixerReset);

                bool isPastTrigger = (TransitionType == TransitionMode.Crossfade || TransitionType == TransitionMode.FadeInOut)
                                     && (bytes >= syncPos);

                if (isPastTrigger)
                {
                    int remainingMs = (int)((Bass.ChannelBytes2Seconds(_streamHandle, actualAudioEndBytes) - seconds) * 1000);
                    if (remainingMs <= 0) remainingMs = 50;

                    float ratio = (float)remainingMs / TransitionDurationMs;
                    if (ratio > 1f) ratio = 1f;
                    if (ratio < 0f) ratio = 0f;

                    float expectedVolume = _currentVolume * ratio;
                    Bass.ChannelSetAttribute(_streamHandle, ChannelAttribute.Volume, expectedVolume);

                    Bass.ChannelSlideAttribute(_streamHandle, ChannelAttribute.Volume, 0f, remainingMs);

                    if (TransitionType == TransitionMode.Crossfade && _nextStreamHandle != 0)
                    {
                        bool isNextPlaying = false;
                        if (_mixerHandle != 0)
                            isNextPlaying = BassMix.ChannelGetMixer(_nextStreamHandle) != 0;
                        else
                            isNextPlaying = Bass.ChannelIsActive(_nextStreamHandle) == PlaybackState.Playing;

                        if (!isNextPlaying)
                        {
                            if (_mixerHandle != 0)
                                BassMix.MixerAddChannel(_mixerHandle, _nextStreamHandle, BassFlags.MixerChanBuffer);
                            else if (_currentDevice?.DriverType == "Standard")
                                Bass.ChannelPlay(_nextStreamHandle);

                            float nextExpectedVol = _currentVolume * (1f - ratio);
                            Bass.ChannelSetAttribute(_nextStreamHandle, ChannelAttribute.Volume, nextExpectedVol);

                            Bass.ChannelSlideAttribute(_nextStreamHandle, ChannelAttribute.Volume, _currentVolume, remainingMs);

                            _ = Application.Current.Dispatcher.BeginInvoke(new Action(() => TrackEnded?.Invoke()));
                        }
                    }
                }
                else
                {
                    Bass.ChannelSetAttribute(_streamHandle, ChannelAttribute.Volume, 0f);  
                    Bass.ChannelSlideAttribute(_streamHandle, ChannelAttribute.Volume, _currentVolume, 30);
                }

                await Task.Delay(50);
            }
            finally
            {
                _isSeeking = false;
            }
        }

        public double GetPosition()
        {
            if (_streamHandle == 0) return 0;

            long bytes;
            if (_currentDevice?.DriverType == "Standard")
            {
                bytes = Bass.ChannelGetPosition(_streamHandle);
            }
            else
            {
                bytes = BassMix.ChannelGetPosition(_streamHandle);
                if (bytes < 0) bytes = Bass.ChannelGetPosition(_streamHandle);
            }

            return Bass.ChannelBytes2Seconds(_streamHandle, bytes);
        }

        public double GetDuration()
        {
            if (_streamHandle == 0) return 1;
            long bytes = Bass.ChannelGetLength(_streamHandle);
            return Bass.ChannelBytes2Seconds(_streamHandle, bytes);
        }

        public int GetHandle()
        {
            return _streamHandle;
        }

        private void WasapiDeviceNotify(WasapiNotificationType notify, int device, IntPtr user)
        {
            if (notify == WasapiNotificationType.Disabled)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Stop();
                    MessageBox.Show("Audio device was disconnected!", "Device lost", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }
        }

        public void GetVULevels(double[] buffer)
        {
            if (buffer == null || buffer.Length < 2) return;

            if (_streamHandle == 0)
            {
                buffer[0] = 0; buffer[1] = 0;
                return;
            }

            int level = 0;

            if (_currentDevice?.DriverType == "Standard")
            {
                level = Bass.ChannelGetLevel(_streamHandle);
            }
            else
            {
                level = BassMix.ChannelGetLevel(_streamHandle);
            }

            if (level == -1)
            {
                buffer[0] = 0; buffer[1] = 0;
                return;
            }

            buffer[0] = (level & 0xFFFF) / 32768.0 * 100;
            buffer[1] = ((level >> 16) & 0xFFFF) / 32768.0 * 100;
        }

        public float[] GetFFTData()
        {
            if (_streamHandle == 0) return _fftBuffer;

            if (_currentDevice?.DriverType == "Standard")
                Bass.ChannelGetData(_streamHandle, _fftBuffer, (int)DataFlags.FFT2048);
            else
                BassMix.ChannelGetData(_streamHandle, _fftBuffer, (int)DataFlags.FFT2048);

            return _fftBuffer;
        }

        public float[] GetWaveData()
        {
            if (_streamHandle == 0) return _waveBuffer;

            if (_currentDevice?.DriverType == "Standard")
                Bass.ChannelGetData(_streamHandle, _waveBuffer, _waveBuffer.Length * 4);
            else
                BassMix.ChannelGetData(_streamHandle, _waveBuffer, _waveBuffer.Length * 4);

            return _waveBuffer;
        }

        private void FreeDevice()
        {
            if (_currentDevice?.DriverType == "Standard")
            {
                Bass.Free();
            }
            else if (_currentDevice?.DriverType.StartsWith("WASAPI") == true)
            {
                BassWasapi.Free();
                Bass.CurrentDevice = 0;
                Bass.Free();
            }
            else if (_currentDevice?.DriverType == "ASIO")
            {
                BassAsio.Free();
                Bass.CurrentDevice = 0;
                Bass.Free();
            }
        }

        private long ScanForTrueStart(int handle)
        {
            Bass.ChannelSetPosition(handle, 0);     
            long currentPos = 0;
            int bytesToRead = (int)Bass.ChannelSeconds2Bytes(handle, 0.05);     
            if (bytesToRead <= 0) return 0;
            byte[] buffer = new byte[bytesToRead];

            var info = Bass.ChannelGetInfo(handle);
            bool isFloat = info.Flags.HasFlag(BassFlags.Float);
            bool is8Bit = info.Flags.HasFlag(BassFlags.Byte);

            while (true)
            {
                int length = Bass.ChannelGetData(handle, buffer, bytesToRead);
                if (length <= 0) break;

                bool hasAudio = false;
                long peakOffset = 0;

                if (isFloat)
                {
                    float[] floats = new float[length / 4];
                    Buffer.BlockCopy(buffer, 0, floats, 0, length);
                    for (int i = 0; i < floats.Length; i++)
                    {
                        if (Math.Abs(floats[i]) > _silenceThreshold) { hasAudio = true; peakOffset = i * 4; break; }
                    }
                }
                else if (is8Bit)
                {
                    for (int i = 0; i < length; i++)
                    {
                        float val = (buffer[i] - 128) / 128f;
                        if (Math.Abs(val) > _silenceThreshold) { hasAudio = true; peakOffset = i; break; }
                    }
                }
                else   
                {
                    short[] shorts = new short[length / 2];
                    Buffer.BlockCopy(buffer, 0, shorts, 0, length);
                    for (int i = 0; i < shorts.Length; i++)
                    {
                        float val = shorts[i] / 32768f;
                        if (Math.Abs(val) > _silenceThreshold) { hasAudio = true; peakOffset = i * 2; break; }
                    }
                }

                if (hasAudio)
                {
                    long foundPos = currentPos + peakOffset;

                    int bytesPerSample = isFloat ? 4 : (is8Bit ? 1 : 2);
                    int frameSize = info.Channels * bytesPerSample;
                    foundPos -= (foundPos % frameSize);

                    long safetyBuffer = Bass.ChannelSeconds2Bytes(handle, _skipSilencePreRoll);
                    return Math.Max(0, foundPos - safetyBuffer);
                }

                currentPos += length;
                if (Bass.ChannelBytes2Seconds(handle, currentPos) > 15) return 0;      
            }
            return 0;
        }

        private long ScanForTrueEnd(int handle, out long actualAudioEnd)
        {
            long totalLength = Bass.ChannelGetLength(handle);
            long searchWindow = Bass.ChannelSeconds2Bytes(handle, 15);
            long startSearchPos = Math.Max(0, totalLength - searchWindow);

            Bass.ChannelSetPosition(handle, startSearchPos);

            int bytesToRead = (int)Bass.ChannelSeconds2Bytes(handle, 0.05);
            if (bytesToRead <= 0)
            {
                actualAudioEnd = totalLength;  
                return totalLength;
            }
            byte[] buffer = new byte[bytesToRead];

            var info = Bass.ChannelGetInfo(handle);
            bool isFloat = info.Flags.HasFlag(BassFlags.Float);
            bool is8Bit = info.Flags.HasFlag(BassFlags.Byte);

            long lastAudioPos = startSearchPos;
            long currentPos = startSearchPos;

            while (true)
            {
                int length = Bass.ChannelGetData(handle, buffer, bytesToRead);
                if (length <= 0) break;

                if (isFloat)
                {
                    float[] floats = new float[length / 4];
                    Buffer.BlockCopy(buffer, 0, floats, 0, length);
                    for (int i = 0; i < floats.Length; i++)
                    {
                        if (Math.Abs(floats[i]) > _silenceThreshold) lastAudioPos = currentPos + (i * 4);
                    }
                }
                else if (is8Bit)
                {
                    for (int i = 0; i < length; i++)
                    {
                        float val = (buffer[i] - 128) / 128f;
                        if (Math.Abs(val) > _silenceThreshold) lastAudioPos = currentPos + i;
                    }
                }
                else
                {
                    short[] shorts = new short[length / 2];
                    Buffer.BlockCopy(buffer, 0, shorts, 0, length);
                    for (int i = 0; i < shorts.Length; i++)
                    {
                        float val = shorts[i] / 32768f;
                        if (Math.Abs(val) > _silenceThreshold) lastAudioPos = currentPos + (i * 2);
                    }
                }
                currentPos += length;
            }

            actualAudioEnd = lastAudioPos;

            int bytesPerSample = isFloat ? 4 : (is8Bit ? 1 : 2);
            int frameSize = info.Channels * bytesPerSample;
            actualAudioEnd -= (actualAudioEnd % frameSize);

            long safetyBuffer = Bass.ChannelSeconds2Bytes(handle, _skipSilencePostRoll);
            return Math.Min(totalLength, actualAudioEnd + safetyBuffer);
        }

    }
}