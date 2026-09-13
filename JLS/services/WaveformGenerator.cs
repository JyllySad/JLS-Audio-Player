using ManagedBass;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace JLS.Services
{
    public static class WaveformGenerator
    {
        public static async Task<(float[] Left, float[] Right)?> GenerateAsync(string filePath, int pointsCount = 2000, CancellationToken token = default)
        {
            return await Task.Run(() =>
            {
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

                int stream = Bass.CreateStream(PathHelper.GetSafePath(filePath), 0, 0, BassFlags.Decode | BassFlags.Float);
                if (stream == 0) return ((float[] Left, float[] Right)?)null;

                try
                {
                    long lengthBytes = Bass.ChannelGetLength(stream);
                    if (lengthBytes <= 0) return null;

                    var info = Bass.ChannelGetInfo(stream);
                    int channels = info.Channels;

                    float[] leftPeaks = new float[pointsCount];
                    float[] rightPeaks = new float[pointsCount];

                    long bytesPerPoint = lengthBytes / pointsCount;
                    int sampleSize = 4 * channels;
                    bytesPerPoint = (bytesPerPoint / sampleSize) * sampleSize;
                    if (bytesPerPoint == 0) bytesPerPoint = sampleSize;

                    byte[] buffer = new byte[bytesPerPoint];
                    float[] floatBuffer = new float[bytesPerPoint / 4];

                    int step = channels * 16;

                    for (int i = 0; i < pointsCount; i++)
                    {
                        token.ThrowIfCancellationRequested();

                        int bytesRead = Bass.ChannelGetData(stream, buffer, (int)bytesPerPoint);
                        if (bytesRead < 0) break;

                        Buffer.BlockCopy(buffer, 0, floatBuffer, 0, bytesRead);

                        int floatsRead = bytesRead / 4;

                        float maxL = 0;
                        float maxR = 0;

                        for (int j = 0; j < floatsRead; j += step)
                        {
                            float l = Math.Abs(floatBuffer[j]);
                            if (l > maxL) maxL = l;

                            if (channels > 1 && j + 1 < floatsRead)
                            {
                                float r = Math.Abs(floatBuffer[j + 1]);
                                if (r > maxR) maxR = r;
                            }
                            else
                            {
                                if (l > maxR) maxR = l;
                            }
                        }

                        leftPeaks[i] = maxL;
                        rightPeaks[i] = maxR;
                    }

                    return (leftPeaks, rightPeaks);
                }
                finally
                {
                    Bass.StreamFree(stream);
                }
            }, token);
        }
    }
}