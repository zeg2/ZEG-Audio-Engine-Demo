using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace ZEGAudioEngineAI

{
    public sealed class MixTrackVM : INotifyPropertyChanged
    {

        // ===================== WAVEFORM (SAFE WAV-ONLY, NO ACM) =====================
        private Geometry _waveformGeometry = Geometry.Empty;
        public Geometry WaveformGeometry
        {
            get => _waveformGeometry;
            private set
            {
                _waveformGeometry = value;
                OnPropertyChanged();
            }
        }
        private float[] _waveformSamples = Array.Empty<float>();
        public float[] WaveformSamples
        {
            get => _waveformSamples;
            private set { _waveformSamples = value; OnPropertyChanged(); }
        }




        private TimeSpan GetAudioDurationSafe(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                    return TimeSpan.Zero;

                string ext = Path.GetExtension(filePath)?.ToLowerInvariant() ?? "";

                if (ext == ".wav")
                {
                    using var reader = new WaveFileReader(filePath);
                    return reader.TotalTime;
                }

                if (ext == ".aiff" || ext == ".aif")
                {
                    using var reader = new AiffFileReader(filePath);
                    return reader.TotalTime;
                }

                using var audio = new AudioFileReader(filePath);
                return audio.TotalTime;
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }

        private double _fxReverbHoldPercent = 0.0;
        public double FxReverbHoldPercent
        {
            get => _fxReverbHoldPercent;
            set
            {
                if (Math.Abs(_fxReverbHoldPercent - value) < 0.001) return;
                _fxReverbHoldPercent = value;
                OnPropertyChanged(nameof(FxReverbHoldPercent));
            }
        }
        private double _fxChorusRate = 0.35;
        public double FxChorusRate
        {
            get => _fxChorusRate;
            set
            {
                if (Math.Abs(_fxChorusRate - value) < 0.0001) return;
                _fxChorusRate = value;
                OnPropertyChanged(nameof(FxChorusRate));
            }
        }

        private double _fxChorusDepthPercent = 30.0;
        public double FxChorusDepthPercent
        {
            get => _fxChorusDepthPercent;
            set
            {
                if (Math.Abs(_fxChorusDepthPercent - value) < 0.001) return;
                _fxChorusDepthPercent = value;
                OnPropertyChanged(nameof(FxChorusDepthPercent));
            }
        }

        private double _fxChorusPhasePercent = 50.0;
        public double FxChorusPhasePercent
        {
            get => _fxChorusPhasePercent;
            set
            {
                if (Math.Abs(_fxChorusPhasePercent - value) < 0.001) return;
                _fxChorusPhasePercent = value;
                OnPropertyChanged(nameof(FxChorusPhasePercent));
            }
        }

        private double _fxChorusMixPercent = 0.0;
        public double FxChorusMixPercent
        {
            get => _fxChorusMixPercent;
            set
            {
                if (Math.Abs(_fxChorusMixPercent - value) < 0.001) return;
                _fxChorusMixPercent = value;
                OnPropertyChanged(nameof(FxChorusMixPercent));
                RaiseAutomationValueChanged(AutomationTarget.FxChorusMixPercent, value);
            }
        }

        private double _fxDistDrivePercent = 35.0;
        public double FxDistDrivePercent
        {
            get => _fxDistDrivePercent;
            set
            {
                if (Math.Abs(_fxDistDrivePercent - value) < 0.001) return;
                _fxDistDrivePercent = value;
                OnPropertyChanged(nameof(FxDistDrivePercent));
            }
        }

        private int _octaveShiftMode = 0;
        public int OctaveShiftMode
        {
            get => _octaveShiftMode;
            set
            {
                if (_octaveShiftMode == value) return;
                _octaveShiftMode = value;
                OnPropertyChanged(nameof(OctaveShiftMode));
            }
        }

        private double _fxOctaveMixPercent = 0.0;
        public double FxOctaveMixPercent
        {
            get => _fxOctaveMixPercent;
            set
            {
                if (Math.Abs(_fxOctaveMixPercent - value) < 0.001) return;
                _fxOctaveMixPercent = value;
                OnPropertyChanged(nameof(FxOctaveMixPercent));
                RaiseAutomationValueChanged(AutomationTarget.FxOctaveMixPercent, value);
            }
        }

        private double _fxOctaveFormantPercent = 50.0;
        public double FxOctaveFormantPercent
        {
            get => _fxOctaveFormantPercent;
            set
            {
                if (Math.Abs(_fxOctaveFormantPercent - value) < 0.001) return;
                _fxOctaveFormantPercent = value;
                OnPropertyChanged(nameof(FxOctaveFormantPercent));
            }
        }

        private double _fxOctaveOutputPercent = 100.0;
        public double FxOctaveOutputPercent
        {
            get => _fxOctaveOutputPercent;
            set
            {
                if (Math.Abs(_fxOctaveOutputPercent - value) < 0.001) return;
                _fxOctaveOutputPercent = value;
                OnPropertyChanged(nameof(FxOctaveOutputPercent));
            }
        }

        private bool _fxOctave;
        public bool FxOctave
        {
            get => _fxOctave;
            set
            {
                if (_fxOctave == value) return;
                _fxOctave = value;
                OnPropertyChanged(nameof(FxOctave));
            }
        }




        private double _fxOctaveWidthPercent = 50.0;
        public double FxOctaveWidthPercent
        {
            get => _fxOctaveWidthPercent;
            set
            {
                if (Math.Abs(_fxOctaveWidthPercent - value) < 0.001) return;
                _fxOctaveWidthPercent = value;
                OnPropertyChanged(nameof(FxOctaveWidthPercent));
            }
        }

        private double _fxOctaveLfoRateHz = 20.0;
        public double FxOctaveLfoRateHz
        {
            get => _fxOctaveLfoRateHz;
            set
            {
                if (Math.Abs(_fxOctaveLfoRateHz - value) < 0.001) return;
                _fxOctaveLfoRateHz = value;
                OnPropertyChanged(nameof(FxOctaveLfoRateHz));
            }
        }

        private double _fxOctaveLfoAmountPercent = 0.0;
        public double FxOctaveLfoAmountPercent
        {
            get => _fxOctaveLfoAmountPercent;
            set
            {
                if (Math.Abs(_fxOctaveLfoAmountPercent - value) < 0.001) return;
                _fxOctaveLfoAmountPercent = value;
                OnPropertyChanged(nameof(FxOctaveLfoAmountPercent));
            }
        }

        private bool _fxOctaveLfoSyncEnabled = false;
        public bool FxOctaveLfoSyncEnabled
        {
            get => _fxOctaveLfoSyncEnabled;
            set
            {
                if (_fxOctaveLfoSyncEnabled == value) return;
                _fxOctaveLfoSyncEnabled = value;
                OnPropertyChanged(nameof(FxOctaveLfoSyncEnabled));
            }
        }

        private int _fxOctaveLfoStep = 8;
        public int FxOctaveLfoStep
        {
            get => _fxOctaveLfoStep;
            set
            {
                if (_fxOctaveLfoStep == value) return;
                _fxOctaveLfoStep = value;
                OnPropertyChanged(nameof(FxOctaveLfoStep));
            }
        }



        private double _fxOctavePitchSemitones = -12.0;
        public double FxOctavePitchSemitones
        {
            get => _fxOctavePitchSemitones;
            set
            {
                if (Math.Abs(_fxOctavePitchSemitones - value) < 0.001) return;
                _fxOctavePitchSemitones = value;
                OnPropertyChanged(nameof(FxOctavePitchSemitones));
            }
        }




        /// <summary>
        /// Builds waveform for UI. SAFE: reads only WAV PCM/Float/Extensible with manual parsing.
        /// If file is not .wav or unsupported encoding, waveform stays empty (no crash).
        /// </summary>
        public void RebuildWaveform(int width = 520, int height = 66, int points = 240)
        {
            try
            {
                string path = IsStereo ? FileLeft : FileMono;

                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    WaveformGeometry = Geometry.Empty;
                    WaveformSamples = Array.Empty<float>();
                    return;
                }

                WaveformGeometry = Geometry.Empty;

                int outCount = 1700; // αυτό που σου αρέσει τώρα

                if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                    WaveformSamples = BuildWaveformSamplesFromWavManual(path, outCount);
                else
                    WaveformSamples = BuildWaveformSamplesFromAnyAudio(path, outCount); // το non-wav builder που ήδη έχεις
            }
            catch
            {
                WaveformGeometry = Geometry.Empty;
                WaveformSamples = Array.Empty<float>();
            }
        }

        private int _fxPage = 0;   // 0 = first 4 fx, 1 = next 4 fx
        public int FxPage
        {
            get => _fxPage;
            set
            {
                int v = value < 0 ? 0 : value > 1 ? 1 : value;
                if (_fxPage == v) return;
                _fxPage = v;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsFxPage1));
                OnPropertyChanged(nameof(IsFxPage2));
                OnPropertyChanged(nameof(FxPageButtonText));
            }
        }

        private int _hpFilterMode = 0; // 0=12dB, 1=24dB, 2=DRIVE
        public int HpFilterMode
        {
            get => _hpFilterMode;
            set
            {
                if (_hpFilterMode == value) return;
                _hpFilterMode = value;
                OnPropertyChanged(nameof(HpFilterMode));
            }
        }

        private double _fxHpFilterMixPercent = 0;
        public double FxHpFilterMixPercent
        {
            get => _fxHpFilterMixPercent;
            set
            {
                if (Math.Abs(_fxHpFilterMixPercent - value) < 0.001) return;
                _fxHpFilterMixPercent = value;
                OnPropertyChanged(nameof(FxHpFilterMixPercent));
                RaiseAutomationValueChanged(AutomationTarget.FxHpFilterMixPercent, value);
            }
        }

        private double _fxHpFilterCutoffPercent = 0;
        public double FxHpFilterCutoffPercent
        {
            get => _fxHpFilterCutoffPercent;
            set
            {
                if (Math.Abs(_fxHpFilterCutoffPercent - value) < 0.001) return;
                _fxHpFilterCutoffPercent = value;
                OnPropertyChanged(nameof(FxHpFilterCutoffPercent));
            }
        }

        private double _fxHpFilterResonancePercent = 0;
        public double FxHpFilterResonancePercent
        {
            get => _fxHpFilterResonancePercent;
            set
            {
                if (Math.Abs(_fxHpFilterResonancePercent - value) < 0.001) return;
                _fxHpFilterResonancePercent = value;
                OnPropertyChanged(nameof(FxHpFilterResonancePercent));
            }
        }

        private double _fxHpFilterBodyBoostPercent = 0;
        public double FxHpFilterBodyBoostPercent
        {
            get => _fxHpFilterBodyBoostPercent;
            set
            {
                if (Math.Abs(_fxHpFilterBodyBoostPercent - value) < 0.001) return;
                _fxHpFilterBodyBoostPercent = value;
                OnPropertyChanged(nameof(FxHpFilterBodyBoostPercent));
            }
        }
        private double _fxLpFilterLfoRateHz = 35.0;
        public double FxLpFilterLfoRateHz
        {
            get => _fxLpFilterLfoRateHz;
            set
            {
                if (Math.Abs(_fxLpFilterLfoRateHz - value) < 0.0001) return;
                _fxLpFilterLfoRateHz = value;
                OnPropertyChanged(nameof(FxLpFilterLfoRateHz));
            }
        }

        private double _fxLpFilterLfoAmountPercent = 0;
        public double FxLpFilterLfoAmountPercent
        {
            get => _fxLpFilterLfoAmountPercent;
            set
            {
                if (Math.Abs(_fxLpFilterLfoAmountPercent - value) < 0.001) return;
                _fxLpFilterLfoAmountPercent = value;
                OnPropertyChanged(nameof(FxLpFilterLfoAmountPercent));
            }
        }

        private bool _fxLpFilterLfoSyncEnabled = false;
        public bool FxLpFilterLfoSyncEnabled
        {
            get => _fxLpFilterLfoSyncEnabled;
            set
            {
                if (_fxLpFilterLfoSyncEnabled == value) return;
                _fxLpFilterLfoSyncEnabled = value;
                OnPropertyChanged(nameof(FxLpFilterLfoSyncEnabled));
            }
        }

        private int _fxLpFilterLfoStep = 8;
        public int FxLpFilterLfoStep
        {
            get => _fxLpFilterLfoStep;
            set
            {
                if (_fxLpFilterLfoStep == value) return;
                _fxLpFilterLfoStep = value;
                OnPropertyChanged(nameof(FxLpFilterLfoStep));
            }
        }

        private double _fxHpFilterLfoRateHz = 35.0;
        public double FxHpFilterLfoRateHz
        {
            get => _fxHpFilterLfoRateHz;
            set
            {
                if (Math.Abs(_fxHpFilterLfoRateHz - value) < 0.0001) return;
                _fxHpFilterLfoRateHz = value;
                OnPropertyChanged(nameof(FxHpFilterLfoRateHz));
            }
        }

        private double _fxHpFilterLfoAmountPercent = 0;
        public double FxHpFilterLfoAmountPercent
        {
            get => _fxHpFilterLfoAmountPercent;
            set
            {
                if (Math.Abs(_fxHpFilterLfoAmountPercent - value) < 0.001) return;
                _fxHpFilterLfoAmountPercent = value;
                OnPropertyChanged(nameof(FxHpFilterLfoAmountPercent));
            }
        }

        private bool _fxHpFilterLfoSyncEnabled = false;
        public bool FxHpFilterLfoSyncEnabled
        {
            get => _fxHpFilterLfoSyncEnabled;
            set
            {
                if (_fxHpFilterLfoSyncEnabled == value) return;
                _fxHpFilterLfoSyncEnabled = value;
                OnPropertyChanged(nameof(FxHpFilterLfoSyncEnabled));
            }
        }

        private int _fxHpFilterLfoStep = 8;
        public int FxHpFilterLfoStep
        {
            get => _fxHpFilterLfoStep;
            set
            {
                if (_fxHpFilterLfoStep == value) return;
                _fxHpFilterLfoStep = value;
                OnPropertyChanged(nameof(FxHpFilterLfoStep));
            }
        }


        private double _fxReverbDecayPercent = 82;
        public double FxReverbDecayPercent
        {
            get => _fxReverbDecayPercent;
            set
            {
                if (Math.Abs(_fxReverbDecayPercent - value) < 0.001) return;
                _fxReverbDecayPercent = value;
                OnPropertyChanged(nameof(FxReverbDecayPercent));
            }
        }

        private double _fxReverbSizePercent = 68;
        public double FxReverbSizePercent
        {
            get => _fxReverbSizePercent;
            set
            {
                if (Math.Abs(_fxReverbSizePercent - value) < 0.001) return;
                _fxReverbSizePercent = value;
                OnPropertyChanged(nameof(FxReverbSizePercent));
            }
        }

        private double _fxReverbPreDelayMs = 8;
        public double FxReverbPreDelayMs
        {
            get => _fxReverbPreDelayMs;
            set
            {
                if (Math.Abs(_fxReverbPreDelayMs - value) < 0.001) return;
                _fxReverbPreDelayMs = value;
                OnPropertyChanged(nameof(FxReverbPreDelayMs));
            }
        }

        private double _fxReverbDampingPercent = 54;
        public double FxReverbDampingPercent
        {
            get => _fxReverbDampingPercent;
            set
            {
                if (Math.Abs(_fxReverbDampingPercent - value) < 0.001) return;
                _fxReverbDampingPercent = value;
                OnPropertyChanged(nameof(FxReverbDampingPercent));
            }
        }

        private double _fxReverbWidthPercent = 72;
        public double FxReverbWidthPercent
        {
            get => _fxReverbWidthPercent;
            set
            {
                if (Math.Abs(_fxReverbWidthPercent - value) < 0.001) return;
                _fxReverbWidthPercent = value;
                OnPropertyChanged(nameof(FxReverbWidthPercent));
            }
        }

        private double _fxReverbDiffusePercent = 74;
        public double FxReverbDiffusePercent
        {
            get => _fxReverbDiffusePercent;
            set
            {
                if (Math.Abs(_fxReverbDiffusePercent - value) < 0.001) return;
                _fxReverbDiffusePercent = value;
                OnPropertyChanged(nameof(FxReverbDiffusePercent));
            }
        }

        private double _fxReverbTonePercent = 48;
        public double FxReverbTonePercent
        {
            get => _fxReverbTonePercent;
            set
            {
                if (Math.Abs(_fxReverbTonePercent - value) < 0.001) return;
                _fxReverbTonePercent = value;
                OnPropertyChanged(nameof(FxReverbTonePercent));
            }
        }

        private int _lpFilterMode = 0; // 0=12dB, 1=24dB, 2=DRIVE
        public int LpFilterMode
        {
            get => _lpFilterMode;
            set
            {
                if (_lpFilterMode == value) return;
                _lpFilterMode = value;
                OnPropertyChanged(nameof(LpFilterMode));
            }
        }

        private double _fxLpFilterCutoffPercent = 100;
        public double FxLpFilterCutoffPercent
        {
            get => _fxLpFilterCutoffPercent;
            set
            {
                if (Math.Abs(_fxLpFilterCutoffPercent - value) < 0.001) return;
                _fxLpFilterCutoffPercent = value;
                OnPropertyChanged(nameof(FxLpFilterCutoffPercent));
            }
        }

        private double _fxLpFilterResonancePercent = 0;
        public double FxLpFilterResonancePercent
        {
            get => _fxLpFilterResonancePercent;
            set
            {
                if (Math.Abs(_fxLpFilterResonancePercent - value) < 0.001) return;
                _fxLpFilterResonancePercent = value;
                OnPropertyChanged(nameof(FxLpFilterResonancePercent));
            }
        }

        private double _fxLpFilterBassBoostPercent = 0;
        public double FxLpFilterBassBoostPercent
        {
            get => _fxLpFilterBassBoostPercent;
            set
            {
                if (Math.Abs(_fxLpFilterBassBoostPercent - value) < 0.001) return;
                _fxLpFilterBassBoostPercent = value;
                OnPropertyChanged(nameof(FxLpFilterBassBoostPercent));
            }
        }

        private double _fxTimeStretchAmountPercent = 0;

        public double FxTimeStretchAmountPercent
        {
            get => _fxTimeStretchAmountPercent;
            set
            {
                if (_fxTimeStretchAmountPercent == value) return;
                _fxTimeStretchAmountPercent = value;
                Chain.TimeStretchAmount = (float)(_fxTimeStretchAmountPercent / 100.0);
                OnPropertyChanged(nameof(FxTimeStretchAmountPercent));
                OnPropertyChanged(nameof(FxTimeStretchAmount));
            }
        }
        private int _chorusMode = 0;
        public int ChorusMode
        {
            get => _chorusMode;
            set
            {
                if (_chorusMode == value) return;
                _chorusMode = value;
                OnPropertyChanged(nameof(ChorusMode));
            }
        }

        private double _fxChorusWidthPercent = 25;
        public double FxChorusWidthPercent
        {
            get => _fxChorusWidthPercent;
            set
            {
                if (Math.Abs(_fxChorusWidthPercent - value) < 0.001) return;
                _fxChorusWidthPercent = value;
                OnPropertyChanged(nameof(FxChorusWidthPercent));
            }
        }

        private int _fxOctaveLfoShape = 0;
        public int FxOctaveLfoShape
        {
            get => _fxOctaveLfoShape;
            set
            {
                if (_fxOctaveLfoShape == value) return;
                _fxOctaveLfoShape = value;
                OnPropertyChanged(nameof(FxOctaveLfoShape));
            }
        }



        private double _fxDelayTonePercent = 50;
        public double FxDelayTonePercent
        {
            get => _fxDelayTonePercent;
            set
            {
                if (_fxDelayTonePercent == value) return;
                _fxDelayTonePercent = value;
                OnPropertyChanged(nameof(FxDelayTonePercent));
            }
        }

        private double _fxReverbDarknessPercent = 40;
        public double FxReverbDarknessPercent
        {
            get => _fxReverbDarknessPercent;
            set
            {
                if (_fxReverbDarknessPercent == value) return;
                _fxReverbDarknessPercent = value;
                OnPropertyChanged(nameof(FxReverbDarknessPercent));
            }
        }

        private bool _fxDelaySyncEnabled = true;
        public bool FxDelaySyncEnabled
        {
            get => _fxDelaySyncEnabled;
            set
            {
                if (_fxDelaySyncEnabled == value) return;
                _fxDelaySyncEnabled = value;
                OnPropertyChanged(nameof(FxDelaySyncEnabled));
            }
        }

        private int _fxDelayStep = 16;
        public int FxDelayStep
        {
            get => _fxDelayStep;
            set
            {
                if (_fxDelayStep == value) return;
                _fxDelayStep = value;
                OnPropertyChanged(nameof(FxDelayStep));
            }
        }

        // -1..+1 mapped from -100..+100
        public double FxTimeStretchAmount => _fxTimeStretchAmountPercent / 100.0;
        private double _fxLpFilterMixPercent = 0;
        public double FxLpFilterMixPercent
        {
            get => _fxLpFilterMixPercent;
            set
            {
                if (Math.Abs(_fxLpFilterMixPercent - value) < 0.001) return;
                _fxLpFilterMixPercent = value;
                OnPropertyChanged(nameof(FxLpFilterMixPercent));
                RaiseAutomationValueChanged(AutomationTarget.FxLpFilterMixPercent, value);
            }
        }


        public event Action<MixTrackVM, AutomationTarget, double>? AutomationValueChanged;

        private void RaiseAutomationValueChanged(AutomationTarget target, double value)
        {
            AutomationValueChanged?.Invoke(this, target, value);
        }


        public bool IsFxPage1 => FxPage == 0;
        public bool IsFxPage2 => FxPage == 1;
        public string FxPageButtonText => FxPage == 0 ? "FX 2/2" : "FX 1/2";
        private static float[] BuildWaveformSamplesFromAnyAudio(string path, int outCount)
        {
            if (outCount < 800) outCount = 800;

            using var afr = new AudioFileReader(path);
            int ch = Math.Max(1, afr.WaveFormat.Channels);

            float[] buf = new float[8192 * ch];

            // PASS 1: count total frames
            long totalFrames = 0;
            while (true)
            {
                int read = afr.Read(buf, 0, buf.Length);
                if (read <= 0) break;
                totalFrames += (read / ch);
            }
            if (totalFrames <= 0) return Array.Empty<float>();

            // rewind
            afr.Position = 0;

            long framesPerBucket = Math.Max(1, totalFrames / outCount);

            float[] result = new float[outCount];

            int p = 0;
            long accFrames = 0;
            float peak = 0f;

            while (p < outCount)
            {
                int read = afr.Read(buf, 0, buf.Length);
                if (read <= 0) break;

                int framesRead = read / ch;
                int idx = 0;

                for (int f = 0; f < framesRead; f++)
                {
                    float best = 0f;
                    for (int c = 0; c < ch; c++)
                    {
                        float a = Math.Abs(buf[idx + c]);
                        if (a > best) best = a;
                    }
                    idx += ch;

                    if (best > peak) peak = best;

                    accFrames++;
                    if (accFrames >= framesPerBucket)
                    {
                        float s = Math.Min(1f, peak);

                        // alternating sign so your line goes above/below mid (looks like real waveform)
                        result[p++] = ((p & 1) == 0) ? s : -s;

                        accFrames = 0;
                        peak = 0f;

                        if (p >= outCount) break;
                    }
                }
            }

            // fill tail (avoid half-empty look)
            for (; p < outCount; p++)
            {
                float s = Math.Min(1f, peak);
                result[p] = ((p & 1) == 0) ? s : -s;
                peak *= 0.98f;
            }

            // calibration (density/height) - keep what you liked
            for (int i = 0; i < result.Length; i++)
                result[i] *= 0.70f;

            return result;
        }




        private string _autoDetectedLabel = "Detected: —";
        public string AutoDetectedLabel
        {
            get => _autoDetectedLabel;
            set
            {
                if (_autoDetectedLabel == value) return;
                _autoDetectedLabel = value;
                OnPropertyChanged();
            }
        }

        public int DetectedKeyPc { get; set; } = 0;               // 0=C ... 11=B (αυτό που ανιχνεύτηκε)
        public ScaleType DetectedScale { get; set; } = ScaleType.Minor;

        public int OverrideKeyPc { get; set; } = -1;             // -1 = no override, αλλιώς 0..11
        private double _fxDelayFeedbackPercent = 28;
        private double _fxChorusModulationPercent = 25;
        private double _fxDistSaturationPercent = 35;

        private double _fxSaturatorDrivePercent = 16;
        private double _fxVocoderTonePercent = 0;
        private double _fxEchoFeedbackPercent = 20;

        public double FxDelayFeedbackPercent
        {
            get => _fxDelayFeedbackPercent;
            set
            {
                if (_fxDelayFeedbackPercent == value) return;
                _fxDelayFeedbackPercent = value;
                OnPropertyChanged(nameof(FxDelayFeedbackPercent));
            }
        }

        private readonly Dictionary<AutomationTarget, AutomationLane> _automationLanes = new();

        public IReadOnlyDictionary<AutomationTarget, AutomationLane> AutomationLanes => _automationLanes;

        public AutomationLane GetOrCreateAutomationLane(AutomationTarget target)
        {
            if (!_automationLanes.TryGetValue(target, out var lane))
            {
                lane = new AutomationLane
                {
                    Target = target,
                    IsEnabled = true
                };

                _automationLanes[target] = lane;
            }

            return lane;
        }

        

        public bool TryGetAutomationLane(AutomationTarget target, out AutomationLane lane)
        {
            return _automationLanes.TryGetValue(target, out lane);
        }

        public void ClearAutomation()
        {
            _automationLanes.Clear();

            DelayMode = 0;
            DelayAlgo = 0;
            FxDelayMixPercent = 0.0;
            FxDelayFeedbackPercent = 0.0;
            FxDelayTonePercent = 50.0;

            ReverbMode = 0;
            FxReverbMixPercent = 0.0;
            FxReverbHoldPercent = 0.0;
            FxReverbDecayPercent = 82.0;
            FxReverbSizePercent = 68.0;
            FxReverbPreDelayMs = 8.0;

            ChorusMode = 0;
            FxChorusMixPercent = 0.0;
            FxChorusWidthPercent = 25.0;
            FxChorusDepthPercent = 0.0;
            FxChorusRate = 0.35;
            FxChorusPhasePercent = 0.0;

            FxReverbHoldPercent = 0.0;
            FxReverbPreDelayMs = 8.0;
            FxReverbDampingPercent = 54.0;
            FxReverbWidthPercent = 72.0;
            FxReverbDiffusePercent = 74.0;
            FxReverbTonePercent = 48.0;

            FxLpFilterBassBoostPercent = 0.0;
            FxLpFilterLfoRateHz = 35.0;
            FxLpFilterLfoAmountPercent = 0.0;
            FxLpFilterLfoSyncEnabled = false;
            FxLpFilterLfoStep = 8;

            FxOctaveFormantPercent = 50.0;
            FxOctaveWidthPercent = 50.0;
            FxOctavePitchSemitones = -12.0;
            FxOctaveLfoRateHz = 20.0;
            FxOctaveLfoAmountPercent = 0.0;
            FxOctaveLfoSyncEnabled = false;
            FxOctaveLfoStep = 8;

            DistMode = 0;
            FxDistMixPercent = 0.0;
            FxDistDrivePercent = 35.0;
            FxDistSaturationPercent = 35.0;
            FxDistTonePercent = 50.0;
            FxDistSpreadPercent = 0.0;

            SaturatorMode = 0;
            FxSaturatorMixPercent = 0.0;
            FxSaturatorDrivePercent = 0.0;
            FxSaturatorTonePercent = 50.0;
            FxSaturatorBiasPercent = 50.0;

            LpFilterMode = 0;
            FxLpFilterMixPercent = 0.0;
            FxLpFilterCutoffPercent = 100.0;
            FxLpFilterResonancePercent = 0.0;
            FxLpFilterBassBoostPercent = 0.0;

            HpFilterMode = 0;
            FxHpFilterMixPercent = 0.0;
            FxHpFilterCutoffPercent = 0.0;
            FxHpFilterResonancePercent = 0.0;
            FxHpFilterBodyBoostPercent = 0.0;
            FxHpFilterLfoRateHz = 35.0;
            FxHpFilterLfoAmountPercent = 0.0;
            FxHpFilterLfoSyncEnabled = false;
            FxHpFilterLfoStep = 8;

            OctaveShiftMode = 0;
            FxOctaveMixPercent = 0.0;
            FxOctaveFormantPercent = 50.0;
            FxOctaveOutputPercent = 100.0;
            FxOctaveWidthPercent = 50.0;
            FxOctavePitchSemitones = -12.0;
            FxOctaveLfoRateHz = 20.0;
            FxOctaveLfoAmountPercent = 0.0;
            FxOctaveLfoSyncEnabled = false;
            FxOctaveLfoStep = 8;
        }

        public double FxDistSaturationPercent
        {
            get => _fxDistSaturationPercent;
            set
            {
                if (_fxDistSaturationPercent == value) return;
                _fxDistSaturationPercent = value;
                OnPropertyChanged(nameof(FxDistSaturationPercent));
            }
        }

        public double FxSaturatorDrivePercent
        {
            get => _fxSaturatorDrivePercent;
            set
            {
                if (_fxSaturatorDrivePercent == value) return;
                _fxSaturatorDrivePercent = value;
                Chain.SaturatorDrive = (float)(_fxSaturatorDrivePercent / 100.0);
                OnPropertyChanged(nameof(FxSaturatorDrivePercent));
            }
        }


        private double _fxDistTonePercent = 50.0;
        public double FxDistTonePercent
        {
            get => _fxDistTonePercent;
            set
            {
                if (Math.Abs(_fxDistTonePercent - value) < 0.001) return;
                _fxDistTonePercent = value;
                OnPropertyChanged(nameof(FxDistTonePercent));
            }
        }

        private double _fxDistSpreadPercent = 0.0;
        public double FxDistSpreadPercent
        {
            get => _fxDistSpreadPercent;
            set
            {
                if (Math.Abs(_fxDistSpreadPercent - value) < 0.001) return;
                _fxDistSpreadPercent = value;
                OnPropertyChanged(nameof(FxDistSpreadPercent));
            }
        }


        private double _fxSaturatorTonePercent = 50.0;
        public double FxSaturatorTonePercent
        {
            get => _fxSaturatorTonePercent;
            set
            {
                if (Math.Abs(_fxSaturatorTonePercent - value) < 0.001) return;
                _fxSaturatorTonePercent = value;
                OnPropertyChanged(nameof(FxSaturatorTonePercent));
            }
        }

        private double _fxSaturatorBiasPercent = 50.0;
        public double FxSaturatorBiasPercent
        {
            get => _fxSaturatorBiasPercent;
            set
            {
                if (Math.Abs(_fxSaturatorBiasPercent - value) < 0.001) return;
                _fxSaturatorBiasPercent = value;
                OnPropertyChanged(nameof(FxSaturatorBiasPercent));
            }
        }



        private static float[] BuildWaveformSamplesFromWavManual(string path, int outCount)
        {
            if (outCount < 800) outCount = 800;

            using var w = new WaveFileReader(path);

            var enc = w.WaveFormat.Encoding;
            if (enc != WaveFormatEncoding.Pcm &&
                enc != WaveFormatEncoding.IeeeFloat &&
                enc != WaveFormatEncoding.Extensible)
                return Array.Empty<float>();

            int ch = Math.Max(1, w.WaveFormat.Channels);
            int bits = w.WaveFormat.BitsPerSample;

            int bytesPerSample = Math.Max(1, bits / 8);
            int frameBytes = bytesPerSample * ch;

            long totalFrames = w.Length / frameBytes;
            if (totalFrames <= 0) return Array.Empty<float>();

            long framesPerBucket = Math.Max(1, totalFrames / outCount);

            float[] result = new float[outCount];
            byte[] buf = new byte[2048 * frameBytes];

            long frameIndex = 0;
            long bucketEnd = framesPerBucket;
            int bucket = 0;

            float peak = 0f;

            while (bucket < outCount)
            {
                int bytesRead = w.Read(buf, 0, buf.Length);
                if (bytesRead <= 0) break;

                int framesRead = bytesRead / frameBytes;
                int bi = 0;

                for (int f = 0; f < framesRead; f++)
                {
                    float best = 0f;

                    for (int c = 0; c < ch; c++)
                    {
                        float s;

                        if (enc == WaveFormatEncoding.IeeeFloat && bits == 32)
                        {
                            s = BitConverter.ToSingle(buf, bi);
                        }
                        else if (bits == 16)
                        {
                            short v = (short)(buf[bi] | (buf[bi + 1] << 8));
                            s = v / 32768f;
                        }
                        else if (bits == 24)
                        {
                            int x = (buf[bi] | (buf[bi + 1] << 8) | (buf[bi + 2] << 16));
                            if ((x & 0x800000) != 0) x |= unchecked((int)0xFF000000);
                            s = x / 8388608f;
                        }
                        else // 32-bit PCM
                        {
                            int v = BitConverter.ToInt32(buf, bi);
                            s = v / 2147483648f;
                        }

                        float a = Math.Abs(s);
                        if (a > best) best = a;

                        bi += bytesPerSample;
                    }

                    if (best > peak) peak = best;

                    frameIndex++;
                    if (frameIndex >= bucketEnd)
                    {
                        float v = Math.Min(1f, peak);
                        // alternating sign => looks like real waveform around midline
                        result[bucket++] = ((bucket & 1) == 0) ? v : -v;

                        peak = 0f;
                        bucketEnd += framesPerBucket;

                        if (bucket >= outCount) break;
                    }
                }
            }

            // fill tail so it never draws “half”
            for (; bucket < outCount; bucket++)
            {
                float v = Math.Min(1f, peak);
                result[bucket] = ((bucket & 1) == 0) ? v : -v;
                peak *= 0.98f;
            }

            // calibration (same as non-wav if you want)
           
            for (int i = 0; i < result.Length; i++)
            {
                float x = result[i];
                // soft clip: tanh
                result[i] = (float)Math.Tanh(x * 1.15f) * 0.65f;
            }
            return result;
        }

        // --- TTS timing + looping count ---
        private double _startSeconds = 0.0;
        public double StartSeconds
        {
            get => _startSeconds;
            set
            {
                double v = Math.Max(0.0, value);
                if (Math.Abs(_startSeconds - v) < 0.000001) return;
                _startSeconds = v;
                RaiseFileChanged();
            }
        }

        // 0 = no loop, 2/4/8 = times total playback
        private int _loopCount = 0;
        public int LoopCount
        {
            get => _loopCount;
            set
            {
                int v = value;
                if (v != 0 && v != 2 && v != 4 && v != 8) v = 0;
                if (_loopCount == v) return;
                _loopCount = v;
                RaiseFileChanged();
            }
        }

        private static float[] BuildWaveformSamplesFromStereoPair(string leftPath, string rightPath, int outCount)
        {
            // Build each side, then combine by max(abs)
            var L = BuildWaveformSamplesFromAnyAudio(leftPath, outCount);
            var R = BuildWaveformSamplesFromAnyAudio(rightPath, outCount);

            int n = Math.Min(L.Length, R.Length);
            if (n <= 0) return Array.Empty<float>();

            var outS = new float[n];
            for (int i = 0; i < n; i++)
            {
                float a = Math.Abs(L[i]);
                float b = Math.Abs(R[i]);
                outS[i] = (a >= b) ? L[i] : R[i];
            }
            return outS;
        }

       

        private bool _loopEnabled = false;
        public bool LoopEnabled
        {
            get => _loopEnabled;
            set
            {
                if (_loopEnabled == value) return;
                _loopEnabled = value;
                RaiseFileChanged();
            }
        }

        private static void SmoothInPlace(float[] a, int radius)
        {
            if (a.Length < 5 || radius <= 0) return;

            float[] tmp = new float[a.Length];
            int n = a.Length;

            for (int i = 0; i < n; i++)
            {
                int i0 = Math.Max(0, i - radius);
                int i1 = Math.Min(n - 1, i + radius);

                float sum = 0f;
                int cnt = 0;
                for (int k = i0; k <= i1; k++)
                {
                    sum += a[k];
                    cnt++;
                }
                tmp[i] = (cnt > 0) ? (sum / cnt) : a[i];
            }

            Array.Copy(tmp, a, n);
        }

        private bool _isSolo;
        public bool IsSolo
        {
            get => _isSolo;
            set { if (_isSolo == value) return; _isSolo = value; OnPropertyChanged(); }
        }

        private bool _isMute;
        public bool IsMute
        {
            get => _isMute;
            set { if (_isMute == value) return; _isMute = value; OnPropertyChanged(); }
        }

        private static Geometry BuildWaveformFromWavManual(string path, int width, int height, int points)
        {
            if (width < 64) width = 64;
            if (height < 24) height = 24;

            // ✅ IMPORTANT: give it enough buckets to show texture (mastering-like)
            // If caller passes small points (e.g. 120), override to something closer to pixel resolution.
            int minPoints = Math.Max(180, width);          // for width=520 -> 520 points
            if (points < minPoints) points = minPoints;

            using var w = new WaveFileReader(path);

            var enc = w.WaveFormat.Encoding;
            if (enc != WaveFormatEncoding.Pcm &&
                enc != WaveFormatEncoding.IeeeFloat &&
                enc != WaveFormatEncoding.Extensible)
                return Geometry.Empty;

            int channels = Math.Max(1, w.WaveFormat.Channels);
            int sr = Math.Max(8000, w.WaveFormat.SampleRate);
            int bits = w.WaveFormat.BitsPerSample;

            bool isFloat = (enc == WaveFormatEncoding.IeeeFloat) || (enc == WaveFormatEncoding.Extensible && bits == 32);
            bool isPcm = (enc == WaveFormatEncoding.Pcm) || (enc == WaveFormatEncoding.Extensible);

            if (isFloat)
            {
                if (bits != 32) return Geometry.Empty;
            }
            else if (isPcm)
            {
                if (bits != 16 && bits != 24 && bits != 32) return Geometry.Empty;
            }
            else return Geometry.Empty;

            long totalFrames = (long)(w.TotalTime.TotalSeconds * sr);
            if (totalFrames <= 0) totalFrames = sr * 5;

            long framesPerBucket = Math.Max(1, totalFrames / points);

            float[] peak = new float[points];
            float[] rms = new float[points];
            float[] ripple = new float[points];

            int bytesPerSample = Math.Max(1, bits / 8);
            int frameBytes = bytesPerSample * channels;

            byte[] buf = new byte[2048 * frameBytes];

            long frameIndex = 0;
            long bucketEnd = framesPerBucket;
            int bucket = 0;

            float p = 0f;
            double sumSq = 0.0;
            long n = 0;

            // ripple window inside bucket (adds "grain")
            int rippleWin = Math.Max(16, (int)(framesPerBucket / 10));
            int rippleCount = 0;
            float ripplePeak = 0f;

            while (bucket < points)
            {
                int bytesRead = w.Read(buf, 0, buf.Length);
                if (bytesRead <= 0) break;

                int framesRead = bytesRead / frameBytes;
                int bi = 0;

                for (int f = 0; f < framesRead; f++)
                {
                    float chosen = 0f;

                    for (int c = 0; c < channels; c++)
                    {
                        float sample;

                        if (isFloat)
                        {
                            sample = BitConverter.ToSingle(buf, bi);
                        }
                        else if (bits == 16)
                        {
                            short s = (short)(buf[bi] | (buf[bi + 1] << 8));
                            sample = s / 32768f;
                        }
                        else if (bits == 24)
                        {
                            int x = (buf[bi] | (buf[bi + 1] << 8) | (buf[bi + 2] << 16));
                            if ((x & 0x800000) != 0) x |= unchecked((int)0xFF000000);
                            sample = x / 8388608f;
                        }
                        else
                        {
                            int s32 = BitConverter.ToInt32(buf, bi);
                            sample = s32 / 2147483648f;
                        }

                        if (Math.Abs(sample) > Math.Abs(chosen))
                            chosen = sample;

                        bi += bytesPerSample;
                    }

                    float a = Math.Abs(chosen);

                    if (a > p) p = a;
                    sumSq += (double)chosen * chosen;
                    n++;

                    if (a > ripplePeak) ripplePeak = a;
                    rippleCount++;
                    if (rippleCount >= rippleWin)
                    {
                        rippleCount = 0;
                        ripplePeak *= 0.88f; // a bit more decay => more visible variation
                    }

                    frameIndex++;
                    if (frameIndex >= bucketEnd)
                    {
                        peak[bucket] = Math.Min(1f, p);
                        rms[bucket] = (n > 0) ? (float)Math.Min(1.0, Math.Sqrt(sumSq / n)) : 0f;
                        ripple[bucket] = Math.Min(1f, ripplePeak);

                        bucket++;
                        if (bucket >= points) break;

                        p = 0f;
                        sumSq = 0.0;
                        n = 0;
                        ripplePeak = 0f;
                        rippleCount = 0;

                        bucketEnd += framesPerBucket;
                    }
                }
            }

            // instead of zeroing the tail -> hold last measured values and fade out gently
            int last = Math.Max(0, bucket - 1);

            float lastPeak = (bucket > 0) ? peak[last] : 0f;
            float lastRms = (bucket > 0) ? rms[last] : 0f;
            float lastRip = (bucket > 0) ? ripple[last] : 0f;

            // small fade so it doesn't look like a hard cut
            for (int i = bucket; i < points; i++)
            {
                double t = (points <= 1) ? 0.0 : (double)(i - bucket) / Math.Max(1, (points - bucket));
                double fade = 1.0 - t;          // linear fade 1..0
                fade = fade * fade;             // smoother (quadratic)

                peak[i] = (float)(lastPeak * fade);
                rms[i] = (float)(lastRms * fade);
                ripple[i] = (float)(lastRip * fade);
            }

            double midY = height / 2.0;
            double half = (height / 2.0) - 2.0;
            double stepX = (double)width / (points - 1);

            // ---- Blend tuned for "mastering" look ----
            const double rmsMix = 0.62;
            const double peakMix = 0.18;
            const double rippleMix = 0.20;

            const double gRms = 0.55;
            const double gPeak = 0.85;
            const double gRip = 0.60;

            const double floor = 0.010;

            // ✅ Auto-zoom: makes flat material show texture
            double sumA = 0;
            double maxA = 0;
            for (int i = 0; i < points; i++)
            {
                double aR0 = Math.Pow(rms[i], gRms);
                double aP0 = Math.Pow(peak[i], gPeak);
                double aT0 = Math.Pow(ripple[i], gRip);
                double a0 = (rmsMix * aR0) + (peakMix * aP0) + (rippleMix * aT0);

                sumA += a0;
                if (a0 > maxA) maxA = a0;
            }
            double avgA = sumA / Math.Max(1, points);
            double dyn = Math.Max(0.0001, maxA - avgA);

            // If very flat -> bigger zoom, else close to 1
            double autoGain = 1.0 / Math.Max(0.22, dyn * 3.0);
            if (autoGain > 7.0) autoGain = 7.0;

            double Amp(int i)
            {
                double aR = Math.Pow(rms[i], gRms);
                double aP = Math.Pow(peak[i], gPeak);
                double aT = Math.Pow(ripple[i], gRip);

                double a = (rmsMix * aR) + (peakMix * aP) + (rippleMix * aT);

                // auto zoom around average
                a = (a - avgA) * autoGain + avgA;

                if (a > 0) a = Math.Max(a, floor);
                if (a > 1) a = 1;
                if (a < 0) a = 0;
                return a;
            }

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(0, midY), isFilled: true, isClosed: true);

                for (int i = 0; i < points; i++)
                {
                    double x = i * stepX;
                    double a = Amp(i);
                    double y = midY - (a * half);
                    ctx.LineTo(new Point(x, y), true, true);
                }

                for (int i = points - 1; i >= 0; i--)
                {
                    double x = i * stepX;
                    double a = Amp(i);
                    double y = midY + (a * half);
                    ctx.LineTo(new Point(x, y), true, true);
                }
            }

            geo.Freeze();
            return geo;
        }
        // ===================== END WAVEFORM =====================

        public ObservableCollection<string> Categories { get; } = new ObservableCollection<string>
        {
            "Kick","Drum Kits","Bass","Synth","FX","Strings","Vocals","Physical Instruments"
        };

        public ObservableCollection<string> Subcategories { get; } = new ObservableCollection<string>();

        private float _detectedBpm = 120f;

        public float DetectedBpm
        {
            get => _detectedBpm;
            set
            {
                float v = value;

                // normalize half/double time
                if (v > 170f) v *= 0.5f;
                if (v < 70f) v *= 2.0f;

                v = Math.Max(40f, Math.Min(220f, v));

                if (Math.Abs(_detectedBpm - v) < 0.01f) return;
                _detectedBpm = v;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BpmText));   // ✅ ΑΥΤΟ ΛΕΙΠΕΙ
            }
        }

        private bool _eqOn = true;
        public bool EqOn
        {
            get => _eqOn;
            set
            {
                if (_eqOn == value) return;
                _eqOn = value;
                Chain.EqEnabled = value;
                OnPropertyChanged();
            }
        }

        private bool _compOn = true;
        public bool CompOn
        {
            get => _compOn;
            set
            {
                if (_compOn == value) return;
                _compOn = value;
                Chain.CompEnabled = value;
                OnPropertyChanged();
            }
        }

        private bool _imagerOn = true;
        public bool ImagerOn
        {
            get => _imagerOn;
            set
            {
                if (_imagerOn == value) return;
                _imagerOn = value;
                Chain.ImagerEnabled = value;
                OnPropertyChanged();
            }
        }

        private bool _limiterOn = true;
        public bool LimiterOn
        {
            get => _limiterOn;
            set
            {
                if (_limiterOn == value) return;
                _limiterOn = value;
                Chain.LimiterEnabled = value;
                OnPropertyChanged();
            }
        }
        public string TypeLabel => $"{Category} {Subcategory}"; public string BpmText => $"{DetectedBpm:0} BPM";
        public float EffectiveBpm => (DetectedBpm >= 40f && DetectedBpm <= 220f) ? DetectedBpm : 120f;
        private string _name = "";
        private string _category = "Kick";
        private string _subcategory = "Main";
        private string _fileMono = "";
        private string _fileLeft = "";
        private string _fileRight = "";

        private int _delayMode = 0;   // 0=Slap, 1=1/8, 2=Dotted 1/4
        private int _reverbMode = 0;  // 0=Room, 1=Plate, 2=Hall
        private int _distMode = 0;    // 0=Warm, 1=Punch, 2=Dirty
                                      // Sync + steps
        private bool _delaySyncEnabled = true;
        private int _delayStep = 8; // 4/8/16/32                        // ===== METER =====
        private double _meterDb = -60.0;        // peak dBFS
        private double _meterFillHeight = 0.0;  // px height for UI

        private double _meterLDb = -60;
        public double MeterLDb
        {
            get => _meterLDb;
            set { if (Math.Abs(_meterLDb - value) < 0.01) return; _meterLDb = value; OnPropertyChanged(); }
        }

        private double _meterRDb = -60;
        public double MeterRDb
        {
            get => _meterRDb;
            set { if (Math.Abs(_meterRDb - value) < 0.01) return; _meterRDb = value; OnPropertyChanged(); }
        }
        public double MeterDb
        {
            get => _meterDb;
            set
            {
                if (Math.Abs(_meterDb - value) < 0.01) return;
                _meterDb = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MeterText));

                // map -60..0 dB => 0..1
                var norm = (_meterDb + 60.0) / 60.0;
                if (norm < 0) norm = 0;
                if (norm > 1) norm = 1;

                // the bar inside has ~160px usable height (ρυθμίζεται)
                MeterFillHeight = norm * 160.0;
            }
        }

        public double MeterFillHeight
        {
            get => _meterFillHeight;
            private set
            {
                if (Math.Abs(_meterFillHeight - value) < 0.5) return;
                _meterFillHeight = value;
                OnPropertyChanged();
            }
        }

        private double _levelLDb = -60.0;
        private double _levelRDb = -60.0;

        public double LevelLDb
        {
            get => _levelLDb;
            set { if (Math.Abs(_levelLDb - value) < 0.01) return; _levelLDb = value; OnPropertyChanged(); OnPropertyChanged(nameof(LevelText)); }
        }
        public double LevelRDb
        {
            get => _levelRDb;
            set { if (Math.Abs(_levelRDb - value) < 0.01) return; _levelRDb = value; OnPropertyChanged(); OnPropertyChanged(nameof(LevelText)); }
        }

        public string LevelText => $"{Math.Max(LevelLDb, LevelRDb):0.0} dB";
        private int _delayModeId = 0; // 0=Slap, 4/8/16/32=Sync step, 101=PingPong, 102=AnalogTape

        private double _eqMixPercent = 100;
        private double _compMixPercent = 100;
        private double _imagerMixPercent = 100;
        private double _limiterMixPercent = 100;

        public double EqMixPercent
        {
            get => _eqMixPercent;
            set
            {
                if (_eqMixPercent == value) return;
                _eqMixPercent = value;
                OnPropertyChanged(nameof(EqMixPercent));
                OnPropertyChanged(nameof(EqMix));
            }
        }

        public double CompMixPercent
        {
            get => _compMixPercent;
            set
            {
                if (_compMixPercent == value) return;
                _compMixPercent = value;
                OnPropertyChanged(nameof(CompMixPercent));
                OnPropertyChanged(nameof(CompMix));
            }
        }

        public double ImagerMixPercent
        {
            get => _imagerMixPercent;
            set
            {
                if (_imagerMixPercent == value) return;
                _imagerMixPercent = value;
                OnPropertyChanged(nameof(ImagerMixPercent));
                OnPropertyChanged(nameof(ImagerMix));
            }
        }

        public double LimiterMixPercent
        {
            get => _limiterMixPercent;
            set
            {
                if (_limiterMixPercent == value) return;
                _limiterMixPercent = value;
                OnPropertyChanged(nameof(LimiterMixPercent));
                OnPropertyChanged(nameof(LimiterMix));
            }
        }

        // 0..1 για audio engine
        public double EqMix => _eqMixPercent / 100.0;
        public double CompMix => _compMixPercent / 100.0;
        public double ImagerMix => _imagerMixPercent / 100.0;
        public double LimiterMix => _limiterMixPercent / 100.0;
        public int DelayMode
        {
            get => _delayMode;
            set
            {
                int v = value;
                if (v < 0) v = 0;
                if (v > 4) v = 4;
                if (_delayMode == v) return;

                _delayMode = v;
                OnPropertyChanged(nameof(DelayMode));
            }
        }
        public string MeterText => $"{MeterDb:0.0} dB";
        private double _pan = 0;
        private double _gainDb = 0;

        private bool _fxDelay, _fxReverb, _fxChorus, _fxDistortion; 
        private int _delayAlgo = 0;            // 0=Classic, 1=PingPong, 2=AnalogTape

        public bool DelaySyncEnabled
        {
            get => _delaySyncEnabled;
            set { if (_delaySyncEnabled == value) return; _delaySyncEnabled = value; OnPropertyChanged(); }
        }

        public int DelayStep
        {
            get => _delayStep;
            set { if (_delayStep == value) return; _delayStep = value; OnPropertyChanged(); }
        }

        public int DelayAlgo
        {
            get => _delayAlgo;
            set { if (_delayAlgo == value) return; _delayAlgo = value; OnPropertyChanged(); }
        }
        public MixingChain Chain { get; } = new MixingChain();

        public MixTrackVM(int index)
        {
            Name = $"TRACK {index:00}";
            SetSubcategoriesFor(_category);
            Subcategory = Subcategories.Count > 0 ? Subcategories[0] : "Main";
            ApplyPresetFromSelection();
            Chain.SaturatorEnabled = false;
            Chain.TimeStretchEnabled = false;
           

            Chain.SaturatorMix = 0f;
            Chain.TimeStretchMix = 0f;
          

            Chain.SaturatorMode = 0;
            Chain.TimeStretchMode = 0;
           
        }

        public void ApplyAutomationAt(double timeSec)
        {
            if (TryGetAutomationLane(AutomationTarget.FxDelayMixPercent, out var delayMixLane) && delayMixLane.IsEnabled)
            {
                double? v = delayMixLane.GetValueAt(timeSec);
                if (v.HasValue) FxDelayMixPercent = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxDelayFeedbackPercent, out var delayFeedbackLane) && delayFeedbackLane.IsEnabled)
            {
                double? v = delayFeedbackLane.GetValueAt(timeSec);
                if (v.HasValue) FxDelayFeedbackPercent = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxDelayTonePercent, out var delayToneLane) && delayToneLane.IsEnabled)
            {
                double? v = delayToneLane.GetValueAt(timeSec);
                if (v.HasValue) FxDelayTonePercent = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxDelaySyncEnabled, out var delaySyncLane) && delaySyncLane.IsEnabled)
            {
                double? v = delaySyncLane.GetValueAt(timeSec);
                if (v.HasValue) FxDelaySyncEnabled = v.Value >= 0.5;
            }

            if (TryGetAutomationLane(AutomationTarget.FxDelayStep, out var delayStepLane) && delayStepLane.IsEnabled)
            {
                double? v = delayStepLane.GetValueAt(timeSec);
                if (v.HasValue) FxDelayStep = (int)Math.Round(v.Value);
            }

            if (TryGetAutomationLane(AutomationTarget.FxReverbMixPercent, out var reverbMixLane) && reverbMixLane.IsEnabled)
            {
                double? v = reverbMixLane.GetValueAt(timeSec);
                if (v.HasValue) FxReverbMixPercent = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxReverbDecayPercent, out var reverbDecayLane) && reverbDecayLane.IsEnabled)
            {
                double? v = reverbDecayLane.GetValueAt(timeSec);
                if (v.HasValue) FxReverbDecayPercent = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxReverbSizePercent, out var reverbSizeLane) && reverbSizeLane.IsEnabled)
            {
                double? v = reverbSizeLane.GetValueAt(timeSec);
                if (v.HasValue) FxReverbSizePercent = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxReverbPreDelayMs, out var reverbPreDelayLane) && reverbPreDelayLane.IsEnabled)
            {
                double? v = reverbPreDelayLane.GetValueAt(timeSec);
                if (v.HasValue) FxReverbPreDelayMs = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxReverbDampingPercent, out var reverbDampingLane) && reverbDampingLane.IsEnabled)
            {
                double? v = reverbDampingLane.GetValueAt(timeSec);
                if (v.HasValue) FxReverbDampingPercent = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxReverbWidthPercent, out var reverbWidthLane) && reverbWidthLane.IsEnabled)
            {
                double? v = reverbWidthLane.GetValueAt(timeSec);
                if (v.HasValue) FxReverbWidthPercent = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxReverbDiffusePercent, out var reverbDiffuseLane) && reverbDiffuseLane.IsEnabled)
            {
                double? v = reverbDiffuseLane.GetValueAt(timeSec);
                if (v.HasValue) FxReverbDiffusePercent = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxReverbTonePercent, out var reverbToneLane) && reverbToneLane.IsEnabled)
            {
                double? v = reverbToneLane.GetValueAt(timeSec);
                if (v.HasValue) FxReverbTonePercent = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxChorusMixPercent, out var chorusMixLane) && chorusMixLane.IsEnabled)
            {
                double? v = chorusMixLane.GetValueAt(timeSec);
                if (v.HasValue) FxChorusMixPercent = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxChorusRate, out var chorusRateLane) && chorusRateLane.IsEnabled)
            {
                double? v = chorusRateLane.GetValueAt(timeSec);
                if (v.HasValue) FxChorusRate = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxChorusDepthPercent, out var chorusDepthLane) && chorusDepthLane.IsEnabled)
            {
                double? v = chorusDepthLane.GetValueAt(timeSec);
                if (v.HasValue) FxChorusDepthPercent = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxChorusPhasePercent, out var chorusPhaseLane) && chorusPhaseLane.IsEnabled)
            {
                double? v = chorusPhaseLane.GetValueAt(timeSec);
                if (v.HasValue) FxChorusPhasePercent = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxDistMixPercent, out var distMixLane) && distMixLane.IsEnabled)
            {
                double? v = distMixLane.GetValueAt(timeSec);
                if (v.HasValue) FxDistMixPercent = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxDistDrivePercent, out var distDriveLane) && distDriveLane.IsEnabled)
            {
                double? v = distDriveLane.GetValueAt(timeSec);
                if (v.HasValue) FxDistDrivePercent = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxDistSaturationPercent, out var distSatLane) && distSatLane.IsEnabled)
            {
                double? v = distSatLane.GetValueAt(timeSec);
                if (v.HasValue) FxDistSaturationPercent = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxDistTonePercent, out var distToneLane) && distToneLane.IsEnabled)
            {
                double? v = distToneLane.GetValueAt(timeSec);
                if (v.HasValue) FxDistTonePercent = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxDistSpreadPercent, out var distSpreadLane) && distSpreadLane.IsEnabled)
            {
                double? v = distSpreadLane.GetValueAt(timeSec);
                if (v.HasValue) FxDistSpreadPercent = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxSaturatorMixPercent, out var satMixLane) && satMixLane.IsEnabled)
            {
                double? v = satMixLane.GetValueAt(timeSec);
                if (v.HasValue) FxSaturatorMixPercent = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxSaturatorDrivePercent, out var satDriveLane) && satDriveLane.IsEnabled)
            {
                double? v = satDriveLane.GetValueAt(timeSec);
                if (v.HasValue) FxSaturatorDrivePercent = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxSaturatorTonePercent, out var satToneLane) && satToneLane.IsEnabled)
            {
                double? v = satToneLane.GetValueAt(timeSec);
                if (v.HasValue) FxSaturatorTonePercent = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxSaturatorBiasPercent, out var satBiasLane) && satBiasLane.IsEnabled)
            {
                double? v = satBiasLane.GetValueAt(timeSec);
                if (v.HasValue) FxSaturatorBiasPercent = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxLpFilterMixPercent, out var lpMixLane) && lpMixLane.IsEnabled)
            {
                double? v = lpMixLane.GetValueAt(timeSec);
                if (v.HasValue) FxLpFilterMixPercent = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxHpFilterMixPercent, out var hpMixLane) && hpMixLane.IsEnabled)
            {
                double? v = hpMixLane.GetValueAt(timeSec);
                if (v.HasValue) FxHpFilterMixPercent = v.Value;
            }

            if (TryGetAutomationLane(AutomationTarget.FxOctaveMixPercent, out var octaveMixLane) && octaveMixLane.IsEnabled)
            {
                double? v = octaveMixLane.GetValueAt(timeSec);
                if (v.HasValue) FxOctaveMixPercent = v.Value;
            }
        }

        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }

        public string Category
        {
            get => _category;
            set
            {
                if (_category == value) return;
                _category = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TypeLabel));
                SetSubcategoriesFor(_category);
                Subcategory = Subcategories.Count > 0 ? Subcategories[0] : "Main";

                ApplyPresetFromSelection(); // ✅ και εδώ
            }
        }

        public string Subcategory
        {
            get => _subcategory;
            set
            {
                if (_subcategory == value) return;
                _subcategory = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TypeLabel));
                ApplyPresetFromSelection(); // ✅ αυτό έλειπε/δεν έτρεχε
            }
        }

        public bool FxDelay
        {
            get => _fxDelay;
            set { if (_fxDelay == value) return; _fxDelay = value; OnPropertyChanged(); }
        }

        public bool FxReverb
        {
            get => _fxReverb;
            set { if (_fxReverb == value) return; _fxReverb = value; OnPropertyChanged(); }
        }

        public bool FxChorus
        {
            get => _fxChorus;
            set { if (_fxChorus == value) return; _fxChorus = value; OnPropertyChanged(); }
        }

        public bool FxDistortion
        {
            get => _fxDistortion;
            set { if (_fxDistortion == value) return; _fxDistortion = value; OnPropertyChanged(); }
        }

        private float GetEqIntensityForCategory(string cat)
        {
            switch (cat)
            {
                case "Kick": return 2.6f;
                case "Drum Kits": return 2.0f;
                case "Bass": return 1.8f;
                case "Synth": return 2.2f;
                case "FX": return 2.4f;
                case "Strings": return 1.6f;
                case "Vocals": return 2.5f;
                case "Physical Instruments": return 1.7f;
                default: return 2.0f;
            }
        }

        private double _fxDelayMixPercent = 0;
        private double _fxReverbMixPercent = 0;
        public double FxReverbMixPercent
        {
            get => _fxReverbMixPercent;
            set
            {
                if (Math.Abs(_fxReverbMixPercent - value) < 0.001) return;
                _fxReverbMixPercent = value;
                OnPropertyChanged(nameof(FxReverbMixPercent));
                RaiseAutomationValueChanged(AutomationTarget.FxReverbMixPercent, value);
            }
        }
        public double FxDelayMixPercent
        {
            get => _fxDelayMixPercent;
            set
            {
                if (Math.Abs(_fxDelayMixPercent - value) < 0.001) return;
                _fxDelayMixPercent = value;
                OnPropertyChanged(nameof(FxDelayMixPercent));
                RaiseAutomationValueChanged(AutomationTarget.FxDelayMixPercent, value);
            }
        }

        private double _fxDistMix = 0;

        public double FxDistMixPercent
        {
            get => _fxDistMix;
            set
            {
                if (Math.Abs(_fxDistMix - value) < 0.001) return;
                _fxDistMix = value;
                OnPropertyChanged(nameof(FxDistMixPercent));
                OnPropertyChanged(nameof(FxDistMix));
                RaiseAutomationValueChanged(AutomationTarget.FxDistMixPercent, value);
            }
        }

        public double FxDistMix => _fxDistMix / 100.0;



        public double FxDelayMix => _fxDelayMixPercent / 100.0;
        public double FxReverbMix => _fxReverbMixPercent / 100.0;
        public double FxChorusMix => _fxChorusMixPercent / 100.0;


        private void ApplyPresetFromSelection()
        {
            var p = MixingPresets.Get(Category, Subcategory);

            // αν θες μπορείς να το κάνεις και ανά category:
            // Chain.EqIntensity = GetEqIntensityForCategory(Category);
            // αλλιώς άστο όπως είναι (έχει default 2.0f)

            Chain.ApplyPreset(p, Category, Subcategory);   // ✅ αυτό δίνει subcategory διαφορές
            OnPropertyChanged(nameof(CategoryDisplay));
        }

        public string CategoryDisplay => string.IsNullOrWhiteSpace(Subcategory) ? Category : $"{Category} • {Subcategory}";

        // ===== presets per selection =====
        

        // ===== MONO/STEREO =====
        public bool IsStereo => !string.IsNullOrWhiteSpace(_fileLeft) && !string.IsNullOrWhiteSpace(_fileRight);

        public string ModeText => IsStereo ? "S" : "M";

        // IMPORTANT: Brush (για binding σε BorderBrush/Foreground)
        public Brush ModeBrush => IsStereo ? Brushes.Red : Brushes.Lime;

        private TimeSpan _trackDuration = TimeSpan.Zero;

        public TimeSpan TrackDuration
        {
            get => _trackDuration;
            set
            {
                if (_trackDuration == value) return;
                _trackDuration = value;
                OnPropertyChanged(nameof(TrackDuration));
                OnPropertyChanged(nameof(TrackDurationText));
            }
        }

        public string TrackDurationText
        {
            get
            {
                if (_trackDuration <= TimeSpan.Zero)
                    return "--:--";

                if (_trackDuration.TotalHours >= 1)
                    return _trackDuration.ToString(@"hh\:mm\:ss");

                return _trackDuration.ToString(@"mm\:ss");
            }
        }

        public string FileMono => _fileMono;
        public string FileLeft => _fileLeft;
        public string FileRight => _fileRight;

        public bool HasAnyFile => !string.IsNullOrWhiteSpace(_fileMono) || IsStereo;

        public string FileDisplay
        {
            get
            {
                if (IsStereo) return $"L: {Path.GetFileName(_fileLeft)} | R: {Path.GetFileName(_fileRight)}";
                if (!string.IsNullOrWhiteSpace(_fileMono)) return $"MONO: {Path.GetFileName(_fileMono)}";
                return "(no file loaded)";
            }
        }

        public void SetMono(string file)
        {
            _fileMono = file ?? "";
            _fileLeft = "";
            _fileRight = "";

            TrackDuration = GetAudioDurationSafe(_fileMono);

            RaiseFileChanged();
        }

        public void SetStereo(string left, string right)
        {
            _fileMono = "";
            _fileLeft = left ?? "";
            _fileRight = right ?? "";

            var leftDur = GetAudioDurationSafe(_fileLeft);
            var rightDur = GetAudioDurationSafe(_fileRight);

            TrackDuration = leftDur >= rightDur ? leftDur : rightDur;

            RaiseFileChanged();
        }

        private void RaiseFileChanged()
        {
            OnPropertyChanged(nameof(FileDisplay));
            OnPropertyChanged(nameof(IsStereo));
            OnPropertyChanged(nameof(ModeText));
            OnPropertyChanged(nameof(ModeBrush));
            OnPropertyChanged(nameof(HasAnyFile));
            OnPropertyChanged(nameof(TrackDuration));
            OnPropertyChanged(nameof(TrackDurationText));
        }

        // ===== PAN / GAIN =====
        public double Pan { get => _pan; set { _pan = value; OnPropertyChanged(); OnPropertyChanged(nameof(PanText)); } }
        public string PanText => $"Pan: {Pan:0}";

        public double GainDb { get => _gainDb; set { _gainDb = value; OnPropertyChanged(); OnPropertyChanged(nameof(GainText)); } }
        public string GainText => $"Gain: {GainDb:0.0} dB";

       

        // ===== FX modes =====
       

        
        public int ReverbMode { get => _reverbMode; set { if (_reverbMode == value) return; _reverbMode = value; OnPropertyChanged(); } }
        public int DistMode { get => _distMode; set { if (_distMode == value) return; _distMode = value; OnPropertyChanged(); } }

        private void SetSubcategoriesFor(string category)
        {
            Subcategories.Clear();
            switch (category)
            {
                case "Kick":
                    Add("Main"); Add("Sub"); Add("808"); Add("Acoustic"); Add("Punchy"); Add("Soft");
                    break;
                case "Drum Kits":
                    Add("Full Kit"); Add("Snare"); Add("Clap"); Add("HiHat"); Add("Tom"); Add("Perc"); Add("Cymbal");
                    break;
                case "Bass":
                    Add("Sub Bass"); Add("Reese"); Add("Acid"); Add("FM"); Add("Electric"); Add("Upright");
                    break;
                case "Synth":
                    Add("Lead"); Add("Pad"); Add("Pluck"); Add("Keys"); Add("Arp"); Add("Seq");
                    break;
                case "FX":
                    Add("Impact"); Add("Riser"); Add("Downlifter"); Add("Sweep"); Add("Hit"); Add("Atmos");
                    break;
                case "Strings":
                    Add("Violin"); Add("Viola"); Add("Cello"); Add("Orchestra"); Add("Staccato"); Add("Legato");
                    break;
                case "Vocals":
                    Add("Lead"); Add("Backing"); Add("Harmony"); Add("Adlibs"); Add("Choir");
                    break;
                case "Physical Instruments":
                    Add("Guitar"); Add("Piano"); Add("Keys"); Add("Brass"); Add("Woodwind"); Add("Percussion");
                    break;
                default:
                    Add("Main");
                    break;
            }
        }

        private double _fxSaturatorMixPercent = 0;
        private double _fxTimeStretchMixPercent = 0;
        private double _fxVocoderMixPercent = 0;
        private double _fxEchoMixPercent = 0;
        public double FxSaturatorMixPercent
        {
            get => _fxSaturatorMixPercent;
            set
            {
                if (Math.Abs(_fxSaturatorMixPercent - value) < 0.001) return;
                _fxSaturatorMixPercent = value;
                OnPropertyChanged(nameof(FxSaturatorMixPercent));
                RaiseAutomationValueChanged(AutomationTarget.FxSaturatorMixPercent, value);
            }
        }

        public double FxTimeStretchMixPercent
        {
            get => _fxTimeStretchMixPercent;
            set
            {
                if (_fxTimeStretchMixPercent == value) return;
                _fxTimeStretchMixPercent = value;
                Chain.TimeStretchMix = (float)(_fxTimeStretchMixPercent / 100.0);
                Chain.TimeStretchEnabled = _fxTimeStretchMixPercent > 0.001;
                OnPropertyChanged(nameof(FxTimeStretchMixPercent));
                OnPropertyChanged(nameof(FxTimeStretchMix));
            }
        }

       

       



        public double FxSaturatorMix => _fxSaturatorMixPercent / 100.0;
        public double FxTimeStretchMix => _fxTimeStretchMixPercent / 100.0;
        public double FxVocoderMix => _fxVocoderMixPercent / 100.0;
        public double FxEchoMix => _fxEchoMixPercent / 100.0;

        private int _saturatorMode = 0;   // 0=TAPE, 1=TUBE, 2=CONSOLE
        private int _timeStretchMode = 0; // 0=SMOOTH, 1=TIGHT, 2=EXTREME
        private int _vocoderMode = 0;     // 0=SOFT, 1=CLASSIC, 2=EXTREME
        private int _echoMode = 0;        // 0=DUB, 1=SPACE, 2=GRAIN

        public int SaturatorMode
        {
            get => _saturatorMode;
            set
            {
                if (_saturatorMode == value) return;
                _saturatorMode = value;
                Chain.SaturatorMode = value;
                OnPropertyChanged();
            }
        }

        public int TimeStretchMode
        {
            get => _timeStretchMode;
            set
            {
                if (_timeStretchMode == value) return;
                _timeStretchMode = value;
                Chain.TimeStretchMode = value;
                OnPropertyChanged();
            }
        }

       
      
        public void ToggleFxPage()
        {
            FxPage = FxPage == 0 ? 1 : 0;
        }

        private bool _fxSaturator;
        private bool _fxTimeStretch;
        private bool _fxVocoder;
        private bool _fxEcho;

        public bool FxSaturator
        {
            get => _fxSaturator;
            set { if (_fxSaturator == value) return; _fxSaturator = value; OnPropertyChanged(); }
        }

        public bool FxTimeStretch
        {
            get => _fxTimeStretch;
            set { if (_fxTimeStretch == value) return; _fxTimeStretch = value; OnPropertyChanged(); }
        }

        public bool FxVocoder
        {
            get => _fxVocoder;
            set { if (_fxVocoder == value) return; _fxVocoder = value; OnPropertyChanged(); }
        }

        public bool FxEcho
        {
            get => _fxEcho;
            set { if (_fxEcho == value) return; _fxEcho = value; OnPropertyChanged(); }
        }

        private void Add(string s) => Subcategories.Add(s);

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

}