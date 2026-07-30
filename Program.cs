using System;
using System.Threading;
using System.Windows.Forms;

namespace UselessChecker;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        // Глобальная страховка: любой необработанный краш пишется в лог, а не убивает процесс молча.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) => Logger.Error("UI ThreadException", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            Logger.Error("AppDomain UnhandledException", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));

        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
    }
}