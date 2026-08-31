using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace FightstickLab
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // 全局异常兜底：记录到日志并不让程序崩溃退出
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        }

        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogCrash(e.Exception);
            e.Handled = true; // 尽量让界面继续运行
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            LogCrash(e.ExceptionObject as Exception);
        }

        private static void LogCrash(Exception? ex)
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "crash.log");
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
            }
            catch { /* 忽略日志失败 */ }
        }
    }
}
