using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Sobre.Models;
using Sobre.Services;
using Sobre.ViewModels;
using Sobre.Views;

namespace Sobre
{
    public partial class App : Application
    {
        private static Mutex? _singleInstance;
        private ShellViewModel? _vm;

        public ShellViewModel Shell => _vm ??= new ShellViewModel();

        protected override void OnStartup(StartupEventArgs e)
        {
            // Always log startup — even if the UI crashes, sobre.log survives.
            try { Log.Info($"Sobre starting {Assembly.GetExecutingAssembly().GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? ""} exe={Environment.ProcessPath} args={string.Join(" ", e.Args)}"); } catch { }

            // Global handlers BEFORE anything that can throw
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += (s, ev) => { try { Log.Error("UnobservedTaskException", ev.Exception); } catch { } ev.SetObserved(); };

            // Installer mode — if the exe name contains "setup" or "installer", offer to install.
            try
            {
                if (IsInstallerMode(e.Args))
                {
                    if (RunInstaller()) { Shutdown(); return; }
                    // If user cancels, fall through to normal launch.
                }
            }
            catch (Exception ex) { try { Log.Error("IsInstallerMode", ex); } catch { } }

            // Single instance — a second copy would fight over the same credential cache.
            try
            {
                _singleInstance = new Mutex(true, @"Local\Sobre.Mail.SingleInstance", out bool createdNew);
                if (!createdNew)
                {
                    MessageBox.Show(
                        "Sobre is already running. Look for it on the taskbar.",
                        "Sobre", MessageBoxButton.OK, MessageBoxImage.Information);
                    Shutdown();
                    return;
                }
            }
            catch (Exception ex) { try { Log.Error("SingleInstance", ex); } catch { } }

            base.OnStartup(e);

            // Keep app alive explicitly until window is closed — prevents OnMainWindowClose race after an early exception
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try
            {
                ThemeService.Current.Start();
                try { Log.Info("ThemeService started, IsDark=" + ThemeService.Current.IsDark); } catch { }

                var window = new MainWindow { DataContext = Shell };
                MainWindow = window;
                window.Closed += (_, _) => Shutdown();
                window.Show();
                try { Log.Info("MainWindow shown"); } catch { }
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                MessageBox.Show(
                    "Sobre could not finish starting.\n\n" + Describe(ex) +
                    "\n\nA log was written to:\n" + LogPath,
                    "Sobre", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogCrash(e.Exception);
            try { Log.Error("DispatcherUnhandled: " + Describe(e.Exception), e.Exception); } catch { }
            MessageBox.Show(
                "Sobre hit an unexpected problem and kept running.\n\n" +
                Describe(e.Exception) + "\n\nA log was written to:\n" + LogPath,
                "Sobre", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }

        /// <summary>Flattens the inner-exception chain — the outer message alone is usually useless.</summary>
        private static string Describe(Exception ex)
        {
            var text = ex.GetType().Name + ": " + ex.Message;
            var inner = ex.InnerException;
            int depth = 0;
            while (inner != null && depth++ < 3)
            {
                if (!string.Equals(inner.Message, ex.Message, StringComparison.Ordinal))
                    text += "\n→ " + inner.GetType().Name + ": " + inner.Message;
                inner = inner.InnerException;
            }
            if (ex.StackTrace != null) text += "\n\n" + ex.StackTrace;
            return text;
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                if (e.ExceptionObject is Exception ex) LogCrash(ex);
                else Log.Info("DomainUnhandled: " + e.ExceptionObject?.ToString());
            }
            catch { }
            // Don't shutdown — let the dispatcher handler decide
        }

        private static string LogPath => Path.Combine(Store.DataDirectory, "sobre.log");

        private static void LogCrash(Exception ex) => Log.Error(ex);

        protected override void OnExit(ExitEventArgs e)
        {
            try { Log.Info($"Sobre exiting code={e.ApplicationExitCode}"); } catch { }
            try { _vm?.PersistAccounts(); } catch (Exception ex) { try { Log.Error("PersistAccounts on exit", ex); } catch { } }
            try { ThemeService.Current.Dispose(); } catch { }
            try { _singleInstance?.Dispose(); } catch { }
            base.OnExit(e);
        }

        private static bool IsInstallerMode(string[] args)
        {
            try
            {
                var exe = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];
                var name = Path.GetFileNameWithoutExtension(exe) ?? "";
                if (name.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("installer", StringComparison.OrdinalIgnoreCase))
                    return true;
                foreach (var a in args)
                    if (a.Equals("--install", StringComparison.OrdinalIgnoreCase) ||
                        a.Equals("/install", StringComparison.OrdinalIgnoreCase) ||
                        a.Equals("--setup", StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch { }
            return false;
        }

        private static bool RunInstaller()
        {
            try
            {
                var result = MessageBox.Show(
                    "Install Sobre for the current user?\n\n" +
                    "This will copy Sobre to:\n%LOCALAPPDATA%\\Programs\\Sobre\\Sobre.exe\n" +
                    "and create a Start Menu shortcut.\n\nNo admin rights needed. You can uninstall by deleting that folder.",
                    "Sobre — Installer",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return false;

                var source = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(source) || !File.Exists(source))
                {
                    MessageBox.Show("Could not locate the installer file.", "Sobre", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                var targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Sobre");
                Directory.CreateDirectory(targetDir);
                var target = Path.Combine(targetDir, "Sobre.exe");

                var isSelf = string.Equals(Path.GetFullPath(source).TrimEnd('\\'), Path.GetFullPath(target).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                if (!isSelf)
                {
                    File.Copy(source, target, true);
                }

                try
                {
                    var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                    var linkPath = Path.Combine(startMenu, "Sobre.lnk");
                    var shellType = Type.GetTypeFromProgID("WScript.Shell");
                    if (shellType != null)
                    {
                        dynamic shell = Activator.CreateInstance(shellType)!;
                        dynamic link = shell.CreateShortcut(linkPath);
                        link.TargetPath = target;
                        link.WorkingDirectory = targetDir;
                        link.Description = "Sobre — Ad-free mail for Windows";
                        link.IconLocation = target;
                        link.Save();
                    }
                }
                catch { }

                var launch = MessageBox.Show(
                    "Sobre was installed to:\n" + target + "\n\nLaunch it now?",
                    "Sobre", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (launch == MessageBoxResult.Yes)
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Installer failed:\n" + ex.Message, "Sobre", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
    }
}
