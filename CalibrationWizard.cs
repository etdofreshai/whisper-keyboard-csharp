using NAudio.Wave;

namespace WhisperKeyboard;

public class CalibrationWizard : Form
{
    private readonly Config _config;
    private WaveInEvent? _waveIn;
    private readonly List<double> _samples = new();

    private Label _instructionLabel = null!;
    private Label _statusLabel = null!;
    private ProgressBar _progressBar = null!;
    private Panel _volumeMeter = null!;
    private Label _volumeLabel = null!;
    private Button _nextButton = null!;
    private Button _cancelButton = null!;

    private int _currentStep;
    private double _ambientLevel;
    private double _voiceLevel;
    private double _currentVolume;
    private System.Windows.Forms.Timer _updateTimer = null!;
    private System.Windows.Forms.Timer _sampleTimer = null!;
    private int _sampleCount;
    private const int SamplesNeeded = 50; // ~2.5 seconds at 50ms intervals

    public int CalibratedThreshold { get; private set; }
    public bool CalibrationSuccessful { get; private set; }

    public CalibrationWizard(Config config)
    {
        _config = config;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "Audio Calibration Wizard";
        Size = new Size(500, 380);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(45, 45, 48);

        // Instruction label
        _instructionLabel = new Label
        {
            Text = "Welcome to the Audio Calibration Wizard!\n\n" +
                   "This wizard will help you set the optimal voice detection\n" +
                   "threshold for your microphone in two quick steps:\n\n" +
                   "  1. Measure your ambient (background) noise\n" +
                   "  2. Measure your speaking voice level\n\n" +
                   "Click 'Next' when you're ready to begin.",
            Location = new Point(20, 20),
            Size = new Size(440, 140),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11)
        };
        Controls.Add(_instructionLabel);

        // Volume meter background
        var volumeMeterBg = new Panel
        {
            Location = new Point(20, 170),
            Size = new Size(440, 30),
            BackColor = Color.FromArgb(30, 30, 30)
        };
        Controls.Add(volumeMeterBg);

