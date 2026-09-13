using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JLS.Services
{
    public static class ColorExtractor
    {
        public static (Color Primary, Color Light, Color Dark) GetPaletteFromImage(BitmapSource source)
        {
            Color defPrimary = Color.FromRgb(30, 30, 35);
            Color defLight = Color.FromRgb(100, 100, 120);
            Color defDark = Color.FromRgb(20, 20, 24);

            if (source == null) return (defPrimary, defLight, defDark);

            string algorithm = JLS.Properties.Settings.Default.ColorExtractionAlgorithm;

            if (algorithm == "AverageOverallColor")
            {
                return GetAveragePalette(source, defPrimary, defLight, defDark);
            }
            else
            {
                return GetSmartAccentPalette(source, defPrimary, defLight, defDark);
            }
        }

        public static (Color Primary, Color Light, Color Dark) GetPaletteFromFixedColor(Color baseColor)
        {
            RgbToHsl(baseColor.R, baseColor.G, baseColor.B, out double h, out double s, out double l);

            l = Math.Max(0.08, Math.Min(0.75, l));
            Color primary = HslToRgb(h, s, l);

            double lightL, darkL;

            if (l < 0.45)
            {
                lightL = Math.Min(0.65, l + 0.25);

                darkL = Math.Max(0.03, l - 0.15);
            }
            else
            {
                lightL = Math.Min(0.98, l + 0.35);

                darkL = Math.Max(0.15, l - 0.35);
            }

            double lightS = s > 0.05 ? Math.Min(1.0, s + 0.20) : 0.0;
            double darkS = s > 0.05 ? Math.Max(0.0, s - 0.15) : 0.0;

            Color light = HslToRgb(h, lightS, lightL);
            Color dark = HslToRgb(h, darkS, darkL);

            if (Math.Abs(lightL - darkL) < 0.25)
            {
                if (l < 0.45)
                {
                    lightL = Math.Min(0.75, lightL + 0.12);
                    darkL = Math.Max(0.03, darkL - 0.12);
                }
                else
                {
                    lightL = Math.Min(0.98, lightL + 0.15);
                    darkL = Math.Max(0.10, darkL - 0.15);
                }

                light = HslToRgb(h, lightS, lightL);
                dark = HslToRgb(h, darkS, darkL);
            }

            return (primary, light, dark);
        }

        private static (Color Primary, Color Light, Color Dark) GetSmartAccentPalette(BitmapSource source, Color defPrimary, Color defLight, Color defDark)
        {
            try
            {
                double scaleX = 150.0 / source.PixelWidth;
                double scaleY = 150.0 / source.PixelHeight;

                var transform = new ScaleTransform(scaleX, scaleY);
                transform.Freeze();   

                var scaled = new TransformedBitmap(source, transform);
                scaled.Freeze();    

                var converted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
                converted.Freeze();    

                int width = converted.PixelWidth;
                int height = converted.PixelHeight;
                int stride = width * 4;

                byte[] pixels = new byte[height * stride];
                converted.CopyPixels(pixels, stride, 0);

                long edgeR = 0, edgeG = 0, edgeB = 0; int edgeCount = 0;
                long centerR = 0, centerG = 0, centerB = 0; int centerCount = 0;
                long fallbackR = 0, fallbackG = 0, fallbackB = 0; int fallbackCount = 0;

                long[] edgeBucketR = new long[13];
                long[] edgeBucketG = new long[13];
                long[] edgeBucketB = new long[13];
                int[] edgeBucketCount = new int[13];

                long[] accentBucketR = new long[12];
                long[] accentBucketG = new long[12];
                long[] accentBucketB = new long[12];
                int[] accentBucketCount = new int[12];
                long[] accentBucketSatSum = new long[12];
                int[] accentBucketMaxSat = new int[12];

                int colorfulPixelCount = 0;
                int edgeThickness = 18;         

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int i = (y * stride) + (x * 4);
                        byte b = pixels[i];
                        byte g = pixels[i + 1];
                        byte r = pixels[i + 2];

                        fallbackR += r; fallbackG += g; fallbackB += b; fallbackCount++;

                        int maxC = Math.Max(r, Math.Max(g, b));
                        int minC = Math.Min(r, Math.Min(g, b));
                        int diff = maxC - minC;

                        int requiredDiff = 15;
                        if (maxC > 210) requiredDiff = 35;
                        else if (maxC > 160) requiredDiff = 25;

                        bool isColorful = diff > requiredDiff;
                        int bucket = 12;

                        if (diff > 10)
                        {
                            float hueVal = 0;
                            float d = diff;
                            if (maxC == r) hueVal = (g - b) / d + (g < b ? 6 : 0);
                            else if (maxC == g) hueVal = (b - r) / d + 2;
                            else if (maxC == b) hueVal = (r - g) / d + 4;

                            int hue = (int)(hueVal * 60);
                            if (hue < 0) hue += 360;
                            if (hue >= 360) hue = 0;
                            bucket = hue / 30;
                        }

                        if (isColorful)
                        {
                            colorfulPixelCount++;
                            if (maxC > 35 && minC < 240 && bucket != 12)
                            {
                                accentBucketR[bucket] += r;
                                accentBucketG[bucket] += g;
                                accentBucketB[bucket] += b;
                                accentBucketCount[bucket]++;
                                accentBucketSatSum[bucket] += diff;

                                if (diff > accentBucketMaxSat[bucket])
                                    accentBucketMaxSat[bucket] = diff;
                            }
                        }

                        if ((r < 10 && g < 10 && b < 10) || (r > 245 && g > 245 && b > 245)) continue;

                        if (x < edgeThickness || x >= width - edgeThickness || y < edgeThickness || y >= height - edgeThickness)
                        {
                            edgeR += r; edgeG += g; edgeB += b; edgeCount++;

                            int eBucket = isColorful ? bucket : 12;
                            edgeBucketR[eBucket] += r;
                            edgeBucketG[eBucket] += g;
                            edgeBucketB[eBucket] += b;
                            edgeBucketCount[eBucket]++;
                        }
                        else
                        {
                            centerR += r; centerG += g; centerB += b; centerCount++;
                        }
                    }
                }

                byte eR = 0, eG = 0, eB = 0;
                int bestEdgeBucket = 12;

                if (edgeCount > 0)
                {
                    int maxColorCount = 0;
                    int maxColorBucket = 12;     

                    for (int j = 0; j < 12; j++)
                    {
                        if (edgeBucketCount[j] > maxColorCount)
                        {
                            maxColorCount = edgeBucketCount[j];
                            maxColorBucket = j;
                        }
                    }

                    int neutralCount = edgeBucketCount[12];

                    if (maxColorCount > Math.Max(60, edgeCount * 0.015))
                    {
                        if (neutralCount > maxColorCount * 3)
                        {
                            bestEdgeBucket = 12;
                        }
                        else
                        {
                            bestEdgeBucket = maxColorBucket;    
                        }
                    }
                    else
                    {
                        bestEdgeBucket = 12;
                    }

                    int finalBucketCount = edgeBucketCount[bestEdgeBucket];
                    if (finalBucketCount > 0)
                    {
                        eR = (byte)(edgeBucketR[bestEdgeBucket] / finalBucketCount);
                        eG = (byte)(edgeBucketG[bestEdgeBucket] / finalBucketCount);
                        eB = (byte)(edgeBucketB[bestEdgeBucket] / finalBucketCount);
                    }
                    else
                    {
                        eR = (byte)(edgeR / edgeCount); eG = (byte)(edgeG / edgeCount); eB = (byte)(edgeB / edgeCount);
                    }
                }
                else if (fallbackCount > 0)
                {
                    eR = (byte)(fallbackR / fallbackCount); eG = (byte)(fallbackG / fallbackCount); eB = (byte)(fallbackB / fallbackCount);
                }

                RgbToHsl(eR, eG, eB, out double bgH, out double bgS, out double bgL);

                bool isBlackAndWhiteCover = false;
                if (fallbackCount > 0)
                {
                    isBlackAndWhiteCover = ((double)colorfulPixelCount / fallbackCount) < 0.07;
                }

                if (isBlackAndWhiteCover)
                {
                    bgS = 0.0;
                }
                else
                {
                    if (bgS < 0.025)
                    {
                        bgS = 0.0;
                    }
                    else if (bgS < 0.08)
                    {
                        bgS = bgS + 0.05;
                    }
                    else
                    {
                        bgS = Math.Min(1.0, bgS + 0.06);
                    }
                }

                bgL = Math.Max(0.06, bgL - 0.04);

                Color primary = HslToRgb(bgH, bgS, bgL);
                Color dark = Color.FromRgb((byte)(primary.R * 0.45), (byte)(primary.G * 0.45), (byte)(primary.B * 0.45));

                Color light;
                int bestAccentBucket = -1;
                double maxScore = -1;

                int populatedBuckets = 0;
                for (int j = 0; j < 12; j++)
                {
                    if (accentBucketCount[j] > 15)
                    {
                        populatedBuckets++;
                    }
                }

                bool isStylizedCover = populatedBuckets <= 2;

                int minRequiredPixels = isStylizedCover ? 20 : 75;
                int requiredAvgSat = isStylizedCover ? 25 : 40;
                int requiredMaxSat = isStylizedCover ? 60 : 85;

                for (int j = 0; j < 12; j++)
                {
                    if (accentBucketCount[j] >= minRequiredPixels)
                    {
                        double avgSat = (double)accentBucketSatSum[j] / accentBucketCount[j];
                        int maxSat = accentBucketMaxSat[j];

                        if (avgSat < requiredAvgSat && maxSat < requiredMaxSat)
                        {
                            continue;
                        }

                        double volumeBonus = Math.Min(1.3, 1.0 + (accentBucketCount[j] / 1000.0));
                        double score = avgSat * volumeBonus;

                        if (j == bestEdgeBucket)
                        {
                            score *= 0.8;
                        }

                        if (score > maxScore)
                        {
                            maxScore = score;
                            bestAccentBucket = j;
                        }
                    }
                }

                if (isBlackAndWhiteCover && isStylizedCover && bestAccentBucket != -1)
                {
                    if (accentBucketMaxSat[bestAccentBucket] < 30)
                    {
                        bestAccentBucket = -1;
                    }
                }

                if (bestAccentBucket != -1)
                {
                    byte avgAccentR = (byte)(accentBucketR[bestAccentBucket] / accentBucketCount[bestAccentBucket]);
                    byte avgAccentG = (byte)(accentBucketG[bestAccentBucket] / accentBucketCount[bestAccentBucket]);
                    byte avgAccentB = (byte)(accentBucketB[bestAccentBucket] / accentBucketCount[bestAccentBucket]);

                    RgbToHsl(avgAccentR, avgAccentG, avgAccentB, out double h, out double s, out double l);

                    double satBoost = 0.0;
                    if (s < 0.35) satBoost = 0.08;           
                    else if (s < 0.60) satBoost = 0.15;      
                    else satBoost = 0.05;                       

                    if (isBlackAndWhiteCover && h > 0.05 && h < 0.15 && s > 0.15)
                    {
                        satBoost = 0.30;
                    }

                    s = Math.Min(isStylizedCover ? 1.0 : 0.90, s + satBoost);

                    l = Math.Max(0.48, Math.Min(0.80, l));

                    light = HslToRgb(h, s, l);
                }

                else if (fallbackCount > 0)
                {
                    byte avgR = (byte)(fallbackR / fallbackCount);
                    byte avgG = (byte)(fallbackG / fallbackCount);
                    byte avgB = (byte)(fallbackB / fallbackCount);

                    RgbToHsl(avgR, avgG, avgB, out double h, out double s, out double l);

                    s = 0.0;
                    l = Math.Max(0.50, Math.Min(1.0, l + 0.25));

                    light = HslToRgb(h, s, l);
                }
                else
                {
                    light = defLight;
                }

                RgbToHsl(primary.R, primary.G, primary.B, out double finBgH, out double finBgS, out double finBgL);
                RgbToHsl(light.R, light.G, light.B, out double finLtH, out double finLtS, out double finLtL);

                double hueDiff = Math.Abs(finBgH - finLtH);
                if (hueDiff > 0.5) hueDiff = 1.0 - hueDiff;

                if (hueDiff < 0.1 || (finBgS < 0.1 && finLtS < 0.1))
                {
                    double lightnessDiff = finLtL - finBgL;

                    if (lightnessDiff < 0.25)
                    {
                        finBgL = Math.Max(0.08, finBgL - 0.08);
                        finLtL = Math.Min(0.95, finLtL + 0.10);

                        if (finLtS > 0.10)
                        {
                            finLtS = Math.Min(1.0, finLtS + 0.15);
                        }

                        primary = HslToRgb(finBgH, finBgS, finBgL);
                        light = HslToRgb(finLtH, finLtS, finLtL);
                        dark = Color.FromRgb((byte)(primary.R * 0.45), (byte)(primary.G * 0.45), (byte)(primary.B * 0.45));
                    }
                }

                return (primary, light, dark);
            }
            catch
            {
                return (defPrimary, defLight, defDark);
            }
        }

        private static (Color Primary, Color Light, Color Dark) GetAveragePalette(BitmapSource source, Color defPrimary, Color defLight, Color defDark)
        {
            try
            {
                double scaleX = 100.0 / source.PixelWidth;
                double scaleY = 100.0 / source.PixelHeight;

                var transform = new ScaleTransform(scaleX, scaleY);
                transform.Freeze();

                var scaled = new TransformedBitmap(source, transform);
                scaled.Freeze();

                var converted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
                converted.Freeze();

                int width = converted.PixelWidth;
                int height = converted.PixelHeight;
                int stride = width * 4;

                byte[] pixels = new byte[height * stride];
                converted.CopyPixels(pixels, stride, 0);

                long edgeR = 0, edgeG = 0, edgeB = 0; int edgeCount = 0;
                long centerR = 0, centerG = 0, centerB = 0; int centerCount = 0;
                long fallbackR = 0, fallbackG = 0, fallbackB = 0; int fallbackCount = 0;

                int colorfulPixelCount = 0;
                int edgeThickness = 12;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int i = (y * stride) + (x * 4);
                        byte b = pixels[i];
                        byte g = pixels[i + 1];
                        byte r = pixels[i + 2];

                        fallbackR += r; fallbackG += g; fallbackB += b; fallbackCount++;

                        int maxC = Math.Max(r, Math.Max(g, b));
                        int minC = Math.Min(r, Math.Min(g, b));

                        int requiredDiff = 15;
                        if (maxC > 210) requiredDiff = 35;
                        else if (maxC > 160) requiredDiff = 25;

                        if (maxC - minC > requiredDiff)
                        {
                            colorfulPixelCount++;
                        }

                        if ((r < 10 && g < 10 && b < 10) || (r > 245 && g > 245 && b > 245)) continue;

                        if (x < edgeThickness || x >= width - edgeThickness || y < edgeThickness || y >= height - edgeThickness)
                        {
                            edgeR += r; edgeG += g; edgeB += b; edgeCount++;
                        }
                        else
                        {
                            centerR += r; centerG += g; centerB += b; centerCount++;
                        }
                    }
                }

                byte eR = 0, eG = 0, eB = 0;
                if (edgeCount > 0)
                {
                    eR = (byte)(edgeR / edgeCount); eG = (byte)(edgeG / edgeCount); eB = (byte)(edgeB / edgeCount);
                }
                else if (fallbackCount > 0)
                {
                    eR = (byte)(fallbackR / fallbackCount); eG = (byte)(fallbackG / fallbackCount); eB = (byte)(fallbackB / fallbackCount);
                }

                byte cR = 0, cG = 0, cB = 0;
                if (centerCount > 0)
                {
                    cR = (byte)(centerR / centerCount); cG = (byte)(centerG / centerCount); cB = (byte)(centerB / centerCount);
                }
                else
                {
                    cR = eR; cG = eG; cB = eB;
                }

                double centerWeight = 0.15;
                int maxE = Math.Max(eR, Math.Max(eG, eB));
                int minE = Math.Min(eR, Math.Min(eG, eB));
                int edgeSat = maxE - minE;

                if (edgeCount == 0)
                {
                    centerWeight = 0.65;
                }
                else if (edgeSat < 25)
                {
                    if (maxE > 60) centerWeight = 0.65;
                    else centerWeight = 0.15;
                }

                bool isBlackAndWhiteCover = false;
                if (fallbackCount > 0)
                {
                    isBlackAndWhiteCover = ((double)colorfulPixelCount / fallbackCount) < 0.05;
                }

                byte finalR = (byte)(eR * (1.0 - centerWeight) + cR * centerWeight);
                byte finalG = (byte)(eG * (1.0 - centerWeight) + cG * centerWeight);
                byte finalB = (byte)(eB * (1.0 - centerWeight) + cB * centerWeight);

                RgbToHsl(finalR, finalG, finalB, out double bgH, out double bgS, out double bgL);

                if (isBlackAndWhiteCover || bgS < 0.08)
                {
                    bgS = 0.0;
                }
                else
                {
                    bgS = Math.Min(1.0, bgS + 0.06);
                }

                bgL = Math.Max(0.06, bgL - 0.04);

                Color primary = HslToRgb(bgH, bgS, bgL);
                Color dark = Color.FromRgb((byte)(primary.R * 0.45), (byte)(primary.G * 0.45), (byte)(primary.B * 0.45));

                Color light;
                if (fallbackCount > 0)
                {
                    byte avgR = (byte)(fallbackR / fallbackCount);
                    byte avgG = (byte)(fallbackG / fallbackCount);
                    byte avgB = (byte)(fallbackB / fallbackCount);

                    RgbToHsl(avgR, avgG, avgB, out double h, out double s, out double l);

                    if (isBlackAndWhiteCover || (l > 0.70 && s < 0.25))
                    {
                        s = 0.0;
                        l = Math.Max(0.50, Math.Min(1.0, l + 0.25));
                    }
                    else
                    {
                        s = Math.Min(1.0, s + 0.20);
                        l = Math.Max(0.40, Math.Min(1.0, l + 0.15));
                    }

                    light = HslToRgb(h, s, l);
                }
                else
                {
                    light = defLight;
                }

                return (primary, light, dark);
            }
            catch
            {
                return (defPrimary, defLight, defDark);
            }
        }

        private static void RgbToHsl(byte r, byte g, byte b, out double h, out double s, out double l)
        {
            double rd = r / 255.0; double gd = g / 255.0; double bd = b / 255.0;
            double max = Math.Max(rd, Math.Max(gd, bd));
            double min = Math.Min(rd, Math.Min(gd, bd));
            l = (max + min) / 2.0;

            if (max == min) { h = 0; s = 0; }
            else
            {
                double d = max - min;
                s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
                if (max == rd) h = (gd - bd) / d + (gd < bd ? 6 : 0);
                else if (max == gd) h = (bd - rd) / d + 2;
                else h = (rd - gd) / d + 4;
                h /= 6.0;
            }
        }

        private static Color HslToRgb(double h, double s, double l)
        {
            double r, g, b;
            if (s == 0) { r = g = b = l; }
            else
            {
                Func<double, double, double, double> hue2rgb = (p, q, t) => {
                    if (t < 0) t += 1; if (t > 1) t -= 1;
                    if (t < 1.0 / 6) return p + (q - p) * 6 * t;
                    if (t < 1.0 / 2) return q;
                    if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
                    return p;
                };

                double qValue = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double pValue = 2 * l - qValue;

                r = hue2rgb(pValue, qValue, h + 1.0 / 3);
                g = hue2rgb(pValue, qValue, h);
                b = hue2rgb(pValue, qValue, h - 1.0 / 3);
            }

            return Color.FromRgb((byte)Math.Min(255, Math.Max(0, Math.Round(r * 255))),
                                 (byte)Math.Min(255, Math.Max(0, Math.Round(g * 255))),
                                 (byte)Math.Min(255, Math.Max(0, Math.Round(b * 255))));
        }
    }
}