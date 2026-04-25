using DevPulse.App;
using DevPulse.Infrastructure.Persistence;
using Serilog;

Application.SetHighDpiMode(HighDpiMode.SystemAware);
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);

var logPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "DevPulse", "logs", "devpulse-.log");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
    .CreateLogger();

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Log.Error(e.Exception, "Unobserved task exception");
    e.SetObserved();
};

try
{
    var store = new SqliteStateStore(DbSchema.DbPath);
    await store.InitializeAsync();

    Application.Run(new TrayApplicationContext(store));
}
catch (Exception ex)
{
    Log.Fatal(ex, "Unhandled startup exception");
    MessageBox.Show($"DevPulse failed to start:\n{ex.Message}", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
finally
{
    await Log.CloseAndFlushAsync();
}
