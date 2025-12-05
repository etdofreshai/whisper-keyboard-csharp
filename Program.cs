namespace WhisperKeyboard;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        // Ensure single instance
        using var mutex = new Mutex(true, "WhisperKeyboard_SingleInstance", out bool createdNew);

        if (!createdNew)
        {
            MessageBox.Show("Whisper Keyboard is already running!", "Whisper Keyboard",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
