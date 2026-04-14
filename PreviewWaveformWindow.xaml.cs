using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ZEGAudioEngineAI
{
    public partial class PreviewWaveformWindow : Window
    {
        // callbacks from MixingWindow
        public Func<double>? GetPositionSeconds;
        public Func<double>? GetDurationSeconds;
        public Action<double>? SeekToSeconds;
        public Action? TogglePause;

        private bool _dragging;

        private readonly Polyline _waveLine = new Polyline { StrokeThickness = 1.0 };
        private readonly Line _playhead = new Line { StrokeThickness = 2.0 };

        private double[] _downsampledAbs = Array.Empty<double>(); // 0..1
        private readonly System.Windows.Threading.DispatcherTimer _timer;

        public PreviewWaveformWindow()
        {
            InitializeComponent();

            _waveLine.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00C8FF"));
            _playhead.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3B3B"));

            WaveCanvas.Children.Add(_waveLine);
            WaveCanvas.Children.Add(_playhead);

            _timer = new System.Windows.Threading.DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(33);
            _timer.Tick += (s, e) => UpdatePlayhead();
            Loaded += (s, e) => _timer.Start();
            Closed += (s, e) => _timer.Stop();

            SizeChanged += (s, e) => RedrawWave();
        }

        public void SetWaveform(double[] downsampledAbs01)
        {
            _downsampledAbs = downsampledAbs01 ?? Array.Empty<double>();
            RedrawWave();
        }

        private void RedrawWave()
        {
            double w = Math.Max(10, WaveCanvas.ActualWidth);
            double h = Math.Max(10, WaveCanvas.ActualHeight);
            double mid = h / 2.0;

            _waveLine.Points.Clear();

            int n = _downsampledAbs.Length;
            if (n <= 1) return;

            // center line waveform (abs amplitude)
            for (int i = 0; i < n; i++)
            {
                double x = (i / (double)(n - 1)) * w;
                double a = _downsampledAbs[i]; // 0..1
                double yTop = mid - a * (mid - 6);
                double yBot = mid + a * (mid - 6);

                // draw as vertical-ish by adding 2 points
                _waveLine.Points.Add(new Point(x, yTop));
                _waveLine.Points.Add(new Point(x, yBot));
            }

            UpdatePlayhead();
        }

        private void UpdatePlayhead()
        {
            if (GetPositionSeconds == null || GetDurationSeconds == null) return;

            double pos = Math.Max(0, GetPositionSeconds());
            double dur = Math.Max(0.0001, GetDurationSeconds());

            TimeText.Text = $"{pos:0.00} / {dur:0.00} sec";

            double w = Math.Max(10, WaveCanvas.ActualWidth);
            double h = Math.Max(10, WaveCanvas.ActualHeight);

            double x = (pos / dur) * w;
            if (x < 0) x = 0;
            if (x > w) x = w;

            _playhead.X1 = x;
            _playhead.X2 = x;
            _playhead.Y1 = 4;
            _playhead.Y2 = h - 4;
        }

        private void SeekFromMouse(Point p)
        {
            if (SeekToSeconds == null || GetDurationSeconds == null) return;
            double w = Math.Max(10, WaveCanvas.ActualWidth);
            double dur = Math.Max(0.0001, GetDurationSeconds());

            double t = (p.X / w) * dur;
            if (t < 0) t = 0;
            if (t > dur) t = dur;

            SeekToSeconds(t);
            UpdatePlayhead();
        }

        private void WaveCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // double click => pause/resume
            if (e.ClickCount >= 2)
            {
                TogglePause?.Invoke();
                return;
            }

            _dragging = true;
            WaveCanvas.CaptureMouse();
            SeekFromMouse(e.GetPosition(WaveCanvas));
        }

        private void WaveCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            SeekFromMouse(e.GetPosition(WaveCanvas));
        }

        private void WaveCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragging = false;
            WaveCanvas.ReleaseMouseCapture();
        }
    }
}