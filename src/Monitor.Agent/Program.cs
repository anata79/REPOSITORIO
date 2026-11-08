namespace Monitor.Agent;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // Uma única cópia por sessão de usuário. A tarefa agendada tenta abrir de novo a cada
        // 5 minutos; se já estiver rodando, esta cópia sai sem fazer nada.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\MonitorPortoconseg", out var isFirst);
        if (!isFirst) return 0;

        var settings = AgentSettings.Load();
        if (settings is null)
        {
            Log.Write($"Configuração não encontrada em {AgentSettings.Folder}. Rode o instalador como administrador.");
            return 1;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) => Log.Write($"Erro: {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"Erro fatal: {e.ExceptionObject}");

        using var worker = new AgentWorker(settings);
        Application.Run(new TrayContext(worker));
        return 0;
    }
}
