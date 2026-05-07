namespace HVTTool;

static class Program
{
#if HVTTOOL_GUI
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string? initialPath = args.Length > 0 ? args[0] : null;
        Application.Run(new MainForm(initialPath));
        return 0;
    }
#else
    static int Main(string[] args) => Cli.Run(args);
#endif
}
