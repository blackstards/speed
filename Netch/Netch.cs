﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Win32;
using Windows.Win32.Foundation;
using Microsoft.VisualStudio.Threading;
using Netch.Controllers;
using Netch.Enums;
using Netch.Forms;
using Netch.Services;
using Netch.Utils;
using Serilog;
using Serilog.Events;
using SingleInstance;
#if RELEASE
using Windows.Win32.UI.WindowsAndMessaging;
#endif

namespace Netch
{
    public static class Netch
    {
        public static readonly SingleInstanceService SingleInstance = new($"Global\\{nameof(Netch)}");

        internal static HWND ConsoleHwnd { get; private set; }

        /// <summary>
        ///     应用程序的主入口点
        /// </summary>
        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Contains(Constants.Parameter.ForceUpdate))
                Flags.AlwaysShowNewVersionFound = true;

            // 设置当前目录
            Directory.SetCurrentDirectory(Global.NetchDir);
            var binPath = Path.Combine(Global.NetchDir, "bin");
            Environment.SetEnvironmentVariable("PATH", $"{Environment.GetEnvironmentVariable("PATH")};{binPath}");

            if (!Directory.Exists("bin") || !Directory.EnumerateFileSystemEntries("bin").Any())
            {
                i18N.Load("System");
                //MessageBoxX.Show(i18N.Translate("Please extract all files then run the program!"));
                //File.WriteAllText(@"logging\connect_Status", $"程序缺失00");

                //Environment.Exit(2);
            }

            Updater.CleanOld(Global.NetchDir);

            // 预创建目录
            var directories = new[] { "mode\\Custom", "data", "i18n", "logging" };
            foreach (var item in directories)
                if (!Directory.Exists(item))
                    Directory.CreateDirectory(item);

            // 加载配置
#pragma warning disable VSTHRD002
            Configuration.LoadAsync().Wait();
#pragma warning restore VSTHRD002

            if (!SingleInstance.IsFirstInstance)
            {
                SingleInstance.PassArgumentsToFirstInstance(args.Append(Constants.Parameter.Show));
                Environment.Exit(0);
                return;
            }

            SingleInstance.ArgumentsReceived.Subscribe(SingleInstance_ArgumentsReceived);

            // 清理上一次的日志文件，防止淤积占用磁盘空间
            if (Directory.Exists("logging"))
            {
                var directory = new DirectoryInfo("logging");

                foreach (var file in directory.GetFiles())
                    file.Delete();

                foreach (var dir in directory.GetDirectories())
                    dir.Delete(true);
            }

            InitConsole();

            CreateLogger();

            // 加载语言
            i18N.Load(Global.Settings.Language);

            Task.Run(LogEnvironment).Forget();
            CheckClr();
            CheckOS();

            // 绑定错误捕获
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += Application_OnException;
            Application.ApplicationExit += Application_OnExit;

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(Global.MainForm);
        }

        private static void LogEnvironment()
        {
            //Log.Information("Netch Version: {Version}", $"{UpdateChecker.Owner}/{UpdateChecker.Repo}@{UpdateChecker.Version}");
            Log.Information("=====================如果出现问题,请将此报告发送给客服============== ");
            Log.Information("GGFox Version:V1.0.0 A114514");
            Log.Information("OS: {OSVersion}", Environment.OSVersion);
            Log.Information("SHA256: {Hash}", $"{Utils.Utils.SHA256CheckSum(Global.NetchExecutable)}");
            Log.Information("System Language: {Language}", CultureInfo.CurrentCulture.Name);

            if (Log.IsEnabled(LogEventLevel.Debug))
            {
                // TODO log level setting
                Task.Run(() => Log.Debug("Third-party Drivers:\n{Drivers}", string.Join(Constants.EOF, SystemInfo.SystemDrivers(false)))).Forget();
                Task.Run(() => Log.Debug("Running Processes: \n{Processes}", string.Join(Constants.EOF, SystemInfo.Processes(false)))).Forget();
            }
        }

        private static void CheckClr()
        {
            var framework = Assembly.GetExecutingAssembly().GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
            if (framework == null)
            {
                Log.Warning("TargetFrameworkAttribute null");
                return;
            }

            var frameworkName = new FrameworkName(framework);

            if (frameworkName.Version.Major != Environment.Version.Major)
            {
                Log.Information("CLR: {Version}", Environment.Version);
                Flags.NoSupport = true;
                if (!Global.Settings.NoSupportDialog)
                    MessageBoxX.Show(
                        i18N.TranslateFormat("{0} won't get developers' support, Please do not report any issues or seek help from developers.",
                            "CLR " + Environment.Version),
                        LogLevel.WARNING);
            }
        }

        private static void CheckOS()
        {
            if (Environment.OSVersion.Version.Build < 17763)
            {
                Flags.NoSupport = true;
                if (!Global.Settings.NoSupportDialog)
                    MessageBoxX.Show(
                        i18N.TranslateFormat("{0} won't get developers' support, Please do not report any issues or seek help from developers.",
                            Environment.OSVersion),
                        LogLevel.WARNING);
            }
        }

        private static void InitConsole()
        {
            PInvoke.AllocConsole();

            ConsoleHwnd = PInvoke.GetConsoleWindow();
#if RELEASE
            PInvoke.ShowWindow(ConsoleHwnd, SHOW_WINDOW_CMD.SW_HIDE);
#endif
        }

        public static void CreateLogger()
        {
            Log.Logger = new LoggerConfiguration()
#if DEBUG
                .MinimumLevel.Verbose()
#else
                .MinimumLevel.Debug()
#endif
                .WriteTo.Async(c => c.File(Path.Combine(Global.NetchDir, Constants.LogFile),
                    outputTemplate: Constants.OutputTemplate,
                    rollOnFileSizeLimit: false))
                .WriteTo.Console(outputTemplate: Constants.OutputTemplate)
                .MinimumLevel.Override(@"Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .CreateLogger();
        }

        private static void Application_OnException(object sender, ThreadExceptionEventArgs e)
        {
            Log.Error(e.Exception, "Unhandled error");
        }

        private static void Application_OnExit(object? sender, EventArgs eventArgs)
        {
            Log.CloseAndFlush();
        }

        private static void SingleInstance_ArgumentsReceived(IEnumerable<string> args)
        {
            if (args.Contains(Constants.Parameter.Show))
            {
                Global.MainForm.ShowMainFormToolStripButton_Click(null!, null!);
            }
        }
    }
}