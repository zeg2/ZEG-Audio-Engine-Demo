// MixingWindow.xaml.cs  (REPLACE ALL)
// Αυτό το αρχείο περιέχει ΚΑΙ το MixingWindow ΚΑΙ το MixTrackVM (δεν ανοίγεις νέο class/file)

using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;
// (αν δεν έχεις NAudio, σβήσε τα 3 using NAudio και τα Preview methods πιο κάτω)

using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using static ZEGAudioEngineAI.ExportWindow;
using System.Windows.Threading;
namespace ZEGAudioEngineAI
{
    public partial class MixingWindow : Window
    {
        private readonly ObservableCollection<MixTrackVM> _mixTracks = new();
        // ==== LOG OUT (buffer only) ====
        private readonly List<string> _logBuffer = new List<string>(200);
        private float _tempoBpm = 120f;
        public float TempoBpm => _tempoBpm;
        private void AppendLog(string line)
        {
            var ts = DateTime.Now.ToString("HH:mm:ss");
            _logBuffer.Add($"[{ts}] {line}");
            if (_logBuffer.Count > 200) _logBuffer.RemoveAt(0);
        }
        private IWavePlayer? _previewOut;
        private ISampleProvider? _previewProvider;
        private bool _isPreviewing;
        private readonly List<IDisposable> _previewDisposables = new List<IDisposable>();
        private readonly Dictionary<MixTrackVM, VolumeSampleProvider> _previewTrackVolumes = new();
        private readonly object _previewVolLock = new();

        private void 
            
            
            _Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not WaveformCanvas wc) return;

            // αν αλλάξει DataContext (recycling από ItemsControl), ξε-γράψου/ξαναγράψου σωστά
            wc.DataContextChanged -= WaveformCanvas_DataContextChanged;
            wc.DataContextChanged += WaveformCanvas_DataContextChanged;

            AttachWaveformVm(wc);
        }

        private void WaveformCanvas_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is not WaveformCanvas wc) return;

            wc.DataContextChanged -= WaveformCanvas_DataContextChanged;
            DetachWaveformVm(wc);
        }

        private void WaveformCanvas_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not WaveformCanvas wc) return;
            if (wc.DataContext is not MixTrackVM tr) return;

            // Non-WAV -> WaveformSamples
            wc.SetSamples(tr.WaveformSamples ?? Array.Empty<float>());
        }

        private void WaveformCanvas_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is not WaveformCanvas wc) return;

            // detach old
            if (e.OldValue is MixTrackVM oldTr)
                PropertyChangedEventManager.RemoveHandler(oldTr, WaveformVm_PropertyChanged, nameof(MixTrackVM.WaveformSamples));

            // attach new
            AttachWaveformVm(wc);
        }

        private void AttachWaveformVm(WaveformCanvas wc)
        {
            if (wc.DataContext is not MixTrackVM tr)
            {
                wc.SetSamples(Array.Empty<float>());
                return;
            }

            // subscribe (weak) to avoid memory leaks
            PropertyChangedEventManager.AddHandler(tr, WaveformVm_PropertyChanged, nameof(MixTrackVM.WaveformSamples));

            // draw current samples now
            wc.SetSamples(tr.WaveformSamples ?? Array.Empty<float>());
        }

        private void DetachWaveformVm(WaveformCanvas wc)
        {
            if (wc.DataContext is MixTrackVM tr)
                PropertyChangedEventManager.RemoveHandler(tr, WaveformVm_PropertyChanged, nameof(MixTrackVM.WaveformSamples));
        }

        private void WaveformVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not MixTrackVM tr) return;
            if (e.PropertyName != nameof(MixTrackVM.WaveformSamples)) return;

            // Βρες ποιο canvas έχει αυτό το DataContext και κάνε refresh.
            // Γρήγορο/ασφαλές: επειδή κάθε canvas βλέπει το δικό του DataContext,
            // θα καλέσει SetSamples στο attach, και εδώ απλά αφήνουμε WPF να ζωγραφίσει.
            // (Αν θες 100% immediate, το κάνουμε με VisualTree search, αλλά συνήθως δεν χρειάζεται.)
        }



        public MixingWindow()
        {
            InitializeComponent();
            UpdateAutoRecButtonVisual();
            this.Loaded += (s, e) => LoadVoicePacksToUi();
            TracksList.ItemsSource = _mixTracks;
            _automationRecorder.ArmLane(AutomationTarget.FxOctaveMixPercent, true);
            _suppressAutoCreate = true;
            TrackCountCombo.SelectedIndex = 7; // 8 tracks default
            _suppressAutoCreate = false;
           

           
            CreateMixTracks(8);
            WireTrackAutomationEvents();
        }


        // ===================== TOP BAR / FINALIZE WINDOW HANDLERS =====================

        private bool _creatingFromCombo = false;
        private bool _knobDragging;
        private System.Windows.Point _knobStartPt;
        private double _knobStartValue;
        private System.Windows.Controls.Slider? _activeKnob;
        private void TrackCountCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressAutoCreate) return;
            if (TrackCountCombo.SelectedItem is ComboBoxItem it && int.TryParse(it.Content?.ToString(), out int n))
            {
                CreateMixTracks(n);
                WireTrackAutomationEvents();
            }
        }

        private sealed class TrackMixSnapshot
        {
            public double FxDelayMixPercent { get; set; }
            public double FxReverbMixPercent { get; set; }
            public double FxChorusMixPercent { get; set; }
            public double FxDistMixPercent { get; set; }
            public double FxSaturatorMixPercent { get; set; }
            public double FxLpFilterMixPercent { get; set; }
            public double FxHpFilterMixPercent { get; set; }
            public double FxOctaveMixPercent { get; set; }
        }

        private readonly Dictionary<MixTrackVM, TrackMixSnapshot> _previewMixSnapshots = new();

        private void CapturePreviewMixSnapshots()
        {
            _previewMixSnapshots.Clear();

            foreach (var tr in _mixTracks)
            {
                if (tr == null)
                    continue;

                _previewMixSnapshots[tr] = new TrackMixSnapshot
                {
                    FxDelayMixPercent = tr.FxDelayMixPercent,
                    FxReverbMixPercent = tr.FxReverbMixPercent,
                    FxChorusMixPercent = tr.FxChorusMixPercent,
                    FxDistMixPercent = tr.FxDistMixPercent,
                    FxSaturatorMixPercent = tr.FxSaturatorMixPercent,
                    FxLpFilterMixPercent = tr.FxLpFilterMixPercent,
                    FxHpFilterMixPercent = tr.FxHpFilterMixPercent,
                    FxOctaveMixPercent = tr.FxOctaveMixPercent
                };
            }
        }

        private bool _isApplyingAutomationPlayback = false;
        private bool _isRestoringPreviewMixSnapshots = false;

        private void RestorePreviewMixSnapshots()
        {
            _isRestoringPreviewMixSnapshots = true;
            try
            {
                foreach (var pair in _previewMixSnapshots)
                {
                    var tr = pair.Key;
                    var s = pair.Value;

                    if (tr == null || s == null)
                        continue;

                    tr.FxDelayMixPercent = s.FxDelayMixPercent;
                    tr.FxReverbMixPercent = s.FxReverbMixPercent;
                    tr.FxChorusMixPercent = s.FxChorusMixPercent;
                    tr.FxDistMixPercent = s.FxDistMixPercent;
                    tr.FxSaturatorMixPercent = s.FxSaturatorMixPercent;
                    tr.FxLpFilterMixPercent = s.FxLpFilterMixPercent;
                    tr.FxHpFilterMixPercent = s.FxHpFilterMixPercent;
                    tr.FxOctaveMixPercent = s.FxOctaveMixPercent;
                }
            }
            finally
            {
                _isRestoringPreviewMixSnapshots = false;
            }
        }


        private void TrackTuneNext_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            var tr = btn?.Tag as MixTrackVM;
            if (tr == null) return;

            // ✅ Kick/Drums untouched
            if (IsKickOrDrums(tr))
            {
                tr.OverrideKeyPc = -1;
                return;
            }

            int current = (tr.OverrideKeyPc >= 0) ? tr.OverrideKeyPc : tr.DetectedKeyPc;
            int next = (current + 1) % 12;
            tr.OverrideKeyPc = next;

            // update label only
            string root = PcToName(next);
            string sc = tr.DetectedScale.ToString().Replace("HarmonicMinor", "Harmonic Minor");
            tr.AutoDetectedLabel = $"Detected: {root} {sc}";

            // ✅ ΔΕΝ σταματάμε preview εδώ
            // Η αλλαγή θα εφαρμοστεί την επόμενη φορά που θα πατήσεις Preview (StartPreview).
        }

        private void UpdateTrackDetectedLabel(MixTrackVM tr)
        {
            if (tr == null) return;

            try
            {
                var det = DetectKeyScaleForSingleTrack(tr);
                tr.DetectedKeyPc = det.Item1;
                tr.DetectedScale = det.Item2;

                // reset override when new audio loads (προαιρετικό αλλά recommended)
                tr.OverrideKeyPc = -1;

                string root = PcToName(det.Item1);
                string sc = det.Item2.ToString().Replace("HarmonicMinor", "Harmonic Minor");
                tr.AutoDetectedLabel = $"Detected: {root} {sc}";
            }
            catch
            {
                tr.AutoDetectedLabel = "Detected: —";
            }
        }
        private bool _voicePacksLoadedOnce = false;
        private Tuple<int, ScaleType> DetectKeyScaleForSingleTrack(MixTrackVM tr)
        {
            // fast per-track analysis
            const int secondsPerTrack = 10;
            const int frameSize = 2048;
            const int hopSize = 256;

            double[] hist = new double[12];

            // Use your working WAV-safe per-track histogram reader
            AddTrackHistogram_FromTrack(hist, tr, secondsPerTrack, frameSize, hopSize, weight: 1.0);

            double sum = 0;
            for (int i = 0; i < 12; i++) sum += hist[i];
            if (sum <= 1e-9) return Tuple.Create(0, ScaleType.Minor);

            int bestRoot = 0;
            ScaleType bestScale = ScaleType.Minor;
            double bestScore = double.NegativeInfinity;

            for (int root = 0; root < 12; root++)
            {
                double sMaj = ScoreKey(hist, root, ScaleType.Major);
                if (sMaj > bestScore) { bestScore = sMaj; bestRoot = root; bestScale = ScaleType.Major; }

                double sMin = ScoreKey(hist, root, ScaleType.Minor);
                if (sMin > bestScore) { bestScore = sMin; bestRoot = root; bestScale = ScaleType.Minor; }
            }

            return Tuple.Create(bestRoot, bestScale);
        }

        // optional: on export overlay open, fill only missing labels (fast enough)
        private void UpdateAllTrackDetectedLabels_IfMissing()
        {
            foreach (var tr in _mixTracks)
            {
                if (tr == null) continue;
                if (string.IsNullOrWhiteSpace(tr.AutoDetectedLabel) || tr.AutoDetectedLabel == "Detected: —")
                    UpdateTrackDetectedLabel(tr);
            }
        }

        private void WireTrackAutomationEvents()
        {
            foreach (var tr in _mixTracks)
            {
                if (tr == null)
                    continue;

                tr.AutomationValueChanged -= Track_AutomationValueChanged;
                tr.AutomationValueChanged += Track_AutomationValueChanged;
            }
        }


        private void Track_AutomationValueChanged(MixTrackVM track, AutomationTarget target, double value)
        {
            if (track == null)
                return;

            if (_isApplyingAutomationPlayback)
                return;

            if (_isRestoringPreviewMixSnapshots)
                return;

            if (!_automationRecorder.IsRecording)
                return;

            double timeSec = GetCurrentAutomationTimeSec();

            var lane = track.GetOrCreateAutomationLane(target);
            lane.IsEnabled = true;
            lane.IsArmed = true;
            lane.AddPoint(timeSec, value);
        }


        private DispatcherTimer _automationPreviewTimer;

        private void EnsureAutomationPreviewTimer()
        {
            if (_automationPreviewTimer != null)
                return;

            _automationPreviewTimer = new DispatcherTimer();
            _automationPreviewTimer.Interval = TimeSpan.FromMilliseconds(50);
            _automationPreviewTimer.Tick += (s, e) =>
            {
                UpdateAutomationTimeFromPreview();
                ApplyAutomationPlayback();

                if (PreviewTimeOverlayText != null)
                    PreviewTimeOverlayText.Text = FormatPreviewTime(_automationTimeSec);
            };
        }
        private void DumpOctaveAutomationDebug()
        {
            var lane = _automationRecorder.GetOrCreateLane(AutomationTarget.FxOctaveMixPercent);

            System.Diagnostics.Trace.WriteLine($"[AUTOMATION] Lane={lane.Target}, Armed={lane.IsArmed}, Enabled={lane.IsEnabled}, Recording={_automationRecorder.IsRecording}, Points={lane.Points.Count}");

            foreach (var p in lane.Points)
            {
                System.Diagnostics.Trace.WriteLine($"[AUTOMATION] t={p.TimeSec:F3}  v={p.Value:F2}");
            }
        }



        private void ApplyMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (FinalizeOverlay != null)
                FinalizeOverlay.Visibility = Visibility.Visible;

            UpdateDetectedKeyText_ForExport();
            LoadVoicePacksToUi();
        }
        private List<VoicePack> _voicePacks = new List<VoicePack>();

        private void LoadVoicePacksToUi()
        {
            try
            {
                // ✅ load only once
                if (_voicePacksLoadedOnce) return;
                _voicePacksLoadedOnce = true;

                string voicesDir = ResolveVoicesDir();
                _voicePacks = VoicePackLoader.LoadFromVoicesFolder(voicesDir);

                AppendLog($"[VOICE] voicesDir = {voicesDir}");
                AppendLog($"[VOICE] packs found = {_voicePacks.Count}");

                if (VoicePackCombo != null)
                {
                    // ✅ HARD reset
                    VoicePackCombo.ItemsSource = null;
                    VoicePackCombo.Items.Clear();

                    // ✅ bind only VoicePack list
                    var clean = _voicePacks
                        .GroupBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.First())
                        .OrderBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    VoicePackCombo.DisplayMemberPath = "Id";
                    VoicePackCombo.SelectedValuePath = "Id";
                    VoicePackCombo.ItemsSource = clean;
                    VoicePackCombo.SelectedIndex = (clean.Count > 0) ? 0 : -1;

                    AppendLog($"[VOICE] comboItems = {VoicePackCombo.Items.Count}");
                }
            }
            catch (Exception ex)
            {
                AppendLog("[VOICE] Load packs failed: " + ex.Message);
            }
        }
        private void TtsOpenButton_Click(object sender, RoutedEventArgs e)
{
    if (TtsOverlay != null)
        TtsOverlay.Visibility = Visibility.Visible;

    // load voice packs if not loaded
    LoadVoicePacksToUi();
}

private void TtsCloseButton_Click(object sender, RoutedEventArgs e)
{
    if (TtsOverlay != null)
        TtsOverlay.Visibility = Visibility.Collapsed;
}

