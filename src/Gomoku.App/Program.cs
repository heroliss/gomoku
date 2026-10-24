namespace Liss.Gomoku.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => Report(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception error) SessionFiles.Log(error);
        };
        Application.Run(new MainForm(args.Contains("--showcase"), args));
    }

    private static void Report(Exception error)
    {
        SessionFiles.Log(error);
        MessageBox.Show("操作未完成，错误已记录。当前对局会尽量保留；你可以保存棋谱后重新打开程序。", "Gomoku v3", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
