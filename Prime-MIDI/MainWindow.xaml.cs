using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using CSCore;
using CSCore.SoundOut;
using CSCore.MediaFoundation;
using Sanford.Multimedia.Midi;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using System.IO;
using CSCore.Codecs.WAV;
using Microsoft.Win32;
using System.Diagnostics;

namespace Prime_MIDI
{
    public struct VScale
    {
        public double Top, Bottom;
    }

    public struct HScale
    {
        public double Left, Right;
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Chrome Window scary code
        private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case 0x0024:
                    WmGetMinMaxInfo(hwnd, lParam);
                    handled = true;
                    break;
            }
            return (IntPtr)0;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));
            int MONITOR_DEFAULTTONEAREST = 0x00000002;
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                MONITORINFO monitorInfo = new MONITORINFO();
                GetMonitorInfo(monitor, monitorInfo);
                RECT rcWorkArea = monitorInfo.rcWork;
                RECT rcMonitorArea = monitorInfo.rcMonitor;
                mmi.ptMaxPosition.x = Math.Abs(rcWorkArea.left - rcMonitorArea.left);
                mmi.ptMaxPosition.y = Math.Abs(rcWorkArea.top - rcMonitorArea.top);
                mmi.ptMaxSize.x = Math.Abs(rcWorkArea.right - rcWorkArea.left);
                mmi.ptMaxSize.y = Math.Abs(rcWorkArea.bottom - rcWorkArea.top);
            }
            Marshal.StructureToPtr(mmi, lParam, true);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            /// <summary>x coordinate of point.</summary>
            public int x;
            /// <summary>y coordinate of point.</summary>
            public int y;
            /// <summary>Construct a point of coordinates (x,y).</summary>
            public POINT(int x, int y)
            {
                this.x = x;
                this.y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        };

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public class MONITORINFO
        {
            public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            public RECT rcMonitor = new RECT();
            public RECT rcWork = new RECT();
            public int dwFlags = 0;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 0)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
            public static readonly RECT Empty = new RECT();
            public int Width { get { return Math.Abs(right - left); } }
            public int Height { get { return bottom - top; } }
            public RECT(int left, int top, int right, int bottom)
            {
                this.left = left;
                this.top = top;
                this.right = right;
                this.bottom = bottom;
            }
            public RECT(RECT rcSrc)
            {
                left = rcSrc.left;
                top = rcSrc.top;
                right = rcSrc.right;
                bottom = rcSrc.bottom;
            }
            public bool IsEmpty { get { return left >= right || top >= bottom; } }
            public override string ToString()
            {
                if (this == Empty) { return "RECT {Empty}"; }
                return "RECT { left : " + left + " / top : " + top + " / right : " + right + " / bottom : " + bottom + " }";
            }
            public override bool Equals(object obj)
            {
                if (!(obj is Rect)) { return false; }
                return (this == (RECT)obj);
            }
            /// <summary>Return the HashCode for this struct (not garanteed to be unique)</summary>
            public override int GetHashCode() => left.GetHashCode() + top.GetHashCode() + right.GetHashCode() + bottom.GetHashCode();
            /// <summary> Determine if 2 RECT are equal (deep compare)</summary>
            public static bool operator ==(RECT rect1, RECT rect2) { return (rect1.left == rect2.left && rect1.top == rect2.top && rect1.right == rect2.right && rect1.bottom == rect2.bottom); }
            /// <summary> Determine if 2 RECT are different(deep compare)</summary>
            public static bool operator !=(RECT rect1, RECT rect2) { return !(rect1 == rect2); }
        }

        [DllImport("user32")]
        internal static extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO lpmi);

        [DllImport("User32")]
        internal static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);
        #endregion

        OutputDevice midiOutput;

        List<Storyboard> keyPressAnimations = new List<Storyboard>();

        VelocityEase topEase = new VelocityEase(0) { Duration = 0.1, Slope = 2, Supress = 2 };
        VelocityEase bottomEase = new VelocityEase(127) { Duration = 0.1, Slope = 2, Supress = 2 };

        VelocityEase leftEase = new VelocityEase(0) { Duration = 0.1, Slope = 2, Supress = 2 };
        VelocityEase rightEase = new VelocityEase(1) { Duration = 0.1, Slope = 2, Supress = 2 };

        public VScale VerticalScale
        {
            get { return (VScale)GetValue(VerticalScaleProperty); }
            set { SetValue(VerticalScaleProperty, value); }
        }

        public static readonly DependencyProperty VerticalScaleProperty =
            DependencyProperty.Register("VerticalScale", typeof(VScale), typeof(MainWindow), new PropertyMetadata(new VScale()));



        public double LayerHeight
        {
            get { return (double)GetValue(LayerHeightProperty); }
            set { SetValue(LayerHeightProperty, value); }
        }

        public static readonly DependencyProperty LayerHeightProperty =
            DependencyProperty.Register("LayerHeight", typeof(double), typeof(MainWindow), new PropertyMetadata(20.0));

        DXWPF.D3D11 dx11;
        ImgScene scene;

        ISoundOut waveOut;
        IWaveSource fileReader;

        private ISoundOut GetSoundOut()
        {
            if (WasapiOut.IsSupportedOnCurrentPlatform)
                return new WasapiOut();
            else
                return new DirectSoundOut();
        }

        double scaleOffset = 1;//0.997;

        public MainWindow()
        {
            dx11 = new DXWPF.D3D11();
            dx11.SingleThreadedRender = true;
            scene = new ImgScene() { Renderer = dx11 };

            midiOutput = new OutputDevice(0);

            InitializeComponent();
            SourceInitialized += (s, e) =>
            {
                IntPtr handle = (new WindowInteropHelper(this)).Handle;
                HwndSource.FromHwnd(handle).AddHook(new HwndSourceHook(WindowProc));
            };

            for (int i = 0; i < 128; i++)
            {
                var k = 127 - i;
                DockPanel d = new DockPanel();
                Grid key = new Grid();
                key.SetBinding(Grid.WidthProperty, new Binding("ActualWidth") { Source = keyboardBox });

                Storyboard storyboard = new Storyboard();

                if (isBlackNote(k))
                {
                    Panel.SetZIndex(d, 10);
                    SolidColorBrush keycolbrush = new SolidColorBrush(Colors.Black);
                    var bord = new Border() { Background = keycolbrush, Margin = new Thickness(70, 0, 0, 0) };
                    key.Children.Add(bord);

                    var keyCol = new ColorAnimation(Color.FromArgb(255, 0, 0, 250), Colors.Black, new Duration(TimeSpan.FromSeconds(0.5)));
                    storyboard.Children.Add(keyCol);
                    Storyboard.SetTarget(keyCol, bord);
                    Storyboard.SetTargetProperty(keyCol, new PropertyPath("(Border.Background).(SolidColorBrush.Color)"));
                }
                else
                {
                    Panel.SetZIndex(d, 0);
                    SolidColorBrush keycolbrush = new SolidColorBrush(Colors.White);
                    var bord = new Border() { Background = keycolbrush, BorderBrush = Brushes.Black, BorderThickness = new Thickness(1) };
                    key.Children.Add(bord);

                    var keyCol = new ColorAnimation(Color.FromArgb(255, 50, 50, 255), Colors.White, new Duration(TimeSpan.FromSeconds(0.5)));
                    storyboard.Children.Add(keyCol);
                    Storyboard.SetTarget(keyCol, bord);
                    Storyboard.SetTargetProperty(keyCol, new PropertyPath("(Border.Background).(SolidColorBrush.Color)"));

                    var n = k % 12;
                    Func<double, Thickness> conv = (v) => new Thickness(0);

                    if (n == 11) conv = (v) => new Thickness(0, 0, 0, -v * 3 / 4);
                    if (n == 9) conv = (v) => new Thickness(0, -v / 4, 0, -v / 2);
                    if (n == 7) conv = (v) => new Thickness(0, -v / 2, 0, -v / 4);
                    if (n == 5) conv = (v) => new Thickness(0, -v * 3 / 4, 0, 0);

                    if (n == 4) conv = (v) => new Thickness(0, 0, 0, -v * 2 / 3);
                    if (n == 2) conv = (v) => new Thickness(0, -v / 3, 0, -v / 3);
                    if (n == 0) conv = (v) => new Thickness(0, -v * 2 / 3, 0, 0);

                    new InplaceConverter(new[] { new Binding("ActualHeight") { Source = d } }, (args) => conv((double)args[0])).Set(key, MarginProperty);
                }

                SolidColorBrush linecolbrush = new SolidColorBrush(Colors.Transparent);
                Border b = new Border() { BorderBrush = new SolidColorBrush(Color.FromArgb(50, 50, 50, 255)), BorderThickness = new Thickness(0, 1, 0, 1), Background = linecolbrush };

                var lineCol = new ColorAnimation(Color.FromArgb(50, 50, 50, 255), Colors.Transparent, new Duration(TimeSpan.FromSeconds(0.5)));
                storyboard.Children.Add(lineCol);
                Storyboard.SetTarget(lineCol, b);
                Storyboard.SetTargetProperty(lineCol, new PropertyPath("(Border.Background).(SolidColorBrush.Color)"));

                //var h = new DoubleAnimation(0, 20, new Duration(TimeSpan.FromSeconds(0.5)));
                //storyboard.Children.Add(h);
                //Storyboard.SetTarget(h, d);
                //Storyboard.SetTargetProperty(h, new PropertyPath(HeightProperty));

                keyPressAnimations.Add(storyboard);

                d.Children.Add(key);
                d.Children.Add(b);
                d.SetBinding(HeightProperty, new Binding("LayerHeight") { Source = this });

                layersContainer.Children.Add(d);
                d.Tag = k;
                d.PreviewMouseDown += (s, e) =>
                {
                    if (Keyboard.IsKeyDown(Key.LeftCtrl))
                    {
                        var pos = e.GetPosition(contentGrid);
                        var scaled = pos.X / contentGrid.ActualWidth * (scene.HorizontalScale.Right - scene.HorizontalScale.Left) + scene.HorizontalScale.Left;
                        scaled *= scaleOffset;
                        var finalPos = (long)(scaled * (fileReader.Length / fileReader.WaveFormat.Channels)) * fileReader.WaveFormat.Channels;
                        if (finalPos > fileReader.Length) finalPos = fileReader.Length;
                        if (finalPos < 0) finalPos = 0;
                        fileReader.Position = finalPos;
                        return;
                    }
                    PlayKey((int)d.Tag);
                    keyPressAnimations[127 - (int)d.Tag].Begin();
                };
            }

            new InplaceConverter(new[] { new Binding("VerticalScale") { Source = this }, new Binding("ActualHeight") { Source = containerGrid } }, (b) =>
            {
                var scale = (VScale)b[0];
                var height = scale.Bottom - scale.Top;
                var layerHeight = (double)b[1] / height;
                return layerHeight;
            }).Set(this, LayerHeightProperty);

            new InplaceConverter(new[] { new Binding("VerticalScale") { Source = this }, new Binding("ActualHeight") { Source = containerGrid } }, (b) =>
            {
                var scale = (VScale)b[0];
                var height = scale.Bottom - scale.Top;
                var layerHeight = (double)b[1] / height;
                var topMargin = LayerHeight * scale.Top;
                return new Thickness(0, -topMargin, 0, 0);
            }).Set(layersContainer, MarginProperty);

            imgRender.Renderer = scene;

            string path;
            var open = new OpenFileDialog();
            open.Filter = "All Files|*.*";
            if ((bool)open.ShowDialog())
            {
                path = open.FileName;
            }
            else
            {
                throw new Exception();
            }

            scene.LoadTexture(LoadSong(path));
            var fr = new MediaFoundationDecoder(path);
            MemoryStream cache = new MemoryStream();
            fr.WriteToWaveStream(cache);
            cache.Position = 0;
            fileReader = new WaveFileReader(cache);
            waveOut = GetSoundOut();
            waveOut.Initialize(fileReader);
        }

        bool isBlackNote(int n)
        {
            n = n % 12;
            return n == 1 || n == 3 || n == 6 || n == 8 || n == 10;
        }

        void PlayKey(int k)
        {
            midiOutput.SendShort(0x7f0090 + (k << 8));
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MinimiseButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void glContainer_MouseDown(object sender, MouseButtonEventArgs e)
        {

        }

        private void mainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            dx11.Dispose();
            midiOutput.Dispose();
            fileReader.Dispose();
            waveOut.Dispose();
        }

        private void mainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                if (waveOut.PlaybackState != PlaybackState.Playing) waveOut.Play();
                else waveOut.Pause();
            }
        }

        bool altScrolled = false;

        private void mainWindow_KeyDown(object sender, KeyEventArgs e)
        {

        }

        private void mainWindow_KeyUp(object sender, KeyEventArgs e)
        {
            if (altScrolled)
            {
                e.Handled = true;
                altScrolled = false;
            }
        }

        private void mainWindow_PreviewDragEnter(object sender, DragEventArgs e)
        {

        }

        private void mainWindow_PreviewDrop(object sender, DragEventArgs e)
        {

        }

        private void mainWindow_PreviewDragLeave(object sender, DragEventArgs e)
        {

        }

        private void mainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            CompositionTarget.Rendering += RenderCalled;
        }

        private void mainWindow_Unloaded(object sender, RoutedEventArgs e)
        {
            CompositionTarget.Rendering -= RenderCalled;
        }

        void RenderCalled(object s, EventArgs e)
        {
            var top = topEase.GetValue();
            var bottom = bottomEase.GetValue();
            VerticalScale = new VScale() { Top = top, Bottom = bottom };
            scene.VerticalScale = VerticalScale;
            scene.HorizontalScale = new HScale() { Left = leftEase.GetValue(), Right = rightEase.GetValue() };

            var songProgress = fileReader.Position / (double)fileReader.Length;
            var shiftedProgress = (songProgress / scaleOffset - scene.HorizontalScale.Left) / (scene.HorizontalScale.Right - scene.HorizontalScale.Left);
            playHead.Margin = new Thickness(shiftedProgress * contentGrid.ActualWidth, 0, 0, 0);
        }

        private void containerGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var pos = e.GetPosition(containerGrid);
            var newTop = topEase.End;
            var newBotom = bottomEase.End;
            var newLeft = leftEase.End;
            var newRight = rightEase.End;

            if (Keyboard.IsKeyDown(Key.LeftAlt))
            {
                altScrolled = true;
                var topDist = pos.Y / containerGrid.ActualHeight;
                var control = newTop + (newBotom - newTop) * topDist;
                var mult = Math.Pow(1.2, -e.Delta / 120);
                newTop = (newTop - control) * mult + control;
                newBotom = (newBotom - control) * mult + control;
            }
            else if (Keyboard.IsKeyDown(Key.LeftCtrl))
            {
                var leftDist = (pos.X - keyboardBox.ActualWidth) / imgRender.ActualWidth;
                var control = newLeft + (newRight - newLeft) * leftDist;
                var mult = Math.Pow(1.2, -e.Delta / 120);
                newLeft = (newLeft - control) * mult + control;
                newRight = (newRight - control) * mult + control;
            }
            else
            {
                newTop += -e.Delta / 120.0 * 3;
                newBotom += -e.Delta / 120.0 * 3;
            }

            if (newBotom > 128)
            {
                newTop -= (newBotom - 128);
                newBotom = 128;
            }
            if (newTop < 0)
            {
                newBotom += (-newTop);
                newTop = 0;
            }
            if (newBotom > 128)
            {
                newBotom = 128;
            }

            if (newRight > 1)
            {
                newLeft -= (newRight - 1);
                newRight = 1;
            }
            if (newLeft < 0)
            {
                newRight += (-newLeft);
                newLeft = 0;
            }
            if (newRight > 1)
            {
                newRight = 1;
            }

            if (newTop != topEase.End)
                topEase.SetEnd(newTop);
            if (newBotom != bottomEase.End)
                bottomEase.SetEnd(newBotom);

            if (newLeft != leftEase.End)
                leftEase.SetEnd(newLeft);
            if (newRight != rightEase.End)
                rightEase.SetEnd(newRight);
        }

        float[,] LoadSong(string path)
        {
            IWaveSource reader = new MediaFoundationDecoder(path);
            var sampleRate = reader.WaveFormat.SampleRate;
            reader = reader.ToMono();
            MemoryStream cache = new MemoryStream();
            reader.WriteToWaveStream(cache);
            cache.Position = 0;
            reader.Dispose();
            reader = new WaveFileReader(cache);
            var r = reader.ToSampleSource();

            // Samples per key
            var fftSamplesPerKey = 10;

            // Width of the array
            var fftWidth = 10000;

            // Number of subdivisions in each sample group
            // Higher = more precise
            var precisionMultiplier = 5;
            
            // Wavelengths in each sample
            // Higher = more precise
            var wavelengthsPerSample = 20;

            var fftResultLen = 128 * fftSamplesPerKey;
            int len = (int)(reader.Length / 2);
            var result = new float[fftWidth, fftResultLen];

            var datafull = new float[(int)len];
            r.Read(datafull, 0, (int)len);
            r.Dispose();

            int progress = 0;
            object lck = new object();
            Stopwatch s = new Stopwatch();
            s.Start();
            Parallel.For(0, fftResultLen, i =>
            {
                float key = i / (float)fftSamplesPerKey - 0.5f / fftSamplesPerKey;
                float freq = (float)Math.Pow(2, (key - 69 + 9 - 7 * 3 + 12 * 1) / 12) * 440;
                int waveSize = (int)(sampleRate / freq) * wavelengthsPerSample;
                double waveStep = (double)waveSize * fftWidth / len;
                waveStep /= precisionMultiplier;
                if (waveStep < 1) waveStep = 1;
                waveStep = waveStep / fftWidth * len;
                if (waveSize < 1000) waveSize = 1000;
                for (double _l = 0; _l + waveSize < len; _l += waveStep)
                {
                    var l = (int)_l;
                    float mult = freq / sampleRate * (float)Math.PI * 2;
                    float sum_r = 0;
                    float sum_i = 0;
                    for (int j = 0; j < waveSize; j++)
                    {
                        float a = mult * j + (float)Math.PI;
                        sum_r += (float)Math.Cos(a) * datafull[l + j];
                        sum_i += (float)Math.Sin(a) * datafull[l + j];
                    }
                    var val = (Math.Abs(sum_r) + Math.Abs(sum_i)) / waveSize;
                    int start = (int)((double)l * fftWidth / len);
                    int end = (int)((double)(l + waveSize) * fftWidth / len);
                    for (int p = start; p <= end; p++)
                    {
                        result[p, i] = val;
                    }
                }
                lock (lck)
                {
                    Console.WriteLine("Processed frequency bands: " + (++progress));
                }
            });
            Console.WriteLine("Complete! Seconds spent: " + Math.Round(s.ElapsedMilliseconds / 1000.0, 1));

            return result;
        }

        private void mainWindow_PreviewKeyDown_1(object sender, KeyEventArgs e)
        {

        }

        private void colorScale_UserValueChanged(object sender, double e)
        {
            scene.ColorPow = colorScale.Value;
        }

        private void minScale_UserValueChanged(object sender, double e)
        {
            scene.ScaledMin = minScale.Value;
        }

        private void maxScale_UserValueChanged(object sender, double e)
        {
            scene.ScaledMax = maxScale.Value;
        }
    }
}
