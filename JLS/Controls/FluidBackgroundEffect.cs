using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace JLS.Controls
{
    public class FluidBackgroundEffect : ShaderEffect
    {
        private static readonly PixelShader _pixelShader = new PixelShader();

        static FluidBackgroundEffect()
        {
            if (!DesignerProperties.GetIsInDesignMode(new DependencyObject()))
            {
                _pixelShader.UriSource = new Uri("pack://application:,,,/FluidBackground.ps", UriKind.Absolute);
            }
        }

        public FluidBackgroundEffect()
        {
            PixelShader = _pixelShader;
            UpdateShaderValue(InputProperty);
            UpdateShaderValue(TimeProperty);
            UpdateShaderValue(BaseColorProperty);
            UpdateShaderValue(DarkColorProperty);
            UpdateShaderValue(LightColorProperty);
            UpdateShaderValue(RandomSeedProperty);
            UpdateShaderValue(IntensityProperty);
            UpdateShaderValue(DeformationProperty);
            UpdateShaderValue(FlashProperty);
        }

        public Brush Input
        {
            get => (Brush)GetValue(InputProperty);
            set => SetValue(InputProperty, value);
        }
        public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(FluidBackgroundEffect), 0);

        public double Time
        {
            get => (double)GetValue(TimeProperty);
            set => SetValue(TimeProperty, value);
        }
        public static readonly DependencyProperty TimeProperty = DependencyProperty.Register("Time", typeof(double), typeof(FluidBackgroundEffect), new UIPropertyMetadata(0.0, PixelShaderConstantCallback(0)));

        public Color BaseColor
        {
            get => (Color)GetValue(BaseColorProperty);
            set => SetValue(BaseColorProperty, value);
        }
        public static readonly DependencyProperty BaseColorProperty = DependencyProperty.Register("BaseColor", typeof(Color), typeof(FluidBackgroundEffect), new UIPropertyMetadata(Colors.Blue, PixelShaderConstantCallback(1)));

        public Color DarkColor
        {
            get => (Color)GetValue(DarkColorProperty);
            set => SetValue(DarkColorProperty, value);
        }
        public static readonly DependencyProperty DarkColorProperty = DependencyProperty.Register(
            "DarkColor", typeof(Color), typeof(FluidBackgroundEffect), 
            new UIPropertyMetadata(Colors.Black, PixelShaderConstantCallback(2)));

        public Color LightColor
        {
            get => (Color)GetValue(LightColorProperty);
            set => SetValue(LightColorProperty, value);
        }
        public static readonly DependencyProperty LightColorProperty = DependencyProperty.Register(
            "LightColor", typeof(Color), typeof(FluidBackgroundEffect), 
            new UIPropertyMetadata(Colors.White, PixelShaderConstantCallback(3)));

        public double RandomSeed
        {
            get => (double)GetValue(RandomSeedProperty);
            set => SetValue(RandomSeedProperty, value);
        }
        public static readonly DependencyProperty RandomSeedProperty = DependencyProperty.Register(
            "RandomSeed", typeof(double), typeof(FluidBackgroundEffect),
            new UIPropertyMetadata(0.0, PixelShaderConstantCallback(4)));

        public double Intensity
        {
            get => (double)GetValue(IntensityProperty);
            set => SetValue(IntensityProperty, value);
        }
        public static readonly DependencyProperty IntensityProperty = DependencyProperty.Register(
            "Intensity", typeof(double), typeof(FluidBackgroundEffect),
            new UIPropertyMetadata(0.0, PixelShaderConstantCallback(5)));

        public double Deformation
        {
            get => (double)GetValue(DeformationProperty);
            set => SetValue(DeformationProperty, value);
        }
        public static readonly DependencyProperty DeformationProperty = DependencyProperty.Register(
            "Deformation", typeof(double), typeof(FluidBackgroundEffect),
            new UIPropertyMetadata(0.0, PixelShaderConstantCallback(6)));

        public double Flash
        {
            get => (double)GetValue(FlashProperty);
            set => SetValue(FlashProperty, value);
        }
        public static readonly DependencyProperty FlashProperty = DependencyProperty.Register(
            "Flash", typeof(double), typeof(FluidBackgroundEffect),
            new UIPropertyMetadata(0.0, PixelShaderConstantCallback(7)));

    }
}