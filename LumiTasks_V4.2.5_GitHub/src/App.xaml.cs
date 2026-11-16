using System.Windows;
namespace LumiTasks.VisualPrototype;
public partial class App : System.Windows.Application
{
    // A second auto-launched instance could overwrite the same task data while
    // the first instance is timing. Keep a per-session single-instance guard.
    private System.Threading.Mutex? _instanceMutex;
    private bool _ownsMutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new System.Threading.Mutex(true, @"Local\LumiTasks_WPF_Main", out var createdNew);
        if (!createdNew)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }
        _ownsMutex = true;
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumiTasksWPFPreview", "logs");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "error.log"), $"{DateTime.Now:s} {args.Exception}\n");
            }
            catch { }
            System.Windows.MessageBox.Show("LumiTasks 遇到错误，日志位于 LumiTasksWPFPreview\\logs。\n" + args.Exception.Message, "LumiTasks 诊断", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