private void TtsOverlay_BackgroundMouseDown(object sender, MouseButtonEventArgs e)
{
    // close if clicking outside
    if (TtsOverlay != null)
        TtsOverlay.Visibility = Visibility.Collapsed;
}


        private string ResolveVoicesDir()
        {
            // 1) beside exe
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string p1 = System.IO.Path.Combine(baseDir, "voices");
            if (System.IO.Directory.Exists(p1)) return p1;

            // 2) walk up until we find a "voices" folder (max 8 parents)
            try
            {
                var di = new System.IO.DirectoryInfo(baseDir);
                for (int i = 0; i < 8 && di != null; i++)
                {
                    string cand = System.IO.Path.Combine(di.FullName, "voices");
                    if (System.IO.Directory.Exists(cand))
                        return cand;

                    di = di.Parent;
                }
            }
            catch { }

            // fallback
            return p1;
        }

        private readonly AutomationRecorder _automationRecorder = new AutomationRecorder();

        private DateTime _previewStartTimeUtc;
        private double _automationTimeSec = 0.0;

        private double GetCurrentAutomationTimeSec()
        {
            return _automationTimeSec;
        }

        private bool _skipPreviewMixRestoreOnce = false;
        private void ApplyPopupCancel_Click(object sender, RoutedEventArgs e)
        {
            if (FinalizeOverlay != null)
                FinalizeOverlay.Visibility = Visibility.Collapsed;
        }

        private void FinalizeOverlay_BackgroundMouseDown(object sender, MouseButtonEventArgs e)
        {
            // click outside closes
            if (FinalizeOverlay != null)
                FinalizeOverlay.Visibility = Visibility.Collapsed;
        }


        // Click outside overlay closes it too
        private void FinalizeOverlay_Close(object sender, RoutedEventArgs e)
        {
            if (FinalizeOverlay != null)
                FinalizeOverlay.Visibility = Visibility.Collapsed;
        }

        private void FinalizeApplyOnly_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (FinalizeOverlay != null)
                    FinalizeOverlay.Visibility = Visibility.Collapsed;

                bool normalize = (ChkApplyNormalize?.IsChecked == true);

                // APPLY ONLY (no export)
                ApplyMixInMemoryOnly(normalize);

                AppendLog("[APPLY] Applied to mix (no export).");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "APPLY Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void FinalizeExport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (IsDemoBuild)
                {
                    MessageBox.Show(
                        "This is a demo version. Export is disabled.",
                        "Demo Version",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                if (FinalizeOverlay != null)
                    FinalizeOverlay.Visibility = Visibility.Collapsed;

                bool export24bit = (RbApplyWav24?.IsChecked == true);
                bool normalize = (ChkApplyNormalize?.IsChecked == true);

                if (_isPreviewing)
                {
                    _skipPreviewMixRestoreOnce = true;
                    try { StopPreview(); } catch { }
                }

                ExportFinalMix(export24bit, normalize);

                AppendLog("[EXPORT] Final mix exported.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "EXPORT Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private TimeSpan GetLongestLoadedDuration_FromFiles(TimeSpan tail)
        {
            TimeSpan max = TimeSpan.Zero;

            foreach (var tr in _mixTracks)
            {
                if (tr == null || !tr.HasAnyFile) continue;

                try
                {
                    // ανοίγουμε προσωρινά για να πάρουμε duration από το actual reader
                    using (var tmp = tr.IsStereo
                        ? new TrackInputProvider(tr.FileLeft, tr.FileRight)
                        : new TrackInputProvider(tr.FileMono))
                    {
                        // TrackInputProvider πρέπει να εκθέτει TotalTime / Duration
                        // Αν δεν έχει, σου γράφω παρακάτω fallback.
                        if (tmp.TotalTime > max)
                            max = tmp.TotalTime;
                    }
                }
                catch (Exception ex)
                {
                    AppendLog("[EXPORT] Duration read failed: " + ex.Message);
                }
            }

            if (max <= TimeSpan.Zero) return TimeSpan.Zero;
            return max + tail;
        }

        private static double Clamp(double v, double min, double max)
    => v < min ? min : (v > max ? max : v);

        private static double AmpToDb(double amp)
        {
            // amp in [0..1], convert to dBFS
            if (amp <= 1e-12) return -120.0;
            return 20.0 * Math.Log10(amp);
        }

        /// <summary>
        /// Computes peak dBFS for a file (mono/stereo). Uses AudioFileReader (NAudio).
        /// </summary>
        private static double GetPeakDbfs(string path)
        {
            using var r = new AudioFileReader(path); // outputs float
            float[] buf = new float[8192];
            float peak = 0f;

            int read;
            while ((read = r.Read(buf, 0, buf.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    float a = Math.Abs(buf[i]);
                    if (a > peak) peak = a;
                }
            }

            return AmpToDb(peak);
        }

        private int _lastDetectedKeyPc = 0;               // C
        private ScaleType _lastDetectedScale = ScaleType.Minor;

        private void UpdateDetectedKeyLabel(int keyPc, ScaleType scale)
        {
            try
            {
                if (DetectedKeyText == null) return;
                DetectedKeyText.Text = $"Detected: {PcToName(keyPc)} {scale}";
            }
            catch { }
        }

        private static float CentsToPitchFactor(float cents)
        {
            // factor = 2^(cents/1200)
            return (float)Math.Pow(2.0, cents / 1200.0);
        }

        private static float HzToMidi(double hz)
        {
            return (float)(69.0 + 12.0 * Math.Log(hz / 440.0, 2.0));
        }

        private static double MidiToHz(double midi)
        {
            return 440.0 * Math.Pow(2.0, (midi - 69.0) / 12.0);
        }

        private bool _isAutomationRecordArmed = false;

        private void MixingWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (ShouldIgnoreSpaceShortcut())
                return;

            if (e.Key == Key.Space)
            {
                e.Handled = true;

                bool shiftDown = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

                if (IsPreviewRunning())
                {
                    StopPreview();
                    return;
                }

                if (shiftDown)
                {
                    _isAutomationRecordArmed = true;
                    UpdateAutoRecButtonVisual();
                    AppendLog("[AUTOMATION] Auto record armed from Shift+Space.");
                }

                StartPreview();
            }
        }



        /// <summary>
        /// Returns median detune in cents relative to nearest equal-tempered semitone (e.g. +12.3 cents).
        /// Positive = sharp, Negative = flat.
        /// Uses pitch detect on selected source tracks (Vocals/Bass/Synth/Strings), then robust median.
        /// </summary>
        private float DetectGlobalDetuneCents_FromProject(int secondsToAnalyze = 10)
        {
            var candidates = new List<(string path, double weight)>();

            foreach (var tr in _mixTracks)
            {
                if (tr == null || !tr.HasAnyFile) continue;
                string cat = (tr.Category ?? "").Trim();

                double w =
                    cat.Equals("Vocals", StringComparison.OrdinalIgnoreCase) ? 1.60 :
                    cat.Equals("Bass", StringComparison.OrdinalIgnoreCase) ? 1.30 :
                    cat.Equals("Synth", StringComparison.OrdinalIgnoreCase) ? 1.10 :
                    cat.Equals("Strings", StringComparison.OrdinalIgnoreCase) ? 1.00 : 0.0;

                if (w <= 0) continue;

                string path = tr.IsStereo ? tr.FileLeft : tr.FileMono;
                if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) continue;

                candidates.Add((path, w));
            }

            // fallback: first loaded track
            if (candidates.Count == 0)
            {
                foreach (var tr in _mixTracks)
                {
                    if (tr == null || !tr.HasAnyFile) continue;
                    string path = tr.IsStereo ? tr.FileLeft : tr.FileMono;
                    if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
                    {
                        candidates.Add((path, 1.0));
                        break;
                    }
                }
            }

            if (candidates.Count == 0) return 0f;

            candidates.Sort((a, b) => b.weight.CompareTo(a.weight));

            // ✅ scan only top 2 sources (fast + stable)
            if (candidates.Count > 2) candidates = candidates.Take(2).ToList();

            var centsSamples = new List<float>(2048);

            foreach (var item in candidates)
            {
                CollectDetuneCentsFromFile(item.path, secondsToAnalyze, item.weight, centsSamples);
            }

            if (centsSamples.Count < 50) return 0f;

            centsSamples.Sort();
            float median = centsSamples[centsSamples.Count / 2];

            // clamp
            if (median > 25f) median = 25f;
            if (median < -25f) median = -25f;

            return median;
        }

        private void ApplyAutomationPlayback()
        {
            if (_isRestoringPreviewMixSnapshots)
                return;

            _isApplyingAutomationPlayback = true;
            try
            {
                double timeSec = GetCurrentAutomationTimeSec();

                foreach (var tr in _mixTracks)
                {
                    if (tr == null)
                        continue;

                    if (tr.TryGetAutomationLane(AutomationTarget.FxDelayMixPercent, out var delayMixLane) && delayMixLane.IsEnabled)
                    {
                        double? v = delayMixLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxDelayMixPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxDelayFeedbackPercent, out var delayFeedbackLane) && delayFeedbackLane.IsEnabled)
                    {
                        double? v = delayFeedbackLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxDelayFeedbackPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxDelaySyncEnabled, out var delaySyncLane) && delaySyncLane.IsEnabled)
                    {
                        double? v = delaySyncLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxDelaySyncEnabled = v.Value >= 0.5;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxDelayStep, out var delayStepLane) && delayStepLane.IsEnabled)
                    {
                        double? v = delayStepLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxDelayStep = (int)Math.Round(v.Value);
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxDelayTonePercent, out var delayToneLane) && delayToneLane.IsEnabled)
                    {
                        double? v = delayToneLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxDelayTonePercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxReverbMixPercent, out var reverbMixLane) && reverbMixLane.IsEnabled)
                    {
                        double? v = reverbMixLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxReverbMixPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxOctaveFormantPercent, out var octaveFormantLane) && octaveFormantLane.IsEnabled)
                    {
                        double? v = octaveFormantLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxOctaveFormantPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxOctaveWidthPercent, out var octaveWidthLane) && octaveWidthLane.IsEnabled)
                    {
                        double? v = octaveWidthLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxOctaveWidthPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxOctavePitchSemitones, out var octavePitchLane) && octavePitchLane.IsEnabled)
                    {
                        double? v = octavePitchLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxOctavePitchSemitones = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxDistTonePercent, out var distToneLane) && distToneLane.IsEnabled)
                    {
                        double? v = distToneLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxDistTonePercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxDistSpreadPercent, out var distSpreadLane) && distSpreadLane.IsEnabled)
                    {
                        double? v = distSpreadLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxDistSpreadPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxOctaveLfoRateHz, out var octaveLfoRateLane) && octaveLfoRateLane.IsEnabled)
                    {
                        double? v = octaveLfoRateLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxOctaveLfoRateHz = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxOctaveLfoAmountPercent, out var octaveLfoAmountLane) && octaveLfoAmountLane.IsEnabled)
                    {
                        double? v = octaveLfoAmountLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxOctaveLfoAmountPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxOctaveLfoShape, out var octaveLfoShapeLane) && octaveLfoShapeLane.IsEnabled)
                    {
                        double? v = octaveLfoShapeLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxOctaveLfoShape = (int)Math.Round(v.Value);
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxOctaveLfoStep, out var octaveLfoStepLane) && octaveLfoStepLane.IsEnabled)
                    {
                        double? v = octaveLfoStepLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxOctaveLfoStep = (int)Math.Round(v.Value);
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxOctaveLfoSyncEnabled, out var octaveLfoSyncLane) && octaveLfoSyncLane.IsEnabled)
                    {
                        double? v = octaveLfoSyncLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxOctaveLfoSyncEnabled = v.Value >= 0.5;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxReverbDecayPercent, out var reverbDecayLane) && reverbDecayLane.IsEnabled)
                    {
                        double? v = reverbDecayLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxReverbDecayPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxReverbSizePercent, out var reverbSizeLane) && reverbSizeLane.IsEnabled)
                    {
                        double? v = reverbSizeLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxReverbSizePercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxReverbMode, out var reverbModeLane) && reverbModeLane.IsEnabled)
                    {
                        double? v = reverbModeLane.GetValueAt(timeSec);
                        if (v.HasValue)
                            tr.ReverbMode = Math.Max(0, Math.Min(4, (int)Math.Round(v.Value)));
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxReverbHoldPercent, out var reverbHoldLane) && reverbHoldLane.IsEnabled)
                    {
                        double? v = reverbHoldLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxReverbHoldPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxReverbPreDelayMs, out var reverbPreDelayLane) && reverbPreDelayLane.IsEnabled)
                    {
                        double? v = reverbPreDelayLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxReverbPreDelayMs = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxReverbDampingPercent, out var reverbDampingLane) && reverbDampingLane.IsEnabled)
                    {
                        double? v = reverbDampingLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxReverbDampingPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxReverbWidthPercent, out var reverbWidthLane) && reverbWidthLane.IsEnabled)
                    {
                        double? v = reverbWidthLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxReverbWidthPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxReverbDiffusePercent, out var reverbDiffuseLane) && reverbDiffuseLane.IsEnabled)
                    {
                        double? v = reverbDiffuseLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxReverbDiffusePercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxReverbTonePercent, out var reverbToneLane) && reverbToneLane.IsEnabled)
                    {
                        double? v = reverbToneLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxReverbTonePercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxChorusMixPercent, out var chorusMixLane) && chorusMixLane.IsEnabled)
                    {
                        double? v = chorusMixLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxChorusMixPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxChorusRate, out var chorusRateLane) && chorusRateLane.IsEnabled)
                    {
                        double? v = chorusRateLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxChorusRate = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxChorusDepthPercent, out var chorusDepthLane) && chorusDepthLane.IsEnabled)
                    {
                        double? v = chorusDepthLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxChorusDepthPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxChorusWidthPercent, out var chorusWidthLane) && chorusWidthLane.IsEnabled)
                    {
                        double? v = chorusWidthLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxChorusWidthPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxChorusPhasePercent, out var chorusPhaseLane) && chorusPhaseLane.IsEnabled)
                    {
                        double? v = chorusPhaseLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxChorusPhasePercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxDistMixPercent, out var distMixLane) && distMixLane.IsEnabled)
                    {
                        double? v = distMixLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxDistMixPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxDistDrivePercent, out var distDriveLane) && distDriveLane.IsEnabled)
                    {
                        double? v = distDriveLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxDistDrivePercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxDistSaturationPercent, out var distSatLane) && distSatLane.IsEnabled)
                    {
                        double? v = distSatLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxDistSaturationPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxSaturatorMode, out var satModeLane) && satModeLane.IsEnabled)
                    {
                        double? v = satModeLane.GetValueAt(timeSec);
                        if (v.HasValue)
                            tr.SaturatorMode = Math.Max(0, Math.Min(2, (int)Math.Round(v.Value)));
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxSaturatorMixPercent, out var satMixLane) && satMixLane.IsEnabled)
                    {
                        double? v = satMixLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxSaturatorMixPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxSaturatorDrivePercent, out var satDriveLane) && satDriveLane.IsEnabled)
                    {
                        double? v = satDriveLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxSaturatorDrivePercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxSaturatorTonePercent, out var satToneLane) && satToneLane.IsEnabled)
                    {
                        double? v = satToneLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxSaturatorTonePercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxSaturatorBiasPercent, out var satBiasLane) && satBiasLane.IsEnabled)
                    {
                        double? v = satBiasLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxSaturatorBiasPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxSaturatorMixPercent, out var saturatorMixLane) && saturatorMixLane.IsEnabled)
                    {
                        double? v = saturatorMixLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxSaturatorMixPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxLpFilterMixPercent, out var lpMixLane) && lpMixLane.IsEnabled)
                    {
                        double? v = lpMixLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxLpFilterMixPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxLpFilterMode, out var lpModeLane) && lpModeLane.IsEnabled)
                    {
                        double? v = lpModeLane.GetValueAt(timeSec);
                        if (v.HasValue)
                            tr.LpFilterMode = Math.Max(0, Math.Min(2, (int)Math.Round(v.Value)));
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxLpFilterCutoffPercent, out var lpCutoffLane) && lpCutoffLane.IsEnabled)
                    {
                        double? v = lpCutoffLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxLpFilterCutoffPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxLpFilterResonancePercent, out var lpResLane) && lpResLane.IsEnabled)
                    {
                        double? v = lpResLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxLpFilterResonancePercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxLpFilterBassBoostPercent, out var lpBassLane) && lpBassLane.IsEnabled)
                    {
                        double? v = lpBassLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxLpFilterBassBoostPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxLpFilterLfoRateHz, out var lpLfoRateLane) && lpLfoRateLane.IsEnabled)
                    {
                        double? v = lpLfoRateLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxLpFilterLfoRateHz = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxLpFilterLfoAmountPercent, out var lpLfoAmtLane) && lpLfoAmtLane.IsEnabled)
                    {
                        double? v = lpLfoAmtLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxLpFilterLfoAmountPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxLpFilterLfoSyncEnabled, out var lpLfoSyncLane) && lpLfoSyncLane.IsEnabled)
                    {
                        double? v = lpLfoSyncLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxLpFilterLfoSyncEnabled = v.Value >= 0.5;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxLpFilterLfoStep, out var lpLfoStepLane) && lpLfoStepLane.IsEnabled)
                    {
                        double? v = lpLfoStepLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxLpFilterLfoStep = (int)Math.Round(v.Value);
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxHpFilterMixPercent, out var hpMixLane) && hpMixLane.IsEnabled)
                    {
                        double? v = hpMixLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxHpFilterMixPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxOctaveMixPercent, out var octaveMixLane) && octaveMixLane.IsEnabled)
                    {
                        double? v = octaveMixLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxOctaveMixPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxOctaveOutputPercent, out var octaveOutputLane) && octaveOutputLane.IsEnabled)
                    {
                        double? v = octaveOutputLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxOctaveOutputPercent = v.Value;
                    }


                    if (tr.TryGetAutomationLane(AutomationTarget.FxDelayMode, out var delayModeLane) && delayModeLane.IsEnabled)
                    {
                        double? v = delayModeLane.GetValueAt(timeSec);
                        if (v.HasValue)
                        {
                            int mode = Math.Max(0, Math.Min(4, (int)Math.Round(v.Value)));
                            tr.DelayMode = mode;
                            tr.DelayAlgo = mode == 1 ? 1 : 0;

                            if (tr.Chain?.FxTime != null)
                            {
                                tr.Chain.FxTime.DelayMode = mode;
                                tr.Chain.FxTime.DelayAlgo = tr.DelayAlgo;
                            }
                        }
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxChorusMode, out var chorusModeLane) && chorusModeLane.IsEnabled)
                    {
                        double? v = chorusModeLane.GetValueAt(timeSec);
                        if (v.HasValue)
                            tr.ChorusMode = Math.Max(0, Math.Min(3, (int)Math.Round(v.Value)));
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxDistMode, out var distModeLane) && distModeLane.IsEnabled)
                    {
                        double? v = distModeLane.GetValueAt(timeSec);
                        if (v.HasValue)
                            tr.DistMode = Math.Max(0, Math.Min(2, (int)Math.Round(v.Value)));
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxHpFilterMode, out var hpModeLane) && hpModeLane.IsEnabled)
                    {
                        double? v = hpModeLane.GetValueAt(timeSec);
                        if (v.HasValue)
                            tr.HpFilterMode = Math.Max(0, Math.Min(2, (int)Math.Round(v.Value)));
                    }

                  

                    if (tr.TryGetAutomationLane(AutomationTarget.FxHpFilterCutoffPercent, out var hpCutoffLane) && hpCutoffLane.IsEnabled)
                    {
                        double? v = hpCutoffLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxHpFilterCutoffPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxHpFilterResonancePercent, out var hpResLane) && hpResLane.IsEnabled)
                    {
                        double? v = hpResLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxHpFilterResonancePercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxHpFilterBodyBoostPercent, out var hpBodyLane) && hpBodyLane.IsEnabled)
                    {
                        double? v = hpBodyLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxHpFilterBodyBoostPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxHpFilterLfoRateHz, out var hpLfoRateLane) && hpLfoRateLane.IsEnabled)
                    {
                        double? v = hpLfoRateLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxHpFilterLfoRateHz = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxHpFilterLfoAmountPercent, out var hpLfoAmtLane) && hpLfoAmtLane.IsEnabled)
                    {
                        double? v = hpLfoAmtLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxHpFilterLfoAmountPercent = v.Value;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxHpFilterLfoSyncEnabled, out var hpLfoSyncLane) && hpLfoSyncLane.IsEnabled)
                    {
                        double? v = hpLfoSyncLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxHpFilterLfoSyncEnabled = v.Value >= 0.5;
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxHpFilterLfoStep, out var hpLfoStepLane) && hpLfoStepLane.IsEnabled)
                    {
                        double? v = hpLfoStepLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxHpFilterLfoStep = (int)Math.Round(v.Value);
                    }

                    if (tr.TryGetAutomationLane(AutomationTarget.FxOctaveMode, out var octaveModeLane) && octaveModeLane.IsEnabled)
                    {
                        double? v = octaveModeLane.GetValueAt(timeSec);
                        if (v.HasValue)
                            tr.OctaveShiftMode = Math.Max(0, Math.Min(3, (int)Math.Round(v.Value)));
                    }

                   

                    if (tr.TryGetAutomationLane(AutomationTarget.FxOctaveLfoAmountPercent, out var octaveLfoAmtLane) && octaveLfoAmtLane.IsEnabled)
                    {
                        double? v = octaveLfoAmtLane.GetValueAt(timeSec);
                        if (v.HasValue) tr.FxOctaveLfoAmountPercent = v.Value;
                    }

                   

                   
                }
            }
            finally
            {
                _isApplyingAutomationPlayback = false;
            }
        }

        private static void CollectDetuneCentsFromFile(string path, int secondsToAnalyze, double weight, List<float> outCents)
        {
            using (var r = new AudioFileReader(path))
            {
                int sr = r.WaveFormat.SampleRate;
                int ch = r.WaveFormat.Channels;

                // analysis frame
                const int frameSize = 2048;
                const int hopSize = 256;

                int framesToRead = secondsToAnalyze * sr;
                float[] hopBuf = new float[hopSize * ch];
                float[] ring = new float[frameSize];
                float[] monoFrame = new float[frameSize];
                int ringFill = 0;

                int framesRead = 0;

                while (framesRead < framesToRead)
                {
                    int read = r.Read(hopBuf, 0, hopBuf.Length);
                    if (read <= 0) break;

                    int frames = read / ch;
                    framesRead += frames;

                    for (int f = 0; f < frames; f++)
                    {
                        // downmix to mono
                        double sum = 0;
                        int baseIdx = f * ch;
                        for (int c = 0; c < ch; c++) sum += hopBuf[baseIdx + c];
                        float m = (float)(sum / ch);

                        if (ringFill < frameSize)
                            ring[ringFill++] = m;

                        if (ringFill == frameSize)
                        {
                            Array.Copy(ring, 0, monoFrame, 0, frameSize);

                            // your existing detector
                            double f0 = AutoTune.DetectPitchHz(monoFrame, 0, frameSize, sr, 55f, 900f);

                            if (f0 > 1e-6)
                            {
                                // compute detune cents to nearest semitone
                                double midi = HzToMidi(f0);
                                double nearest = Math.Round(midi);
                                double cents = (midi - nearest) * 100.0; // 1 semitone = 100 cents

                                // energy gate (avoid junk)
                                double e = 0;
                                for (int i = 0; i < frameSize; i++) { double v = monoFrame[i]; e += v * v; }
                                e = Math.Sqrt(e / frameSize);

                                if (e > 0.02) // tweak if needed
                                {
                                    // weight: duplicate sample count proportional to weight (cheap weighting)
                                    int reps = (weight >= 1.55) ? 3 :
                                               (weight >= 1.25) ? 2 : 1;

                                    for (int k = 0; k < reps; k++)
                                        outCents.Add((float)cents);
                                }
                            }

                            // shift ring by hop
                            Array.Copy(ring, hopSize, ring, 0, frameSize - hopSize);
                            ringFill = frameSize - hopSize;
                        }
                    }
                }
            }
        }

        private BusTuneSampleProvider CreateBusTune(ISampleProvider src, float factor)
        {
            return new BusTuneSampleProvider(src, frameSize: 4096, hopSize: 512, highSplitHz: 3500f)
            {
                PitchFactor = factor,
                Wet = 0.35f
            };
        }

        private bool ProviderHasAudio(ISampleProvider p)
        {
            try
            {
                float[] test = new float[8192 * p.WaveFormat.Channels];
                int r = p.Read(test, 0, test.Length);
                return r > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool RunRubberBand_FormantSnap(string inWav, string outWav, string pitchMapPath)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string[] exeCandidates =
            {
        System.IO.Path.Combine(baseDir, "tools", "rubberband", "rubberband.exe"),
        System.IO.Path.Combine(baseDir, "tools", "rubberband", "rubberband-r3.exe"),
        System.IO.Path.Combine(baseDir, "rubberband.exe"),
        System.IO.Path.Combine(baseDir, "rubberband-r3.exe")
    };

            string rbExe = null;
            foreach (var c in exeCandidates)
            {
                if (System.IO.File.Exists(c))
                {
                    rbExe = c;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(rbExe))
            {
                AppendLog("[RB] No RubberBand executable found.");
                foreach (var c in exeCandidates)
                    AppendLog("[RB] tried: " + c);
                return false;
            }

            string rbDir = System.IO.Path.GetDirectoryName(rbExe) ?? baseDir;
            string sndfile = System.IO.Path.Combine(rbDir, "sndfile.dll");

            AppendLog("[RB] exe = " + rbExe);
            AppendLog("[RB] in  = " + inWav);
            AppendLog("[RB] out = " + outWav);
            AppendLog("[RB] map = " + pitchMapPath);

            if (!System.IO.File.Exists(inWav))
            {
                AppendLog("[RB] input wav not found: " + inWav);
                return false;
            }

            if (!System.IO.File.Exists(pitchMapPath))
            {
                AppendLog("[RB] pitch map not found: " + pitchMapPath);
                return false;
            }

            if (!System.IO.File.Exists(sndfile))
            {
                AppendLog("[RB] sndfile.dll not found next to: " + rbExe);
                return false;
            }

            try
            {
                if (System.IO.File.Exists(outWav))
                    System.IO.File.Delete(outWav);
            }
            catch (Exception ex)
            {
                AppendLog("[RB] could not delete old output: " + ex.Message);
            }

            var psi = new ProcessStartInfo
            {
                FileName = rbExe,
                Arguments = $"-3 -F -t 1.0 -p 0 --pitchmap \"{pitchMapPath}\" \"{inWav}\" \"{outWav}\"",
                WorkingDirectory = rbDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p == null)
            {
                AppendLog("[RB] Process.Start returned null.");
                return false;
            }

            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();

            if (!string.IsNullOrWhiteSpace(stdout)) AppendLog("[RB OUT] " + stdout.Trim());
            if (!string.IsNullOrWhiteSpace(stderr)) AppendLog("[RB ERR] " + stderr.Trim());
            AppendLog("[RB] ExitCode=" + p.ExitCode);

            bool ok = p.ExitCode == 0 &&
                      System.IO.File.Exists(outWav) &&
                      new System.IO.FileInfo(outWav).Length > 1000;

            if (!ok)
            {
                AppendLog("[RB] output missing or too small.");
                if (System.IO.File.Exists(outWav))
                    AppendLog("[RB] output bytes = " + new System.IO.FileInfo(outWav).Length);
            }

            return ok;
        }


        private void LoadMonoIntoTrackLive(MixTrackVM tr, string wavPath)
        {
            if (tr == null) return;
            if (string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath)) return;

            tr.SetMono(wavPath);

            try
            {
                tr.RebuildWaveform(520, 60, 360);
                RefreshWaveformCanvas(tr);
            }
            catch { }

            // IMPORTANT:
            // do NOT reset chain here
            // do NOT create special TTS playback path here
            // do NOT do anything different from normal wav loading
        }

        private string BuildPitchMapFile_ForSnapToScale(string wavPath, int keyRootPc, ScaleType scale)
        {
            const int frameSize = 2048;
            const int hopSize = 256;

            using var reader = new AudioFileReader(wavPath);
            int sr = reader.WaveFormat.SampleRate;
            int ch = reader.WaveFormat.Channels;

            var samples = new List<float>(sr * 10);
            float[] buf = new float[4096 * ch];

            int r;
            while ((r = reader.Read(buf, 0, buf.Length)) > 0)
            {
                for (int i = 0; i < r; i += ch)
                {
                    float s = 0f;
                    for (int c = 0; c < ch && (i + c) < r; c++) s += buf[i + c];
                    samples.Add(s / Math.Max(1, ch));
                }
            }

            bool[] mask = AutoTune.GetScaleMask(scale);
            if (mask == null || mask.Length != 12)
                mask = AutoTune.GetScaleMask(ScaleType.Chromatic);

            var sb = new StringBuilder();
            var mono = samples.ToArray();

            // snap strength tuning (μπορείς να το αλλάξεις μετά)
            const double clampSemi = 4.0;  // max shift per hop
            const double deadband = 0.15;  // ignore tiny shifts (semi)

            for (int start = 0; start + frameSize <= mono.Length; start += hopSize)
            {
                double f0 = AutoTune.DetectPitchHz(mono, start, frameSize, sr, 70f, 900f);
                if (f0 <= 1e-6) continue;

                double midi = AutoTune.HzToMidi(f0);
                double snapped = AutoTune.SnapMidiToScale(midi, keyRootPc, mask);

                double diffSemi = snapped - midi;

                // deadband to reduce jitter
                if (Math.Abs(diffSemi) < deadband) diffSemi = 0.0;

                // clamp
                if (diffSemi > clampSemi) diffSemi = clampSemi;
                if (diffSemi < -clampSemi) diffSemi = -clampSemi;

                // frame index in sample frames (mono)
                int frameIndex = start;

                sb.Append(frameIndex.ToString(CultureInfo.InvariantCulture));
                sb.Append(' ');
                sb.Append(diffSemi.ToString("0.000", CultureInfo.InvariantCulture));
                sb.AppendLine();
            }

            string mapPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(wavPath) ?? "",
                System.IO.Path.GetFileNameWithoutExtension(wavPath) + ".pitchmap.txt");

            System.IO.File.WriteAllText(mapPath, sb.ToString(), Encoding.ASCII);
            return mapPath;
        }

        private MixTrackVM GetNextAvailableTtsTrack()
        {
            if (_mixTracks == null || _mixTracks.Count == 0)
                return null;

            // find first truly empty track
            foreach (var t in _mixTracks)
            {
                if (t == null) continue;

                if (!t.HasAnyFile)
                    return t;
            }

            return null;
        }
        private void BtnGenerateTts_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (VoicePackCombo?.SelectedItem is not VoicePack vp)
                {
                    MessageBox.Show("Select a Voice Pack first.", "TTS", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string text = (TtsTextBox?.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    MessageBox.Show("Type some text first.", "TTS", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;

                string piperExe = Path.Combine(baseDir, "tools", "piper", "piper.exe");
                if (!File.Exists(piperExe))
                {
                    MessageBox.Show("piper.exe not found. Put it in tools\\piper\\piper.exe", "TTS",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string piperDir = Path.GetDirectoryName(piperExe) ?? baseDir;
                string espeakData = Path.Combine(piperDir, "espeak-ng-data");

                string outDir = Path.Combine(baseDir, "tts_out");
                Directory.CreateDirectory(outDir);

                string safeName = SanitizeFileName(ExportNameBox?.Text ?? "TTS");
                string stamp = DateTime.Now.ToString("HHmmssfff");
                string outWav = Path.Combine(outDir, $"{safeName}_{vp.Id}_{stamp}.wav");
                string outWavTuned = Path.Combine(outDir, $"{safeName}_{vp.Id}_{stamp}_TUNED.wav");

                AppendLog($"[TTS] Voice={vp.Id}");
                AppendLog($"[TTS] Model={vp.ModelPath}");
                AppendLog($"[TTS] Config={vp.ConfigPath}");
                AppendLog($"[TTS] PiperExe={piperExe}");
                AppendLog($"[TTS] PiperDir={piperDir}");
                AppendLog($"[TTS] EspeakDataExists={Directory.Exists(espeakData)}");
                AppendLog($"[TTS] Out={outWav}");

                var psi = new ProcessStartInfo
                {
                    FileName = piperExe,
                    Arguments = $"--model \"{vp.ModelPath}\" --config \"{vp.ConfigPath}\" --output_file \"{outWav}\"",
                    WorkingDirectory = piperDir,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                psi.EnvironmentVariables["ESPEAK_DATA_PATH"] = espeakData;

                using var p = Process.Start(psi);
                if (p == null) throw new Exception("Failed to start piper.exe");

                using (var sw = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false)))
                {
                    sw.WriteLine(text);
                }

                p.WaitForExit();

                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();

                if (!string.IsNullOrWhiteSpace(stdout))
                    AppendLog("[TTS STDOUT] " + stdout.Trim());
                if (!string.IsNullOrWhiteSpace(stderr))
                    AppendLog("[TTS STDERR/INFO] " + stderr.Trim());

                AppendLog($"[TTS] ExitCode={p.ExitCode}");

                if (p.ExitCode != 0)
                {
                    MessageBox.Show(
                        $"TTS failed (ExitCode={p.ExitCode}).\n\n" + (string.IsNullOrWhiteSpace(stderr) ? "(no stderr)" : stderr),
                        "TTS", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!File.Exists(outWav) || new FileInfo(outWav).Length < 1000)
                {
                    MessageBox.Show("Piper produced no audio file.", "TTS", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                AppendLog("[TTS] OK: " + Path.GetFileName(outWav));

                int keyRootPc = (_lastDetectedKeyPc >= 0 && _lastDetectedKeyPc <= 11) ? _lastDetectedKeyPc : 0;
                ScaleType scale = _lastDetectedScale;

                string pitchMap = BuildPitchMapFile_ForSnapToScale(outWav, keyRootPc, scale);

                bool okTune = RunRubberBand_FormantSnap(outWav, outWavTuned, pitchMap);
                if (!okTune || !File.Exists(outWavTuned) || new FileInfo(outWavTuned).Length < 1000)
                {
                    MessageBox.Show("RubberBand snap-to-scale failed. Check log.", "TTS",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // ALWAYS place TTS on the next empty track
                var trTts = GetNextAvailableTtsTrack();

                if (trTts == null)
                {
                    MessageBox.Show("No empty track available for TTS.", "TTS",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                trTts.Category = "Vocals";
                if (string.IsNullOrWhiteSpace(trTts.Subcategory) || trTts.Subcategory == "Main")
                    trTts.Subcategory = "Lead";

                trTts.StartSeconds = 0.0;
                trTts.LoopCount = 0;
                trTts.LoopEnabled = false;

                ApplyTtsValuesToTrack(trTts);
                LoadMonoIntoTrackLive(trTts, outWavTuned);
                ApplyTtsValuesToTrack(trTts);

                _lastGeneratedTtsTrack = trTts;

                AppendLog($"[TTS] Loaded into next empty track -> {Path.GetFileName(outWavTuned)}");

                if (_isPreviewing)
                {
                    AppendLog("[TTS] Restarting preview so new TTS track joins live mixer.");
                    StopPreview();
                    StartPreview();
                }

                return;
            }
            catch (Exception ex)
            {
                AppendLog("[TTS] EX: " + ex);
                MessageBox.Show(ex.ToString(), "TTS Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }



        private void Global_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space)
                return;

            if (ShouldIgnoreSpaceShortcut())
                return;

            e.Handled = true;

            bool shiftDown = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            if (IsPreviewRunning())
            {
                StopPreview();
                return;
            }

            if (shiftDown)
            {
                _isAutomationRecordArmed = true;
                UpdateAutoRecButtonVisual();
                AppendLog("[AUTOMATION] Auto record armed from Shift+Space.");
            }

            StartPreview();
        }




        private FxEditorWindow? _fxEditorWindow;

        private void FxEditButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Button btn) return;
                if (btn.Tag is not string fxKey) return;
                if (btn.DataContext is not MixTrackVM track) return;

                if (_fxEditorWindow != null)
                {
                    _fxEditorWindow.Close();
                    _fxEditorWindow = null;
                }

                _fxEditorWindow = new FxEditorWindow(
     track,
     fxKey,
     (target, value) =>
     {
         double timeSec = GetCurrentAutomationTimeSec();

         var lane = track.GetOrCreateAutomationLane(target);
         lane.IsEnabled = true;
         lane.IsArmed = true;
         lane.AddPoint(timeSec, value);
     });
                _fxEditorWindow.MaxHeight = SystemParameters.WorkArea.Height - 30;
                _fxEditorWindow.SizeToContent = SizeToContent.Height;

                _fxEditorWindow.Show();
                _fxEditorWindow.UpdateLayout();

                // open to the right of main window, not covering it
                _fxEditorWindow.Left = this.Left + this.Width + 8;
                _fxEditorWindow.Top = this.Top + 40;

                // if off-screen, place inside right side area instead
                if (_fxEditorWindow.Left + _fxEditorWindow.ActualWidth > SystemParameters.WorkArea.Right)
                    _fxEditorWindow.Left = SystemParameters.WorkArea.Right - _fxEditorWindow.ActualWidth - 8;

                if (_fxEditorWindow.Top + _fxEditorWindow.ActualHeight > SystemParameters.WorkArea.Bottom)
                    _fxEditorWindow.Top = SystemParameters.WorkArea.Bottom - _fxEditorWindow.ActualHeight - 8;

                if (_fxEditorWindow.Top < SystemParameters.WorkArea.Top + 8)
                    _fxEditorWindow.Top = SystemParameters.WorkArea.Top + 8;

                _fxEditorWindow.Activate();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "FX Editor", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearAutomation_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var btn = sender as System.Windows.Controls.Button;
                var tr = btn?.Tag as MixTrackVM;
                if (tr == null) return;

                tr.ClearAutomation();
                AppendLog("[AUTOMATION] Cleared track automation.");
            }
            catch (Exception ex)
            {
                AppendLog("[AUTOMATION] ClearAutomation EX: " + ex);
                MessageBox.Show(ex.Message, "Clear Automation Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearTrack_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var btn = sender as System.Windows.Controls.Button;
                var tr = btn?.Tag as MixTrackVM;
                if (tr == null) return;

                // (optional) confirmation
                var res = MessageBox.Show("Clear this track?", "TRACK",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res != MessageBoxResult.Yes) return;

                // ✅ clear audio (SetMono already clears left/right in your VM)
                tr.SetMono("");

                // ✅ reset timing/loop (so it won’t keep delaying/looping)
                tr.StartSeconds = 0.0;
                tr.LoopCount = 0;

                // (optional) reset tuning overrides if you use them per-track
                // tr.OverrideKeyPc = -1;

                // ✅ refresh waveform/UI (safe)
                try
                {
                    tr.RebuildWaveform(520, 60, 360);
                    RefreshWaveformCanvas(tr);
                }
                catch { }

                AppendLog("[TRACKS] Cleared track audio.");
            }
            catch (Exception ex)
            {
                AppendLog("[TRACKS] ClearTrack EX: " + ex);
                MessageBox.Show(ex.Message, "Clear Track Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }



        /// <summary>
        /// Auto-sets GainDb per track so each track reaches a target peak.
        /// This moves sliders automatically because they bind to GainDb.
        /// </summary>
        private void AutoGainAllTracks(double targetPeakDbfs = -10.0)
        {
            // Slider range from XAML
            const double minDb = -24.0;
            const double maxDb = 12.0;

            int changed = 0;

            foreach (var tr in _mixTracks)
            {
                if (tr == null || !tr.HasAnyFile) continue;

                try
                {
                    // Pick a representative file:
                    // - if stereo uses left file for peak estimate (good enough for staging),
                    //   or take max of L/R if you want.
                    string path = tr.IsStereo ? tr.FileLeft : tr.FileMono;
                    if (string.IsNullOrWhiteSpace(path)) continue;

                    double peakDb = GetPeakDbfs(path);

                    // gain needed to reach target peak
                    double suggestedGainDb = targetPeakDbfs - peakDb;

                    // clamp to slider
                    suggestedGainDb = Clamp(suggestedGainDb, minDb, maxDb);

                    // ✅ This will MOVE the slider (binding TwoWay)
                    Dispatcher.Invoke(() =>
                    {
                        tr.GainDb = suggestedGainDb;
                    }); changed++;
                }
                catch (Exception ex)
                {
                    AppendLog("[AUTO GAIN] Track failed: " + ex.Message);
                }
            }

            // If preview is playing, apply instantly to VolumeSampleProviders
            if (_isPreviewing)
                UpdateSoloMuteVolumes();

            Dispatcher.Invoke(() => TracksList.Items.Refresh());

            AppendLog($"[AUTO GAIN] Applied to {changed} track(s). TargetPeak={targetPeakDbfs:0.0} dBFS");
        }
        private void AutoGain_Click(object sender, RoutedEventArgs e)
        {
            AutoGainAllTracks(targetPeakDbfs: -10.0);
        }

        private void ApplyPopupApplyOnly_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                FinalizeOverlay.Visibility = Visibility.Collapsed;

                // APPLY ONLY: do whatever “apply to mix” means in your engine
                // (placeholder call – keep your existing apply logic here)
                ApplyMixOnly();

                // optional log
                // AppendLog("[APPLY] Applied settings to mix (no export).");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "APPLY Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void Back_Click(object sender, RoutedEventArgs e)
        {
            // Stop preview audio
            try
            {
                if (_isPreviewing)
                    StopPreview();
                else
                {
                    // extra safety
                    try { _previewOut?.Stop(); } catch { }
                }
            }
            catch { }

            var start = new StartWindow();
            start.Show();
            Close();
        }
        private void ApplyPopupExport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                FinalizeOverlay.Visibility = Visibility.Collapsed;

                bool export24bit = (RbApplyWav24?.IsChecked == true);
                bool normalize = (ChkApplyNormalize?.IsChecked == true);

                ExportFinalMix(export24bit, normalize);

                // optional log
                // AppendLog("[EXPORT] Final mix exported.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "EXPORT Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyMixInMemoryOnly(bool normalize)
        {
            // Εδώ απλά κάνεις apply settings / limiter target κλπ χωρίς export.
            // Αν έχεις ήδη pipeline, άφησέ το minimal για τώρα:
            AppendLog($"[APPLY] Apply-only. Normalize={normalize}");
            // TODO: αν θες να “κλειδώσεις” limiter ceiling κλπ:
            foreach (var tr in _mixTracks)
            {
                // π.χ. tr.Chain.LimiterCeilingDb = -1.0f; (αν το έχεις)
            }
            MessageBox.Show("Apply is Done (without export).", "APPLY", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static string SanitizeFileName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "EXPORT";
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s.Trim();
        }

        private string GetSelectedBusTagForFileName()
        {
            string sub = "Main";
            if (ExportBusPresetCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem cbi
                && cbi.Tag is string tag && !string.IsNullOrWhiteSpace(tag))
                sub = tag;

            return sub switch
            {
                "Main" => "MIX",
                "ExportClean" => "CLEAN",
                "ExportGlue" => "GLUE",
                "ExportPunch" => "PUNCH",
                _ => sub.ToUpperInvariant()
            };
        }

       

        private Tuple<int, ScaleType> DetectKeyScaleForExport()
        {
            try
            {
                // Weighted category priority (more reliable for harmony)
                var weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Vocals"] = 1.60,
                    ["Bass"] = 1.30,
                    ["Synth"] = 1.10,
                    ["Strings"] = 1.00
                };

                // Collect candidate tracks in priority groups
                var candidates = new List<Tuple<MixTrackVM, double>>();

                foreach (var tr in _mixTracks)
                {
                    if (tr == null || !tr.HasAnyFile) continue;

                    double w;
                    if (tr.Category != null && weights.TryGetValue(tr.Category, out w))
                        candidates.Add(Tuple.Create(tr, w));
                }

                // If none matched, fallback to first loaded track
                if (candidates.Count == 0)
                {
                    foreach (var tr in _mixTracks)
                    {
                        if (tr == null || !tr.HasAnyFile) continue;
                        candidates.Add(Tuple.Create(tr, 1.0));
                        break;
                    }
                }

                if (candidates.Count == 0)
                    return Tuple.Create(0, ScaleType.Minor); // fallback

                // Pitch-class histogram (12 bins)
                double[] hist = new double[12];

                // Analyze a limited amount per track to stay fast
                const int secondsPerTrack = 20;     // per track analysis window
                const int maxTracksToScan = 6;      // cap (keep export snappy)
                const int frameSize = 2048;
                const int hopSize = 256;

                // sort by weight descending
                candidates.Sort((a, b) => b.Item2.CompareTo(a.Item2));

                int scanned = 0;
                foreach (var pair in candidates)
                {
                    if (scanned >= maxTracksToScan) break;

                    var tr = pair.Item1;
                    double weight = pair.Item2;

                    AddTrackHistogram_FromTrack(hist, tr, secondsPerTrack, frameSize, hopSize, weight);
                    scanned++;
                }

                // If histogram is empty (no voiced frames), fallback
                double sum = 0;
                for (int i = 0; i < 12; i++) sum += hist[i];
                if (sum <= 1e-9)
                    return Tuple.Create(0, ScaleType.Minor);

                // Score 12 keys x {Major, Minor}
                int bestRoot = 0;
                ScaleType bestScale = ScaleType.Minor;
                double bestScore = double.NegativeInfinity;

                for (int root = 0; root < 12; root++)
                {
                    double sMaj = ScoreKey(hist, root, ScaleType.Major);
                    if (sMaj > bestScore) { bestScore = sMaj; bestRoot = root; bestScale = ScaleType.Major; }

                    double sMin = ScoreKey(hist, root, ScaleType.Minor);
                    if (sMin > bestScore) { bestScore = sMin; bestRoot = root; bestScale = ScaleType.Minor; }
                }

                AppendLog($"[AUTO KEY] Detected: {PcToName(bestRoot)} {bestScale} (tracks scanned: {scanned})");

                _lastDetectedKeyPc = bestRoot;
                _lastDetectedScale = bestScale;
                UpdateDetectedKeyLabel(bestRoot, bestScale);

                return Tuple.Create(bestRoot, bestScale);
            }
            catch (Exception ex)
            {
                AppendLog("[AUTO KEY] Detect failed: " + ex.Message);
                if (DetectedKeyText != null) DetectedKeyText.Text = "Detected: —";
                return Tuple.Create(0, ScaleType.Minor);
            }
        }

        private void AddTrackHistogram_FromTrack(double[] hist, MixTrackVM tr, int secondsToAnalyze, int frameSize, int hopSize, double weight)
        {
            if (tr == null || !tr.HasAnyFile) return;

            string monoPath = tr.FileMono;
            string leftPath = tr.FileLeft;
            string rightPath = tr.FileRight;

            string pickPath = tr.IsStereo ? leftPath : monoPath;
            if (string.IsNullOrWhiteSpace(pickPath) || !System.IO.File.Exists(pickPath))
                return;

            string ext = System.IO.Path.GetExtension(pickPath).ToLowerInvariant();

            if (ext == ".ogg")
            {
                AppendLog("[HIST] OGG not supported for histogram analysis: " + pickPath);
                return;
            }

            try
            {
                if (ext == ".wav")
                {
                    if (tr.IsStereo &&
                        !string.IsNullOrWhiteSpace(leftPath) &&
                        !string.IsNullOrWhiteSpace(rightPath) &&
                        System.IO.File.Exists(leftPath) &&
                        System.IO.File.Exists(rightPath))
                    {
                        using (var input = new TrackInputProvider(leftPath, rightPath))
                        {
                            AddTrackHistogram_FromProvider(hist, input, secondsToAnalyze, frameSize, hopSize, weight);
                        }
                    }
                    else
                    {
                        using (var input = new TrackInputProvider(monoPath))
                        {
                            AddTrackHistogram_FromProvider(hist, input, secondsToAnalyze, frameSize, hopSize, weight);
                        }
                    }
                }
                else
                {
                    using (var afr = new NAudio.Wave.AudioFileReader(pickPath))
                    {
                        AddTrackHistogram_FromProvider(hist, afr, secondsToAnalyze, frameSize, hopSize, weight);
                    }
                }
            }
            catch (NotSupportedException ex)
            {
                AppendLog("[HIST] Unsupported audio file: " + ex.Message);
            }
            catch (Exception ex)
            {
                AppendLog("[HIST] Failed to analyze track histogram: " + ex.Message);
            }
        }

        private static void AddTrackHistogram_FromProvider(double[] hist, ISampleProvider sp, int secondsToAnalyze, int frameSize, int hopSize, double weight)
        {
            if (hist == null || hist.Length != 12 || sp == null) return;

            int sr = sp.WaveFormat.SampleRate;
            int ch = sp.WaveFormat.Channels;

            int framesToAnalyze = Math.Max(frameSize, secondsToAnalyze * sr);

            // buffers
            float[] hopBuf = new float[hopSize * ch];
            float[] ring = new float[frameSize];
            float[] frame = new float[frameSize];

            int ringFill = 0;
            int analyzedFrames = 0;

            while (analyzedFrames < framesToAnalyze)
            {
                int r = sp.Read(hopBuf, 0, hopBuf.Length);
                if (r <= 0) break;

                int framesRead = r / ch;
                analyzedFrames += framesRead;

                // zero pad last partial read
                if (r < hopBuf.Length)
                    Array.Clear(hopBuf, r, hopBuf.Length - r);

                // downmix hop to mono and push into ring
                for (int i = 0; i < framesRead; i++)
                {
                    double s = 0;
                    int baseIdx = i * ch;
                    for (int c = 0; c < ch; c++) s += hopBuf[baseIdx + c];
                    float m = (float)(s / ch);

                    if (ringFill < frameSize)
                        ring[ringFill++] = m;

                    if (ringFill == frameSize)
                    {
                        Array.Copy(ring, 0, frame, 0, frameSize);

                        // energy gate (less strict)
                        double e = 0;
                        for (int k = 0; k < frameSize; k++) { double v = frame[k]; e += v * v; }
                        e = Math.Sqrt(e / frameSize);

                        if (e > 0.0025) // ✅ more sensitive than 0.01
                        {
                            double f0 = AutoTune.DetectPitchHz(frame, 0, frameSize, sr, 55f, 900f);
                            if (f0 > 1e-6)
                            {
                                double midi = AutoTune.HzToMidi(f0);
                                int pc = ((int)Math.Round(midi)) % 12;
                                if (pc < 0) pc += 12;

                                hist[pc] += weight * e;
                            }
                        }

                        // shift ring by hop
                        int shift = Math.Min(hopSize, frameSize);
                        Array.Copy(ring, shift, ring, 0, frameSize - shift);
                        ringFill = frameSize - shift;
                    }
                }
            }
        }

        private static double ScoreKey(double[] hist, int rootPc, ScaleType scale)
        {
            bool[] mask = AutoTune.GetScaleMask(scale);
            if (mask == null || mask.Length != 12)
                mask = AutoTune.GetScaleMask(ScaleType.Chromatic); // fallback: no snapping
            double inScale = 0;
            double outScale = 0;

            for (int pc = 0; pc < 12; pc++)
            {
                int rel = (pc - rootPc + 12) % 12;
                if (mask[rel]) inScale += hist[pc];
                else outScale += hist[pc];
            }

            return inScale - 1.25 * outScale;
        }

        private static string PcToName(int pc)
        {
            switch ((pc % 12 + 12) % 12)
            {
                case 0: return "C";
                case 1: return "C#";
                case 2: return "D";
                case 3: return "D#";
                case 4: return "E";
                case 5: return "F";
                case 6: return "F#";
                case 7: return "G";
                case 8: return "G#";
                case 9: return "A";
                case 10: return "A#";
                case 11: return "B";
                default: return "C";
            }
        }

        private void TtsStartSecBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                string s = (TtsStartSecBox?.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(s)) return;

                // accept both 2.45 and 2,45
                s = s.Replace(',', '.');

                if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double v))
                {
                    if (v < 0) v = 0;
                    TtsStartSecBox.Text = v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            catch { }
        }


        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                // Άστο μόνο όταν γράφει ο χρήστης σε text field
                if (Keyboard.FocusedElement is TextBox ||
                    Keyboard.FocusedElement is PasswordBox ||
                    Keyboard.FocusedElement is RichTextBox)
                {
                    base.OnPreviewKeyDown(e);
                    return;
                }

                e.Handled = true;

                bool shiftDown = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

                if (IsPreviewRunning())
                {
                    StopPreview();
                }
                else
                {
                    if (shiftDown)
                    {
                        _isAutomationRecordArmed = true;
                        UpdateAutoRecButtonVisual();
                        AppendLog("[AUTOMATION] Auto record armed from Shift+Space.");
                    }

                    StartPreview();
                }

                return;
            }

            base.OnPreviewKeyDown(e);
        }



        private bool IsPreviewRunning()
        {
            return _isPreviewing ||
                   (_previewOut != null && _previewOut.PlaybackState == PlaybackState.Playing);
        }

        private void SetPreviewUiState(bool isRunning)
        {
            _isPreviewing = isRunning;
            if (PreviewButton != null)
                PreviewButton.Content = isRunning ? "STOP" : "PREVIEW";
        }


        private void ExportFinalMix(bool export24bit, bool normalize)
        {
            AppendLog($"[EXPORT] Export WAV started. Normalize={normalize}, 24bit={export24bit}");

            if (_isPreviewing)
            {
                _skipPreviewMixRestoreOnce = true;
                try { StopPreview(); } catch { }
            }

            TimeSpan renderLength = GetLongestLoadedDuration_FromFiles(TimeSpan.FromSeconds(3));
            if (renderLength <= TimeSpan.Zero)
            {
                MessageBox.Show("No loaded tracks for export.", "EXPORT",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string nameBase = SanitizeFileName(ExportNameBox?.Text ?? "EXPORT");
            string busTag = GetSelectedBusTagForFileName();
            string fmtTag = export24bit ? "24BIT" : "32F";
            string defaultName = $"{nameBase}_{busTag}_{fmtTag}.wav";

            var sfd = new SaveFileDialog
            {
                Title = "Export Final Mix WAV",
                Filter = "WAV file (*.wav)|*.wav",
                FileName = defaultName
            };

            if (sfd.ShowDialog(this) != true)
                return;

            // find target SR from first loaded track
            int targetSr = 44100;
            bool srFound = false;

            foreach (var tr in _mixTracks)
            {
                if (tr == null || !tr.HasAnyFile) continue;

                using (var tmp = tr.IsStereo
                    ? new TrackInputProvider(tr.FileLeft, tr.FileRight)
                    : new TrackInputProvider(tr.FileMono))
                {
                    targetSr = tmp.WaveFormat.SampleRate;
                    srFound = true;
                    break;
                }
            }

            if (!srFound)
            {
                AppendLog("[EXPORT] No tracks loaded.");
                return;
            }

            AppendLog($"[EXPORT] Target SR = {targetSr} Hz");

            var inputs = new List<ISampleProvider>();
            var disposables = new List<IDisposable>();
            int added = 0;

            try
            {
                foreach (var tr in _mixTracks)
                {
                    if (tr == null || !tr.HasAnyFile) continue;

                    var input = tr.IsStereo
                        ? new TrackInputProvider(tr.FileLeft, tr.FileRight)
                        : new TrackInputProvider(tr.FileMono);

                    disposables.Add(input);

                    tr.Chain.Reset(input.WaveFormat.SampleRate, input.WaveFormat.Channels);
                    tr.FxDelay = tr.FxDelayMixPercent > 0.001;
                    tr.FxReverb = tr.FxReverbMixPercent > 0.001;
                    tr.FxChorus = tr.FxChorusMixPercent > 0.001;
                    tr.FxOctave = tr.FxOctaveMixPercent > 0.001;
                    tr.FxDistortion = tr.FxDistMixPercent > 0.001;
                    if (tr.FxDelayMixPercent > 0.001 ||
    tr.FxReverbMixPercent > 0.001 ||
    tr.FxChorusMixPercent > 0.001 ||
    tr.FxDistMixPercent > 0.001 ||
    tr.FxOctaveMixPercent > 0.001)
                    {
                        MessageBox.Show(
                            $"TRACK: {tr.Name}\n" +
                            $"DelayMix={tr.FxDelayMixPercent}\n" +
                            $"ReverbMix={tr.FxReverbMixPercent}\n" +
                            $"ChorusMix={tr.FxChorusMixPercent}\n" +
                            $"DistMix={tr.FxDistMixPercent}\n" +
                            $"OctaveMix={tr.FxOctaveMixPercent}\n" +
                            $"FxDelay={tr.FxDelay}\n" +
                            $"FxReverb={tr.FxReverb}\n" +
                            $"FxChorus={tr.FxChorus}\n" +
                            $"FxDistortion={tr.FxDistortion}\n" +
                            $"FxOctave={tr.FxOctave}",
                            "EXPORT TRACK WITH FX");
                    }

                    var trackProvider = new TrackChainSampleProvider(input, tr)
                    {
                        IsOfflineRender = true
                    };

                    ISampleProvider sp = trackProvider;

                    if (sp.WaveFormat.Channels == 1)
                        sp = new MonoToStereoSampleProvider(sp);
                    else if (sp.WaveFormat.Channels > 2)
                        throw new NotSupportedException("Export supports only mono/stereo tracks.");

                    if (sp.WaveFormat.SampleRate != targetSr)
                        sp = new WdlResamplingSampleProvider(sp, targetSr);

                    // ίδιο logic με preview
                    if (tr.OverrideKeyPc >= 0 && IsTonal(tr) && !IsKickOrDrums(tr))
                    {
                        sp = new TrackKeyShiftLiveSampleProvider(
                            sp,
                            getTargetFactor: () =>
                            {
                                int fromPc = tr.DetectedKeyPc;
                                int toPc = tr.OverrideKeyPc;

                                int diff = (toPc - fromPc) % 12;
                                if (diff < -6) diff += 12;
                                if (diff > 6) diff -= 12;

                                return (float)Math.Pow(2.0, diff / 12.0);
                            },
                            frameSize: 2048,
                            hopSize: 256,
                            retuneMs: 40f,
                            maxStepPerHop: 0.02f
                        );
                    }

                    if (tr.LoopCount == 2 || tr.LoopCount == 4 || tr.LoopCount == 8)
                    {
                        sp = new FiniteLoopSampleProvider(sp, tr.LoopCount);
                    }

                    if (tr.StartSeconds > 0.0001)
                    {
                        sp = new OffsetSampleProvider(sp)
                        {
                            DelayBy = TimeSpan.FromSeconds(tr.StartSeconds),
                            LeadOut = TimeSpan.FromSeconds(60)
                        };
                    }

                    var trackVol = new VolumeSampleProvider(sp)
                    {
                        Volume = DbToLinear(tr.GainDb)
                    };

                    inputs.Add(trackVol);
                    added++;
                }

                if (added == 0)
                {
                    MessageBox.Show("No loaded tracks for export.", "EXPORT",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(targetSr, 2))
                {
                    ReadFully = true
                };

                foreach (var i in inputs)
                    mixer.AddMixerInput(i);

                float g = 1.0f / (float)Math.Sqrt(Math.Max(1, added));
                ISampleProvider preBus = new VolumeSampleProvider(mixer) { Volume = g };

                // bus preset from combo
                string sub = "Main";
                if (ExportBusPresetCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem cbi
                    && cbi.Tag is string tag && !string.IsNullOrWhiteSpace(tag))
                {
                    sub = tag;
                }

                var busPreset = MixingPresets.Get("Bus", sub);
                ISampleProvider finalBus = new BusChainSampleProvider(preBus, busPreset, finalCeilingDb: -1.0f);

                RenderToWav(finalBus, sfd.FileName, renderLength, normalize, export24bit);

                AppendLog($"[EXPORT] OK -> {System.IO.Path.GetFileName(sfd.FileName)}");

                var res = MessageBox.Show(
                    "Export completed successfully.",
                    "EXPORT",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                if (res == MessageBoxResult.OK)
                {
                    try { OpenExplorerAndSelectFile(sfd.FileName); } catch { }
                }
            }
            catch (Exception ex)
            {
                AppendLog("[EXPORT] EX: " + ex);
                MessageBox.Show(ex.ToString(), "EXPORT Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                foreach (var d in disposables)
                {
                    try { d.Dispose(); } catch { }
                }
            }
        }

        private bool ShouldIgnoreSpaceShortcut()
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            if (focused == null) return false;

            return focused is TextBox
                || focused is RichTextBox
                || focused is PasswordBox
                || focused is ComboBox
                || focused is Slider
                || focused is Button
                || focused is System.Windows.Controls.Primitives.ToggleButton
                || focused is System.Windows.Controls.Primitives.TextBoxBase;
        }

       

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space)
                return;

            if (ShouldIgnoreSpaceShortcut())
                return;

            e.Handled = true;

            bool shiftDown = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            if (IsPreviewRunning())
            {
                StopPreview();
                return;
            }

            if (shiftDown)
            {
                _isAutomationRecordArmed = true;
                UpdateAutoRecButtonVisual();
                AppendLog("[AUTOMATION] Auto record armed from Shift+Space.");
            }

            StartPreview();
        }




        private static void OpenExplorerAndSelectFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            if (!System.IO.File.Exists(filePath)) return;

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{filePath}\"",
                UseShellExecute = true
            });
        }


        private static int KeyPcFromString(string s)
        {
            switch ((s ?? "").Trim().ToUpperInvariant())
            {
                case "C": return 0;
                case "C#": return 1;
                case "D": return 2;
                case "D#": return 3;
                case "E": return 4;
                case "F": return 5;
                case "F#": return 6;
                case "G": return 7;
                case "G#": return 8;
                case "A": return 9;
                case "A#": return 10;
                case "B": return 11;
                default: return 0;
            }
        }

        private (ISampleProvider sp, IDisposable disposable) OpenForDetection(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return (null, null);

            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();

            try
            {
                // ✅ WAV: use the SAME path you used before (most reliable for weird WAV/extensible)
                if (ext == ".wav")
                {
                    // If you previously used TrackInputProvider for WAV, use it here too.
                    // (Stereo WAV -> just use the file as mono for detection, left is enough)
                    var input = new TrackInputProvider(path); // mono wav
                    return (input, input);
                }

                // ✅ Non-WAV: AudioFileReader (mp3/flac/etc)
                var afr = new NAudio.Wave.AudioFileReader(path);
                return (afr, afr);
            }
            catch
            {
                // Fallback: try AudioFileReader anyway
                try
                {
                    var afr = new NAudio.Wave.AudioFileReader(path);
                    return (afr, afr);
                }
                catch
                {
                    return (null, null);
                }
            }
        }

        private void FxPageButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is MixTrackVM tr)
                tr.ToggleFxPage();
        }

        private static ScaleType ScaleFromString(string s)
        {
            s = (s ?? "").Trim().ToUpperInvariant();
            if (s == "MAJOR") return ScaleType.Major;
            if (s == "MINOR") return ScaleType.Minor;
            if (s.Contains("HARMONIC")) return ScaleType.HarmonicMinor;
            if (s == "DORIAN") return ScaleType.Dorian;
            if (s == "CHROMATIC") return ScaleType.Chromatic;
            return ScaleType.Minor;
        }

        private static string GetComboText(System.Windows.Controls.ComboBox cb)
        {
            var item = cb?.SelectedItem as System.Windows.Controls.ComboBoxItem;
            return item?.Content?.ToString() ?? "";
        }



        private static void AddTrackHistogram(
     double[] hist,
     string path,
     int secondsToAnalyze,
     int frameSize,
     int hopSize,
     double weight)
        {
            // histogram[12] gets pitch class energy
            if (hist == null || hist.Length != 12) return;
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return;

            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();

            // we will read mono float at 44100 for stable detector
            const int targetSr = 44100;

            IDisposable disp = null;
            ISampleProvider sp = null;

            try
            {
                if (ext == ".wav")
                {
                    // ✅ WAV: WaveFileReader is the most reliable for weird WAV/extensible/24bit/32float
                    var wfr = new NAudio.Wave.WaveFileReader(path);
                    disp = wfr;

                    // Convert to float samples
                    var sampleChannel = new NAudio.Wave.SampleProviders.SampleChannel(wfr, true);
                    sp = sampleChannel;
                }
                else
                {
                    // ✅ MP3/FLAC/others: AudioFileReader
                    var afr = new NAudio.Wave.AudioFileReader(path);
                    disp = afr;
                    sp = afr;
                }

                if (sp == null) return;

                // downmix to mono
                if (sp.WaveFormat.Channels == 2)
                {
                    sp = new NAudio.Wave.SampleProviders.StereoToMonoSampleProvider(sp)
                    {
                        LeftVolume = 0.5f,
                        RightVolume = 0.5f
                    };
                }
                else if (sp.WaveFormat.Channels > 2)
                {
                    sp = new MultiToMonoSampleProvider(sp);
                }

                // resample to stable SR for detector
                if (sp.WaveFormat.SampleRate != targetSr)
                    sp = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(sp, targetSr);

                int sr = sp.WaveFormat.SampleRate;

                // how many frames to analyze
                int framesToAnalyze = Math.Max(frameSize, secondsToAnalyze * sr);

                float[] hopBuf = new float[hopSize];         // mono hop
                float[] ring = new float[frameSize];
                float[] frame = new float[frameSize];
                int ringFill = 0;
                int analyzed = 0;

                while (analyzed < framesToAnalyze)
                {
                    int r = sp.Read(hopBuf, 0, hopBuf.Length);
                    if (r <= 0) break;

                    analyzed += r;

                    // if short read, zero pad
                    if (r < hopBuf.Length)
                        Array.Clear(hopBuf, r, hopBuf.Length - r);

                    for (int i = 0; i < hopSize; i++)
                    {
                        if (ringFill < frameSize)
                            ring[ringFill++] = hopBuf[i];

                        if (ringFill == frameSize)
                        {
                            Array.Copy(ring, 0, frame, 0, frameSize);

                            // simple energy gate (avoid junk frames)
                            double e = 0;
                            for (int k = 0; k < frameSize; k++) { double v = frame[k]; e += v * v; }
                            e = Math.Sqrt(e / frameSize);

                            if (e > 0.003) // ✅ less strict than 0.01
                            {
                                double f0 = AutoTune.DetectPitchHz(frame, 0, frameSize, sr, 55f, 900f);
                                if (f0 > 1e-6)
                                {
                                    double midi = AutoTune.HzToMidi(f0);
                                    int pc = ((int)Math.Round(midi)) % 12;
                                    if (pc < 0) pc += 12;

                                    // add weighted energy
                                    hist[pc] += weight * e;
                                }
                            }

                            // shift ring by hop
                            Array.Copy(ring, hopSize, ring, 0, frameSize - hopSize);
                            ringFill = frameSize - hopSize;
                        }
                    }
                }
            }
            catch
            {
                // ignore – just no contribution
            }
            finally
            {
                try { disp?.Dispose(); } catch { }
            }
        }

        // helper for >2ch downmix
        private sealed class MultiToMonoSampleProvider : ISampleProvider
        {
            private readonly ISampleProvider _src;
            public WaveFormat WaveFormat { get; }

            public MultiToMonoSampleProvider(ISampleProvider src)
            {
                _src = src;
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(src.WaveFormat.SampleRate, 1);
            }

            public int Read(float[] buffer, int offset, int count)
            {
                int ch = _src.WaveFormat.Channels;
                int needed = count * ch;
                float[] tmp = new float[needed];

                int r = _src.Read(tmp, 0, needed);
                if (r <= 0) return 0;

                int frames = r / ch;
                for (int i = 0; i < frames; i++)
                {
                    double s = 0;
                    int b = i * ch;
                    for (int c = 0; c < ch; c++) s += tmp[b + c];
                    buffer[offset + i] = (float)(s / ch);
                }
                return frames;
            }
        }






        private void UpdateSoloMuteVolumes()
        {
            lock (_previewVolLock)
            {
                if (_previewTrackVolumes.Count == 0)
                {
                    AppendLog("[SOLO/MUTE] Preview map empty (start Preview first).");
                    return;
                }

                bool anySolo = _previewTrackVolumes.Keys.Any(t => t.IsSolo);

                foreach (var kv in _previewTrackVolumes)
                {
                    var tr = kv.Key;
                    var vol = kv.Value;

                    bool audible = !tr.IsMute && (!anySolo || tr.IsSolo);

                    // ✅ base gain from slider (dB -> linear)
                    float baseGain = DbToLinear(tr.GainDb);

                    vol.Volume = audible ? baseGain : 0.0f;
                }
            }
        }

        private MixTrackVM GetOrCreateNextEmptyTrack()
        {
            // 1) use first empty slot
            for (int i = 0; i < _mixTracks.Count; i++)
            {
                var tr = _mixTracks[i];
                if (tr != null && !tr.HasAnyFile) return tr;
            }

            // 2) no empty slot -> increase track count by 1 (max 24)
            int cur = _mixTracks.Count;
            int next = Math.Min(24, cur + 1);

            // if already max, fallback to last track
            if (next <= cur)
                return _mixTracks.Count > 0 ? _mixTracks[_mixTracks.Count - 1] : null;

            // This should trigger your existing TrackCountCombo_SelectionChanged rebuild logic
            if (TrackCountCombo != null)
                TrackCountCombo.SelectedIndex = next - 1;

            // After rebuild, find empty again
            for (int i = 0; i < _mixTracks.Count; i++)
            {
                var tr = _mixTracks[i];
                if (tr != null && !tr.HasAnyFile) return tr;
            }

            // fallback
            return _mixTracks.Count > 0 ? _mixTracks[_mixTracks.Count - 1] : null;
        }

        private void UpdateAutoRecButtonVisual()
        {
            if (AutoRecButton == null)
                return;

            if (_automationRecorder.IsRecording)
            {
                AutoRecButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3B3B"));
                AutoRecButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#061427"));
                AutoRecButton.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD6D6"));
                AutoRecButton.Content = "REC";
                return;
            }

            if (_isAutomationRecordArmed)
            {
                AutoRecButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A1A00"));
                AutoRecButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE7BF"));
                AutoRecButton.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB347"));
                AutoRecButton.Content = "ARMED";
                return;
            }

            AutoRecButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A0B0B"));
            AutoRecButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD6D6"));
            AutoRecButton.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5A5A"));
            AutoRecButton.Content = "REC AUTO";
        }


       


        // ===================== YOUR EXISTING FUNCTIONS TO WIRE =====================
        // Βάλε εδώ μέσα το δικό σου πραγματικό logic (ή κάλεσε αυτό που έχεις ήδη).

        private void ApplyMixOnly()
        {
            // TODO: εδώ βάζεις το “apply” που θες (π.χ. finalize limiter settings, commit chain values, etc.)
            // ΜΗΝ κάνεις export εδώ.
        }


        private static float DbToLinear(double db)
        {
            return (float)Math.Pow(10.0, db / 20.0);
        }


        private float _detectedBpm = 120f;
        public string DetectedBpmText => $"BPM: {_detectedBpm:0} (auto)";
        private MixBusSampleProvider? _previewBus;
        private const bool IsDemoBuild = true;

       

        private bool _suppressAutoCreate;



        // ΣΙΓΟΥΡΟ parsing (δουλεύει είτε έχεις ComboBoxItem είτε απλό int)
        private int GetTrackCountFromCombo()
        {
            // ComboBoxItem Content is "1".."24"
            if (TrackCountCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem cbi
                && int.TryParse(cbi.Content?.ToString(), out int n))
                return n;

            // fallback: SelectedIndex 0..23 => 1..24
            return (TrackCountCombo?.SelectedIndex ?? 7) + 1;
        }

        private float GetLivePitchFactorForTrack(MixTrackVM tr)
        {
            if (tr == null) return 1f;

            // Kick/Drums untouched
            if (IsKickOrDrums(tr)) return 1f;

            // only tonal categories
            if (!IsTonal(tr)) return 1f;

            if (tr.OverrideKeyPc < 0) return 1f;

            int fromPc = tr.DetectedKeyPc;
            int toPc = tr.OverrideKeyPc;

            int diff = (toPc - fromPc) % 12;
            if (diff < -6) diff += 12;
            if (diff > 6) diff -= 12;

            return (float)Math.Pow(2.0, diff / 12.0);
        }

        private static bool IsKickOrDrums(MixTrackVM tr)
        {
            string c = (tr?.Category ?? "").Trim();
            string s = (tr?.Subcategory ?? "").Trim();

            return c.Equals("Kick", StringComparison.OrdinalIgnoreCase) ||
                   c.Equals("Drums", StringComparison.OrdinalIgnoreCase) ||
                   c.Equals("Drum Kits", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("Kick", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("Snare", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("Clap", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("HiHat", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("Perc", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("Cymbal", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTonal(MixTrackVM tr)
        {
            string c = (tr?.Category ?? "").Trim();

            return c.Equals("Vocals", StringComparison.OrdinalIgnoreCase) ||
                   c.Equals("Bass", StringComparison.OrdinalIgnoreCase) ||
                   c.Equals("Synth", StringComparison.OrdinalIgnoreCase) ||
                   c.Equals("Strings", StringComparison.OrdinalIgnoreCase);
        }


        private void ApplyTtsTimingLoopToTrack01()
        {
            try
            {
                if (_mixTracks == null || _mixTracks.Count == 0) return;

                var tr0 = _mixTracks[0];

                // Start seconds from UI (accept 2.45 or 2,45)
                double startSec = 0.0;
                string sStart = (TtsStartSecBox?.Text ?? "0").Trim().Replace(',', '.');
                double.TryParse(sStart,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out startSec);

                if (startSec < 0) startSec = 0;
                tr0.StartSeconds = startSec;

                // Loop count from combo (0/2/4/8)
                int loopCount = 0;
                if (TtsLoopCountCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem cbi &&
                    int.TryParse(cbi.Content?.ToString(), out int v))
                {
                    loopCount = v;
                }

                tr0.LoopCount = loopCount;

                AppendLog($"[TTS] Track01 timing set: Start={tr0.StartSeconds:0.00}s Loop={tr0.LoopCount}");
            }
            catch (Exception ex)
            {
                AppendLog("[TTS] ApplyTtsTimingLoopToTrack01 EX: " + ex.Message);
            }
        }
        private MixTrackVM? _lastGeneratedTtsTrack;

        private void PreviewOut_PlaybackStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
                AppendLog("[PREVIEW STOPPED] EX: " + e.Exception);
            else
                AppendLog("[PREVIEW STOPPED] (no exception)");

            Dispatcher.BeginInvoke(new Action(() =>
            {
                SetPreviewUiState(false);
            }));
        }

        private void RestoreMainShortcutFocus()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Activate();
                Focus();

                if (ShortcutFocusSink != null)
                    Keyboard.Focus(ShortcutFocusSink);
                else
                    Keyboard.Focus(this);

            }), System.Windows.Threading.DispatcherPriority.Input);
        }
        private void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsPreviewRunning())
                StopPreview();
            else
                StartPreview();

            RestoreMainShortcutFocus();
        }
      

        private string FormatPreviewTime(double seconds)
        {
            if (seconds < 0)
                seconds = 0;

            var ts = TimeSpan.FromSeconds(seconds);

            if (ts.TotalHours >= 1)
                return ts.ToString(@"hh\:mm\:ss");

            return ts.ToString(@"mm\:ss");
        }





        private void AutoRecButton_Click(object sender, RoutedEventArgs e)
        {
            if (_automationRecorder.IsRecording)
            {
                _automationRecorder.StopRecording();
                _isAutomationRecordArmed = false;
                UpdateAutoRecButtonVisual();
                AppendLog("[AUTOMATION] Auto record stopped.");
                return;
            }

            _isAutomationRecordArmed = !_isAutomationRecordArmed;
            UpdateAutoRecButtonVisual();

            AppendLog(_isAutomationRecordArmed
                ? "[AUTOMATION] Auto record armed. Waiting for preview."
                : "[AUTOMATION] Auto record disarmed.");
        }


        private void StartPreview()
        {
            StopPreview(false); // safety

            lock (_previewVolLock)
            {
                _previewTrackVolumes.Clear();
            }

            int targetSr = 44100;
            bool srFound = false;

            foreach (var tr in _mixTracks)
            {
                if (!tr.HasAnyFile) continue;

                try
                {
                    using var tmp = tr.IsStereo
                        ? new TrackInputProvider(tr.FileLeft, tr.FileRight)
                        : new TrackInputProvider(tr.FileMono);

                    targetSr = tmp.WaveFormat.SampleRate;
                    srFound = true;
                    break;
                }
                catch (NotSupportedException ex)
                {
                    MessageBox.Show(ex.Message, "Unsupported Audio File", MessageBoxButton.OK, MessageBoxImage.Warning);
                    AppendLog("[PREVIEW] Unsupported file: " + ex.Message);
                    SetPreviewUiState(false);
                    return;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Preview Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    AppendLog("[PREVIEW] Failed to inspect track: " + ex);
                    SetPreviewUiState(false);
                    return;
                }
            }

            if (!srFound)
            {
                AppendLog("[PREVIEW] No tracks loaded.");
                SetPreviewUiState(false);
                return;
            }

            AppendLog($"[PREVIEW] Target SR = {targetSr} Hz");

            var inputs = new List<ISampleProvider>();
            int added = 0;

            foreach (var tr in _mixTracks)
            {
                if (!tr.HasAnyFile) continue;

                TrackInputProvider input;
                try
                {
                    input = tr.IsStereo
                        ? new TrackInputProvider(tr.FileLeft, tr.FileRight)
                        : new TrackInputProvider(tr.FileMono);
                }
                catch (NotSupportedException ex)
                {
                    MessageBox.Show(ex.Message, "Unsupported Audio File", MessageBoxButton.OK, MessageBoxImage.Warning);
                    AppendLog("[PREVIEW] Unsupported file on preview: " + ex.Message);
                    StopPreview();
                    return;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Preview Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    AppendLog("[PREVIEW] Failed to open track for preview: " + ex);
                    StopPreview();
                    return;
                }

                _previewDisposables.Add(input);

                tr.Chain.Reset(input.WaveFormat.SampleRate, input.WaveFormat.Channels);

                ISampleProvider sp = new TrackChainSampleProvider(input, tr);

                if (sp.WaveFormat.Channels == 1)
                    sp = new MonoToStereoSampleProvider(sp);
                else if (sp.WaveFormat.Channels > 2)
                    throw new NotSupportedException("Preview supports only mono/stereo tracks.");

                if (sp.WaveFormat.SampleRate != targetSr)
                    sp = new WdlResamplingSampleProvider(sp, targetSr);

                if (tr.OverrideKeyPc >= 0 && IsTonal(tr) && !IsKickOrDrums(tr))
                {
                    sp = new TrackKeyShiftLiveSampleProvider(
                        sp,
                        getTargetFactor: () => GetLivePitchFactorForTrack(tr),
                        frameSize: 2048,
                        hopSize: 256,
                        retuneMs: 40f,
                        maxStepPerHop: 0.02f
                    );
                }

                if (tr.LoopCount == 2 || tr.LoopCount == 4 || tr.LoopCount == 8)
                    sp = new FiniteLoopSampleProvider(sp, tr.LoopCount);

                if (tr.StartSeconds > 0.0001)
                    sp = new OffsetSampleProvider(sp)
                    {
                        DelayBy = TimeSpan.FromSeconds(tr.StartSeconds),
                        LeadOut = TimeSpan.FromSeconds(60)
                    };

                var trackVol = new VolumeSampleProvider(sp)
                {
                    Volume = DbToLinear(tr.GainDb)
                };

                lock (_previewVolLock)
                {
                    _previewTrackVolumes[tr] = trackVol;
                }

                inputs.Add(trackVol);
                added++;
            }

            if (added == 0)
            {
                AppendLog("[PREVIEW] added==0.");
                SetPreviewUiState(false);
                return;
            }

            var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(targetSr, 2))
            {
                ReadFully = true
            };

            foreach (var i in inputs)
                mixer.AddMixerInput(i);

            UpdateSoloMuteVolumes();

            float g = 1.0f / (float)Math.Sqrt(Math.Max(1, added));
            _previewProvider = new VolumeSampleProvider(mixer) { Volume = g };

            var busPreset = MixingPresets.Get("Bus", "Main");
            ISampleProvider busProcessed = new BusChainSampleProvider(_previewProvider, busPreset);

            _previewOut = new WasapiOut(AudioClientShareMode.Shared, true, 200);
            _previewOut.PlaybackStopped -= PreviewOut_PlaybackStopped;
            _previewOut.PlaybackStopped += PreviewOut_PlaybackStopped;

            try
            {
                SetPreviewUiState(true); // δείξε STOP αμέσως
                CapturePreviewMixSnapshots();

                _previewOut.Init(busProcessed.ToWaveProvider());

                _automationTimeSec = 0.0;
                _previewStartTimeUtc = DateTime.UtcNow;

                if (PreviewTimeOverlayText != null)
                    PreviewTimeOverlayText.Text = "00:00";

                EnsureAutomationPreviewTimer();
                _automationPreviewTimer.Start();

                if (_isAutomationRecordArmed)
                {
                    _automationRecorder.StartRecording();
                    UpdateAutoRecButtonVisual();
                    AppendLog("[AUTOMATION] Auto record started with preview.");
                }
                else
                {
                    UpdateAutoRecButtonVisual();
                }

                _previewOut.Play();

                AppendLog($"[PREVIEW] Playing OK: {added} track(s)");
            }
            catch (Exception ex)
            {
                AppendLog("[PREVIEW] Init/Play EX: " + ex);
                StopPreview();
            }
        }
       

      

        private static float PcToHzInRange(int pc, float minHz, float maxHz)
        {
            // Base: C4 = 261.6256 Hz. pc 0=C, 1=C#, ...
            double c4 = 261.625565;
            double hz = c4 * Math.Pow(2.0, pc / 12.0);

            // shift octaves to fit range
            while (hz < minHz) hz *= 2.0;
            while (hz > maxHz) hz *= 0.5;

            // clamp
            if (hz < minHz) hz = minHz;
            if (hz > maxHz) hz = maxHz;

            return (float)hz;
        }


        private void Knob_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Controls.Slider s) return;

            _knobDragging = true;
            _activeKnob = s;
            _knobStartPt = e.GetPosition(this);
            _knobStartValue = s.Value;

            s.CaptureMouse();
            s.Focus();
            e.Handled = true;
        }

        private void Knob_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_knobDragging || _activeKnob == null) return;

            if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
            {
                EndKnobDrag();
                return;
            }

            var p = e.GetPosition(this);
            double dy = _knobStartPt.Y - p.Y;

            double sensitivity = 0.50;
            if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.LeftShift) ||
                System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.RightShift))
                sensitivity *= 0.25;
            if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.LeftCtrl) ||
                System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.RightCtrl))
                sensitivity *= 2.0;

            double newVal = _knobStartValue + dy * sensitivity;
            if (newVal < _activeKnob.Minimum) newVal = _activeKnob.Minimum;
            if (newVal > _activeKnob.Maximum) newVal = _activeKnob.Maximum;

            _activeKnob.Value = newVal;
            e.Handled = true;
        }

        private void Knob_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_knobDragging) EndKnobDrag();
            e.Handled = true;
        }

        private void Knob_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_knobDragging) EndKnobDrag();
        }

        private void EndKnobDrag()
        {
            try { _activeKnob?.ReleaseMouseCapture(); } catch { }
            _activeKnob = null;
            _knobDragging = false;
        }
        private void StopPreview()
        {
            StopPreview(true);
        }

        private void StopPreview(bool disarmAutoRec)
        {
            try { _previewOut?.PlaybackStopped -= PreviewOut_PlaybackStopped; } catch { }
            try { _previewOut?.Stop(); } catch { }
            try { _previewOut?.Dispose(); } catch { }

            _automationPreviewTimer?.Stop();
            _automationTimeSec = 0.0;
            _previewStartTimeUtc = default;

            _automationRecorder.StopRecording();

            if (disarmAutoRec)
                _isAutomationRecordArmed = false;

            if (PreviewTimeOverlayText != null)
                PreviewTimeOverlayText.Text = "00:00";

            _previewOut = null;
            _previewProvider = null;

            foreach (var d in _previewDisposables)
            {
                try { d.Dispose(); } catch { }
            }
            _previewDisposables.Clear();

            if (_skipPreviewMixRestoreOnce)
            {
                _skipPreviewMixRestoreOnce = false;
            }
            else
            {
                RestorePreviewMixSnapshots();
            }
            SetPreviewUiState(false);
            UpdateAutoRecButtonVisual();
            AppendLog("[PREVIEW] Stopped.");
        }

        private void UpdateAutomationTimeFromPreview()
        {
            if (_previewStartTimeUtc == default)
            {
                _automationTimeSec = 0.0;
                return;
            }

            _automationTimeSec = (DateTime.UtcNow - _previewStartTimeUtc).TotalSeconds;

            if (_automationTimeSec < 0)
                _automationTimeSec = 0.0;
        }



        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {

            // open the overlay finalize panel (not a separate window)
            if (FinalizeOverlay != null)
                FinalizeOverlay.Visibility = Visibility.Visible;
           
            {
                
            }
        }
        private void UpdateDetectedKeyText_ForExport()
        {
            try
            {
                var det = DetectKeyScaleForExport(); // must exist already
                int rootPc = det.Item1;
                ScaleType sc = det.Item2;

                string rootName = PcToName(rootPc);
                string scName = sc.ToString().Replace("HarmonicMinor", "Harmonic Minor");

                if (DetectedKeyText != null)
                    DetectedKeyText.Text = $"Detected: {rootName} {scName}";
            }
            catch
            {
                if (DetectedKeyText != null)
                    DetectedKeyText.Text = "Detected: —";
            }
        }

       



        private void ApplyOnlyMix(bool normalize)
        {
            // TODO: εδώ κάνε apply processing in-memory / chain update.
            // Αν δεν έχεις κάτι, άστο κενό προς το παρόν.
            AppendLog($"[APPLY] Apply only. Normalize={normalize}");
        }

        private void FinalizeCancel_Click(object sender, RoutedEventArgs e)
        {
            if (FinalizeOverlay != null)
                FinalizeOverlay.Visibility = Visibility.Collapsed;
        }


       

        // 3) μικρό helper για να κλείνουν readers όταν τελειώσει το render
        private sealed class AutoDisposeSampleProvider : ISampleProvider
        {
            private readonly ISampleProvider _inner;
            private readonly IDisposable _toDispose;

            public AutoDisposeSampleProvider(ISampleProvider inner, IDisposable toDispose)
            {
                _inner = inner;
                _toDispose = toDispose;
            }

            public WaveFormat WaveFormat => _inner.WaveFormat;

            public int Read(float[] buffer, int offset, int count)
            {
                int n = _inner.Read(buffer, offset, count);
                if (n == 0) _toDispose.Dispose();
                return n;
            }
        }



        private static float DbToLinear(float db) => (float)Math.Pow(10.0, db / 20.0);

        private static void RenderToWav(ISampleProvider provider, string path, TimeSpan length, bool normalize, bool export24Bit)
        {
            int sr = provider.WaveFormat.SampleRate;
            int ch = provider.WaveFormat.Channels;

            // ✅ one-pass trim (limiter keeps ceiling)
            if (normalize)
            {
                float trimDb = 3.0f;
                provider = new VolumeSampleProvider(provider)
                {
                    Volume = DbToLinear(trimDb)
                };
            }

            // ✅ bigger blocks = faster export
            int block = 16384 * ch;
            float[] buf = new float[block];

            // ✅ Safety cap: expected length + 5 sec tail.
            // If length is zero/bad, fallback to a reasonable cap (10 minutes).
            double safeSeconds = length.TotalSeconds;
            if (safeSeconds < 1.0) safeSeconds = 600.0; // fallback 10 min if length is bad

            long maxSamples = (long)((safeSeconds + 5.0) * sr * ch);
            if (maxSamples < block * 8) maxSamples = block * 8;

            long written = 0;

            if (!export24Bit)
            {
                using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sr, ch)))
                {
                    while (written < maxSamples)
                    {
                        int read = provider.Read(buf, 0, buf.Length);
                        if (read <= 0) break;

                        writer.WriteSamples(buf, 0, read);
                        written += read;
                    }
                }
                return;
            }

            // 24-bit PCM export
            var pcm24 = new WaveFormat(sr, 24, ch);
            using (var writer = new WaveFileWriter(path, pcm24))
            {
                while (written < maxSamples)
                {
                    int read = provider.Read(buf, 0, buf.Length);
                    if (read <= 0) break;

                    for (int i = 0; i < read; i++)
                    {
                        float s = buf[i];
                        if (s > 1f) s = 1f;
                        if (s < -1f) s = -1f;

                        int v = (int)(s * 8388607f); // 2^23-1
                        writer.WriteByte((byte)(v & 0xFF));
                        writer.WriteByte((byte)((v >> 8) & 0xFF));
                        writer.WriteByte((byte)((v >> 16) & 0xFF));
                    }

                    written += read;
                }
            }
        }


        private void SetDetectedBpm(float bpm)
        {
            if (bpm > 170f) bpm *= 0.5f;
            if (bpm < 70f) bpm *= 2.0f;

            foreach (var t in _mixTracks)
                t.DetectedBpm = bpm;
            _detectedBpm = Math.Max(40f, Math.Min(220f, bpm));
            // update UI if you use INotifyPropertyChanged on window, otherwise just set a TextBlock manually
            foreach (var tr in _mixTracks)
                tr.Chain.FxTime.TempoBpm = _detectedBpm;
        }
        private void CreateTracksButton_Click(object sender, RoutedEventArgs e)
        {
            CreateMixTracks(GetTrackCountFromCombo());
            WireTrackAutomationEvents();
            AppendLog($"Tracks created: {_mixTracks.Count}");
        }

        private ISampleProvider? BuildPreviewMixProvider()
        {
            // Αν ήδη παίζει αλλού, χρησιμοποίησε το ίδιο σημείο που φτιάχνεις _bus
            // και απλά γύρνα το τελικό ISampleProvider.
            return _bus; // ή return _previewBus;
        }

        private sealed class TrackSnapshot
        {
            public string Mono;
            public string Left;
            public string Right;
            public double StartSeconds;
            public int LoopCount;

            public string Category;
            public string Subcategory;

            public double GainDb;
        }

        private List<TrackSnapshot> SnapshotTracks()
        {
            var snap = new List<TrackSnapshot>(_mixTracks.Count);

            foreach (var tr in _mixTracks)
            {
                if (tr == null)
                {
                    snap.Add(new TrackSnapshot());
                    continue;
                }

                snap.Add(new TrackSnapshot
                {
                    // ⚠️ Αν τα fields/properties σου έχουν άλλα ονόματα, άλλαξέ τα εδώ ΜΟΝΟ
                    Mono = tr.FileMono,
                    Left = tr.FileLeft,
                    Right = tr.FileRight,

                    StartSeconds = tr.StartSeconds,
                    LoopCount = tr.LoopCount,

                    Category = tr.Category,
                    Subcategory = tr.Subcategory,

                    GainDb = tr.GainDb
                });
            }

            return snap;
        }

        private void RestoreTracks(List<TrackSnapshot> snap)
        {
            if (snap == null) return;

            int n = Math.Min(snap.Count, _mixTracks.Count);
            for (int i = 0; i < n; i++)
            {
                var tr = _mixTracks[i];
                var s = snap[i];
                if (tr == null || s == null) continue;

                // restore audio file(s)
                if (!string.IsNullOrWhiteSpace(s.Mono))
                    tr.SetMono(s.Mono);
                else if (!string.IsNullOrWhiteSpace(s.Left) && !string.IsNullOrWhiteSpace(s.Right))
                    tr.SetStereo(s.Left, s.Right);

                // restore settings
                tr.StartSeconds = s.StartSeconds;
                tr.LoopCount = s.LoopCount;

                tr.Category = s.Category;
                tr.Subcategory = s.Subcategory;

                tr.GainDb = s.GainDb;

                // refresh waveform for restored tracks (optional but useful)
                try
                {
                    tr.RebuildWaveform(520, 60, 360);
                    RefreshWaveformCanvas(tr);
                }
                catch { }
            }
        }

        // returns an empty track, creating one extra track if needed WITHOUT losing existing audio
        private MixTrackVM GetOrCreateNextEmptyTrack_Safe()
        {
            // 1) use first empty slot
            foreach (var tr in _mixTracks)
                if (tr != null && !tr.HasAnyFile)
                    return tr;

            // 2) no empty slot -> increase by 1 (max 24)
            int cur = _mixTracks.Count;
            int next = Math.Min(24, cur + 1);

            if (next <= cur)
                return _mixTracks.Count > 0 ? _mixTracks[_mixTracks.Count - 1] : null;

            if (TrackCountCombo != null)
                TrackCountCombo.SelectedIndex = next - 1;

            foreach (var tr in _mixTracks)
                if (tr != null && !tr.HasAnyFile)
                    return tr;

            return _mixTracks.Count > 0 ? _mixTracks[_mixTracks.Count - 1] : null;
        }

        private void ApplyTtsValuesToTrack(MixTrackVM tr)
        {
            if (tr == null) return;

            // Start seconds
            double startSec = 0.0;
            string sStart = (TtsStartSecBox?.Text ?? "0").Trim().Replace(',', '.');
            double.TryParse(sStart,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out startSec);
            tr.StartSeconds = Math.Max(0.0, startSec);

            // Loop 0/2/4/8
            int loopCount = 0;
            if (TtsLoopCountCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem cbi &&
                int.TryParse(cbi.Content?.ToString(), out int v))
                loopCount = v;
            tr.LoopCount = loopCount;
        }

        private void RenderToWavFile(ISampleProvider provider, string path, TimeSpan length)
        {
            // Ensure 32-bit float WAV for now (best for further mastering)
            var wf = provider.WaveFormat;
            int sampleRate = wf.SampleRate;
            int channels = wf.Channels;

            int totalSamples = (int)(length.TotalSeconds * sampleRate * channels);
            int block = 4096 * channels;
            float[] buffer = new float[block];

            using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels));

            int written = 0;
            while (written < totalSamples)
            {
                int need = Math.Min(block, totalSamples - written);
                int read = provider.Read(buffer, 0, need);
                if (read <= 0) break;

                writer.WriteSamples(buffer, 0, read);
                written += read;
            }
        }

        private TimeSpan GetLongestLoadedTrackDuration()
        {
            // Αν έχεις durations στο VM, χρησιμοποίησέ τα.
            // Εδώ δίνω safe fallback: 0 = θα χρησιμοποιήσει default (π.χ. 5min)
            try
            {
                TimeSpan max = TimeSpan.Zero;

                foreach (var tr in _mixTracks)
                {
                    // Αν έχεις αποθηκεύσει duration όταν φορτώνεις (προτείνεται):
                    // max = (tr.Duration > max) ? tr.Duration : max;

                    // Αν δεν έχεις ακόμα duration, μην κάνεις τίποτα εδώ.
                }

                return max;
            }
            catch { return TimeSpan.Zero; }
        }

        private TimeSpan GetLongestLoadedDuration()
        {
            TimeSpan max = TimeSpan.Zero;

            foreach (var tr in _mixTracks)
            {
                try
                {
                    if (tr.IsStereo)
                    {
                        if (!string.IsNullOrWhiteSpace(tr.FileLeft))
                        {
                            using var rL = new AudioFileReader(tr.FileLeft);
                            if (rL.TotalTime > max) max = rL.TotalTime;
                        }
                        if (!string.IsNullOrWhiteSpace(tr.FileRight))
                        {
                            using var rR = new AudioFileReader(tr.FileRight);
                            if (rR.TotalTime > max) max = rR.TotalTime;
                        }
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(tr.FileMono))
                        {
                            using var rM = new AudioFileReader(tr.FileMono);
                            if (rM.TotalTime > max) max = rM.TotalTime;
                        }
                    }
                }
                catch { }
            }

            // μικρό safety padding για tails (reverb/delay)
            if (max > TimeSpan.Zero)
                max += TimeSpan.FromSeconds(3);

            return max;
        }


        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            StopPreview();
            Close();
        }

        private WaveformCanvas? FindWaveformCanvasForTrack(MixTrackVM tr)
        {
            var container = TracksList.ItemContainerGenerator.ContainerFromItem(tr) as DependencyObject;
            if (container == null) return null;
            return FindChild<WaveformCanvas>(container);
        }

        private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var r = FindChild<T>(child);
                if (r != null) return r;
            }
            return null;
        }

        private void RefreshWaveformCanvas(MixTrackVM tr)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var wc = FindWaveformCanvasForTrack(tr);
                wc?.SetSamples(tr.WaveformSamples ?? Array.Empty<float>());
            }));
        }

        private void CreateMixTracks(int count)
        {
            if (count < 1) count = 1;
            if (count > 24) count = 24;

            int cur = _mixTracks.Count;

            // ✅ INCREASE: keep existing tracks, add new ones
            if (count > cur)
            {
                for (int i = cur + 1; i <= count; i++)
                    _mixTracks.Add(new MixTrackVM(i));

                return;
            }

            // ✅ DECREASE: only remove extra tracks if they are empty
            if (count < cur)
            {
                // If any track that would be removed has audio, do NOT shrink
                for (int i = count; i < cur; i++)
                {
                    if (_mixTracks[i] != null && _mixTracks[i].HasAnyFile)
                    {
                        // revert combo selection back to current count
                        _suppressAutoCreate = true;
                        try
                        {
                            if (TrackCountCombo != null)
                                TrackCountCombo.SelectedIndex = cur - 1;
                        }
                        finally { _suppressAutoCreate = false; }

                        AppendLog("[TRACKS] Cannot reduce tracks: some removed tracks contain audio.");
                        return;
                    }
                }

                // remove from end down to count
                for (int i = cur - 1; i >= count; i--)
                    _mixTracks.RemoveAt(i);
            }
        }

        // ===== OPEN WAV (1 = MONO, 2 = STEREO L/R, >2 = warning) =====
        private void OpenWav_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is System.Windows.Controls.Button b && b.Tag is MixTrackVM tr))
                return;

            var dlg = new OpenFileDialog
            {
                Title = "Select 1 file (MONO) or 2 files (STEREO L/R)",
                Filter = "Audio Files|*.wav;*.mp3;*.aiff;*.aif;*.flac|WAV (*.wav)|*.wav|All Files|*.*",
                Multiselect = true
            };

            if (dlg.ShowDialog(this) != true)
                return;

            var files = dlg.FileNames;
            if (files == null || files.Length == 0)
                return;

            if (files.Length > 2)
            {
                MessageBox.Show(" Max two Audio Files.", "Stereo choice",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                AppendLog($"[WARN] {tr.Name}: user selected {files.Length} files (ignored).");
                return;
            }

            // mismatch warning based on filename keywords vs selected Category
            string title0 = System.IO.Path.GetFileNameWithoutExtension(files[0]) ?? "";
            string title1 = files.Length == 2 ? (System.IO.Path.GetFileNameWithoutExtension(files[1]) ?? "") : "";

            if (ShouldWarnMismatch(tr.Category, title0, title1))
            {
                var res = MessageBox.Show(
                    $"Προειδοποίηση: Το αρχείο μοιάζει να είναι \"{GuessTypeFromTitle(title0)}\" " +
                    $"αλλά το track είναι \"{tr.Category}\".\n\nΝα συνεχίσω;",
                    "Πιθανό mismatch",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (res != MessageBoxResult.Yes)
                {
                    AppendLog($"[CANCEL] {tr.Name} | {tr.CategoryDisplay} | {title0}");
                    return;
                }
            }

            // Load mono/stereo
            if (files.Length == 1)
            {
                tr.SetMono(files[0]);
                AppendLog($"{tr.Name} loaded MONO: {System.IO.Path.GetFileName(files[0])}");
            }
            else
            {
                tr.SetStereo(files[0], files[1]);
                AppendLog($"{tr.Name} loaded STEREO: L={System.IO.Path.GetFileName(files[0])} | R={System.IO.Path.GetFileName(files[1])}");
            }

            // ✅ Rebuild waveform:
            // WAV -> Geometry (your existing manual WAV builder)
            // non-WAV -> WaveformSamples (dense, internal count handled inside RebuildWaveform)
            tr.RebuildWaveform(520, 60, 360);

            // ✅ refresh ONLY ONCE
            RefreshWaveformCanvas(tr);

            UpdateTrackDetectedLabel(tr);
            RestoreMainShortcutFocus();
            // AUTO BPM detect (use the first file as reference) + update global tempo
            try
            {
                float bpm = BpmDete.DetectBpmFromFile(files[0], 30); SetDetectedBpm(bpm);
                AppendLog($"{tr.Name} BPM auto-detected: {bpm:0}");
            }
            catch (Exception ex)
            {
                AppendLog($"[WARN] BPM detect failed: {ex.Message}");
            }
        }





        private void Solo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is MixTrackVM tr)
            {
                tr.IsSolo = !tr.IsSolo;
                UpdateSoloMuteVolumes(); // ✅
            }
        }

        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is MixTrackVM tr)
            {
                tr.IsMute = !tr.IsMute;
                UpdateSoloMuteVolumes(); // ✅
            }
        }


        private static bool ShouldWarnMismatch(string category, string title0, string title1)
        {
            string t = (title0 + " " + title1).ToLowerInvariant();
            string guessed = GuessTypeFromTitle(t);
            if (guessed == "Unknown") return false;

            if (category.Equals("Kick", StringComparison.OrdinalIgnoreCase) && guessed != "Kick")
                return true;

            if (category.Equals("Vocals", StringComparison.OrdinalIgnoreCase) &&
                (guessed == "Kick" || guessed == "Snare" || guessed == "HiHat"))
                return true;

            if (category.Equals("FX", StringComparison.OrdinalIgnoreCase) &&
                (guessed == "Kick" || guessed == "Snare" || guessed == "HiHat"))
                return true;

            // Drum Kits can contain many drums, so less strict
            if (category.Equals("Drum Kits", StringComparison.OrdinalIgnoreCase))
                return false;

            return false;
        }

        private static string GuessTypeFromTitle(string title)
        {
            string t = (title ?? "").ToLowerInvariant();

            if (ContainsAny(t, "snare", "snr", "rim")) return "Snare";
            if (ContainsAny(t, "kick", "kck", "bd", "bassdrum", "bass drum")) return "Kick";
            if (ContainsAny(t, "hihat", "hi hat", "hat", "hh")) return "HiHat";
            if (ContainsAny(t, "clap")) return "Clap";
            if (ContainsAny(t, "tom")) return "Tom";
            if (ContainsAny(t, "vocal", "vox", "voice")) return "Vocal";
            if (ContainsAny(t, "fx", "sweep", "riser", "impact", "downlifter")) return "FX";

            return "Unknown";
        }

        private static bool ContainsAny(string t, params string[] keys)
        {
            foreach (var k in keys)
                if (!string.IsNullOrWhiteSpace(k) && t.Contains(k))
                    return true;
            return false;
        }

        // =====================================================================================
        // PREVIEW ENGINE (προαιρετικό – αν δεν έχεις κουμπιά PLAY/STOP στο XAML, δεν πειράζει)
        // =====================================================================================
        private WaveOutEvent? _out;
        private MixBusSampleProvider? _bus;

        private void TtsTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }
}



