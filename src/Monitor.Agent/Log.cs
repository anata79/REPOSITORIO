namespace Monitor.Agent;

/// <summary>Registro de funcionamento em %LOCALAPPDATA%\MonitorPortoconseg\agente.log (até ~1 MB).</summary>
internal static class Log
{
    private static readonly object Sync = new();

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonitorPortoconseg");

    private static string FilePath => Path.Combine(Folder, "agente.log");

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Folder);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 1_000_000)
                {
                    var old = Path.Combine(Folder, "agente.old.log");
                    File.Copy(FilePath, old, overwrite: true);
                    File.Delete(FilePath);
                }
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Registro é só para diagnóstico; nunca interrompe o programa.
        }
    }
}
