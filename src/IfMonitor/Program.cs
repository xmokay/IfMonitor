namespace IfMonitor;

static class Program
{
    private const string MutexName = "Local\\IfMonitor_SingleInstance";

    [STAThread]
    static void Main()
    {
        AppNotificationIdentity.Register();

        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "IfMonitor is already running.",
                "IfMonitor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainApplicationContext());
    }
}
