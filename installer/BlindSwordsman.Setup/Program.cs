namespace BlindSwordsman.Setup;

internal static class Program
{
    [STAThread]
    private static void Main(string[] arguments)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            var options = SetupCommandLineOptions.Parse(arguments);
            using var context = new SetupApplicationContext(options);
            Application.Run(context);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Blind Swordsman setup could not start.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Blind Swordsman Setup Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1);
        }
    }
}