        // Volume meter fill
        _volumeMeter = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(0, 30),
            BackColor = Color.Green
        };
        volumeMeterBg.Controls.Add(_volumeMeter);

        // Volume label
        _volumeLabel = new Label
        {
            Text = "Volume: --",
            Location = new Point(20, 205),
            Size = new Size(200, 25),
            ForeColor = Color.LightGray,
            Font = new Font("Segoe UI", 9)
        };
        Controls.Add(_volumeLabel);

        // Progress bar
        _progressBar = new ProgressBar
        {
            Location = new Point(20, 235),
            Size = new Size(440, 25),
            Style = ProgressBarStyle.Continuous,
            Visible = false
        };
        Controls.Add(_progressBar);

        // Status label
        _statusLabel = new Label
        {
            Text = "",
            Location = new Point(20, 265),
            Size = new Size(440, 50),
            ForeColor = Color.LightBlue,
            Font = new Font("Segoe UI", 10)
        };
        Controls.Add(_statusLabel);

        // Next button
        _nextButton = new Button
        {
            Text = "Next",
            Location = new Point(280, 300),
            Size = new Size(100, 35),
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _nextButton.Click += NextButton_Click;
        Controls.Add(_nextButton);

        // Cancel button
        _cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(390, 300),
            Size = new Size(80, 35),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _cancelButton.Click += (s, e) => Close();
        Controls.Add(_cancelButton);

        // Update timer for volume meter
        _updateTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _updateTimer.Tick += UpdateTimer_Tick;

        // Sample timer for collecting samples
        _sampleTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _sampleTimer.Tick += SampleTimer_Tick;

        FormClosing += CalibrationWizard_FormClosing;
    }

    private void NextButton_Click(object? sender, EventArgs e)
    {
        switch (_currentStep)
        {
            case 0: // Welcome -> Preview Step 1
                ShowStep1Preview();
                break;
            case 1: // Preview Step 1 -> Measure Ambient
                StartStep1Measurement();
                break;
            case 2: // Ambient Done -> Preview Step 2
                ShowStep2Preview();
                break;
            case 3: // Preview Step 2 -> Measure Voice
                StartStep2Measurement();
                break;
            case 4: // Voice Done -> Show Results
                ShowResults();
                break;
            case 5: // Results -> Apply
                ApplyAndClose();
                break;
        }
    }

    private void ShowStep1Preview()
    {
        _currentStep = 1;
        _instructionLabel.Text = "Step 1: Measure Ambient Noise\n\n" +
                                  "On the next screen, please remain QUIET and still.\n" +
                                  "We'll measure your background noise level for about\n" +
                                  "3 seconds.\n\n" +
                                  "The volume meter below shows your current audio level.\n" +
                                  "Click 'Start' when you're ready.";
        _nextButton.Text = "Start";
        _statusLabel.Text = "Get ready to stay quiet...";
        _statusLabel.ForeColor = Color.LightBlue;
        _progressBar.Visible = false;

        // Start audio capture to show volume meter
        StartAudioCapture();
    }

    private void StartStep1Measurement()
    {
        _currentStep = 2;
        _instructionLabel.Text = "Step 1: Measuring Ambient Noise...\n\n" +
                                  "Please remain QUIET.\n" +
                                  "Do not speak or make any sounds.";
        _nextButton.Enabled = false;
        _nextButton.Text = "Measuring...";
        _progressBar.Visible = true;
        _progressBar.Value = 0;
        _statusLabel.Text = "Listening to ambient noise...";

        _samples.Clear();
        _sampleCount = 0;
        _sampleTimer.Start();
    }

    private void ShowStep2Preview()
    {
        _currentStep = 3;
        _ambientLevel = _samples.Count > 0 ? _samples.Average() : 50;

        _instructionLabel.Text = "Step 2: Measure Voice Level\n\n" +
                                  "On the next screen, please SPEAK NORMALLY.\n" +
                                  "Say something like: \"Testing one two three\"\n" +
                                  "or count from one to ten.\n\n" +
                                  "Click 'Start' when you're ready to speak.";
        _nextButton.Text = "Start";
        _nextButton.Enabled = true;
        _progressBar.Visible = false;
        _statusLabel.Text = $"Ambient level measured: {_ambientLevel:F0}\nGet ready to speak...";
        _statusLabel.ForeColor = Color.LightGreen;
    }

    private void StartStep2Measurement()
    {
        _currentStep = 4;
        _instructionLabel.Text = "Step 2: Measuring Voice Level...\n\n" +
                                  "Please SPEAK NORMALLY now.\n" +
                                  "Say: \"Testing one two three four five\"";
        _nextButton.Enabled = false;
        _nextButton.Text = "Measuring...";
        _progressBar.Visible = true;
        _progressBar.Value = 0;
        _statusLabel.Text = "Speak into your microphone...";
        _statusLabel.ForeColor = Color.LightBlue;

        _samples.Clear();
        _sampleCount = 0;
        _sampleTimer.Start();
    }

    private void ShowResults()
    {
        _currentStep = 5;
        StopAudioCapture();

        _voiceLevel = _samples.Count > 0 ? _samples.Max() : _ambientLevel * 3;

        // Calculate threshold: between ambient and voice, closer to ambient
        // Use 30% of the way from ambient to voice
        double threshold = _ambientLevel + (_voiceLevel - _ambientLevel) * 0.3;

        // Ensure minimum separation
        if (threshold < _ambientLevel * 1.5)
        {
            threshold = _ambientLevel * 1.5;
        }

        CalibratedThreshold = (int)Math.Max(10, Math.Min(5000, threshold));

        _instructionLabel.Text = "Calibration Complete!\n\n" +
            $"  Ambient noise level:    {_ambientLevel:F0}\n" +
            $"  Voice level:            {_voiceLevel:F0}\n" +
            $"  Recommended threshold:  {CalibratedThreshold}\n\n" +
            "Click 'Apply' to save these settings.";

        _statusLabel.Text = "Calibration successful!";
        _statusLabel.ForeColor = Color.LightGreen;
        _progressBar.Visible = false;
        _nextButton.Text = "Apply";
        _nextButton.Enabled = true;
    }

    private void ApplyAndClose()
    {
        CalibrationSuccessful = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void SampleTimer_Tick(object? sender, EventArgs e)
    {
        if (_currentVolume > 0)
        {
            _samples.Add(_currentVolume);
        }

        _sampleCount++;
        _progressBar.Value = Math.Min(100, (_sampleCount * 100) / SamplesNeeded);

        if (_sampleCount >= SamplesNeeded)
        {
            _sampleTimer.Stop();

            if (_currentStep == 2)
            {
                // Finished measuring ambient - show step 2 preview
                _nextButton.Enabled = true;
                _nextButton.Text = "Next";
                _statusLabel.Text = $"Ambient level: {(_samples.Count > 0 ? _samples.Average() : 0):F0}\nClick Next to continue.";
                _statusLabel.ForeColor = Color.LightGreen;
            }
            else if (_currentStep == 4)
            {
                // Finished measuring voice - show results
                _nextButton.Enabled = true;
                _nextButton.Text = "Next";
                _statusLabel.Text = $"Voice level: {(_samples.Count > 0 ? _samples.Max() : 0):F0}\nClick Next to see results.";
                _statusLabel.ForeColor = Color.LightGreen;
            }
        }
    }

    private void StartAudioCapture()
    {
        if (_waveIn != null) return; // Already capturing

        try
        {
            _waveIn = new WaveInEvent
            {
                DeviceNumber = _config.DeviceIndex >= 0 ? _config.DeviceIndex : 0,
                WaveFormat = new WaveFormat(_config.SampleRate, 16, _config.Channels),
                BufferMilliseconds = 50
            };

            _waveIn.DataAvailable += WaveIn_DataAvailable;
            _waveIn.StartRecording();
            _updateTimer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start audio capture: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private void StopAudioCapture()
    {
        _updateTimer.Stop();
        _sampleTimer.Stop();
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;
    }

    private void WaveIn_DataAvailable(object? sender, WaveInEventArgs e)
    {
        double sum = 0;
        int sampleCount = e.BytesRecorded / 2;

        for (int i = 0; i < e.BytesRecorded; i += 2)
        {
            short sample = BitConverter.ToInt16(e.Buffer, i);
            sum += sample * sample;
        }

        double rms = Math.Sqrt(sum / sampleCount);
        _currentVolume = rms; // This matches the AudioProcessor calculation
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        // Update volume meter
        int meterWidth = (int)Math.Min(440, (_currentVolume / 1000) * 440);
        _volumeMeter.Width = meterWidth;

        // Color based on level
        if (_currentVolume < 100)
            _volumeMeter.BackColor = Color.Green;
        else if (_currentVolume < 500)
            _volumeMeter.BackColor = Color.Yellow;
        else
            _volumeMeter.BackColor = Color.Red;

        _volumeLabel.Text = $"Volume: {_currentVolume:F0}";
    }

    private void CalibrationWizard_FormClosing(object? sender, FormClosingEventArgs e)
    {
        StopAudioCapture();
        _updateTimer.Dispose();
        _sampleTimer.Dispose();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Don't start audio capture on welcome screen - wait until step 1 preview
    }
}
