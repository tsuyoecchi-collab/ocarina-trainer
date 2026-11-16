using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhykroTrainer.Core;

public static class ProcessNames
{
    /// <summary>Compara nombres de proceso ignorando mayúsculas y la extensión .exe.</summary>
    public static bool Match(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        return string.Equals(Path.GetFileNameWithoutExtension(a.Trim()),
            Path.GetFileNameWithoutExtension(b.Trim()), StringComparison.OrdinalIgnoreCase);
    }
}

public static class Paths
{    public static string DataDir { get; } = ResolveDataDir();
    public static string ConfigPath => Path.Combine(DataDir, "trainer-config.json");
    public static string LogPath => Path.Combine(DataDir, "trainer.log");

    private static string ResolveDataDir()
    {
        string exeDir = AppContext.BaseDirectory;
        try
        {
            string probe = Path.Combine(exeDir, ".escritura-ok");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return exeDir;
        }
        catch
        {
            string fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhykroTrainer");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }
}

public static class Log
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Paths.DataDir);
                File.AppendAllText(Paths.LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Un log que falla no debe tumbar el trainer.
        }
    }

    public static void Error(string context, Exception exception) =>
        Write($"ERROR ({context}): {exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception.StackTrace}");

    public static string Tail(int lines = 30)
    {
        try
        {
            if (!File.Exists(Paths.LogPath)) return "(sin registro)";
            string[] all = File.ReadAllLines(Paths.LogPath);
            return string.Join(Environment.NewLine, all.TakeLast(Math.Max(1, lines)));
        }
        catch
        {
            return "(no se pudo leer el registro)";
        }
    }
}

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string PathFile => Paths.ConfigPath;

    public static ConfigFile Load(string? path = null)
    {
        path ??= Paths.ConfigPath;
        try
        {
            if (!File.Exists(path))
            {
                var fresh = new ConfigFile();
                fresh.Normalize();
                return fresh;
            }

            string json = File.ReadAllText(path);
            ConfigFile? config = JsonSerializer.Deserialize<ConfigFile>(json, Options);
            if (config is null) throw new JsonException("El archivo está vacío.");

            config.Normalize();
            return config;
        }
        catch (Exception ex)
        {
            Log.Error("cargar configuración", ex);
            try
            {
                string backup = path + ".roto-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                if (File.Exists(path)) File.Copy(path, backup, true);
            }
            catch
            {
                // ignorar
            }

            var fresh = new ConfigFile();
            fresh.Normalize();
            return fresh;
        }
    }

    public static bool Save(ConfigFile config, string? path = null)
    {
        path ??= Paths.ConfigPath;
        try
        {
            config.Normalize();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(config, Options));
            File.Move(temp, path, true);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("guardar configuración", ex);
            return false;
        }
    }
}
