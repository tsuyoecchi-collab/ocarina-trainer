using System.Buffers.Binary;
using System.Diagnostics;
using PhykroTrainer.Core;
using PhykroTrainer.UI;

namespace PhykroTrainer;

internal static class Program
{
    private const string MutexName = @"Local\Phykro.Trainer";

    [STAThread]
    private static int Main(string[] args)
    {
        if (HasArg(args, "--selftest")) return SelfTest.Run();
        if (HasArg(args, "--probe")) return SelfTest.Probe(args);

        bool elevated = HasArg(args, "--elevated");

        using var mutex = new Mutex(false, MutexName);
        bool owned = false;
        if (!elevated)
        {
            try
            {
                owned = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                owned = true;
            }

            if (!owned)
            {
                MessageBox.Show("Ya hay un Phykro Trainer abierto.\n\nCierra la otra ventana antes de abrir otra.",
                    "Phykro Trainer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
        }

        try
        {
            ApplicationConfiguration.Initialize();
            Application.ThreadException += (_, e) => ShowCrash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("no controlado",
                e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Log.Error("tarea no observada", e.Exception);
                e.SetObserved();
            };

            Application.Run(new MainForm());
            return 0;
        }
        catch (Exception ex)
        {
            ShowCrash(ex);
            return 1;
        }
        finally
        {
            if (owned)
            {
                try { mutex.ReleaseMutex(); } catch { /* ignorar */ }
            }
        }
    }

    private static bool HasArg(string[] args, string value) =>
        args.Any(a => a.Equals(value, StringComparison.OrdinalIgnoreCase));

    private static void ShowCrash(Exception exception)
    {
        Log.Error("aplicación", exception);
        MessageBox.Show(
            "El trainer tuvo un problema:\n\n" + exception.Message +
            "\n\nEl detalle quedó en:\n" + Paths.LogPath +
            "\n\n" + Log.Tail(12),
            "Phykro Trainer", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
