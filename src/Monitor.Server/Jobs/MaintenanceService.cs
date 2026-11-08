using Monitor.Server.Data;

namespace Monitor.Server.Jobs;

/// <summary>
/// Uma vez por dia: apaga registros mais antigos que o prazo (padrão 90 dias)
/// e faz uma cópia do banco em DATA_DIR/backups, mantendo as 14 últimas.
/// </summary>
public sealed class MaintenanceService(Db db, ILogger<MaintenanceService> log) : BackgroundService
{
    private const int BackupsToKeep = 14;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Primeira execução alguns minutos depois de iniciar, depois a cada 24 horas.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                RunOnce();
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Falha na manutenção diária");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public void RunOnce()
    {
        using var conn = db.Open();
        var settings = Settings.Load(conn);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-settings.RetentionDays).ToUnixTimeMilliseconds();
        var removed = conn.Exec("DELETE FROM segments WHERE end_utc < $c", ("$c", cutoff));
        var auditCutoff = DateTimeOffset.UtcNow.AddDays(-365).ToUnixTimeMilliseconds();
        conn.Exec("DELETE FROM audit WHERE at_utc < $c", ("$c", auditCutoff));
        log.LogInformation("Manutenção: {Removed} períodos com mais de {Days} dias apagados", removed, settings.RetentionDays);

        var backupDir = Path.Combine(db.DataDir, "backups");
        Directory.CreateDirectory(backupDir);
        var target = Path.Combine(backupDir, $"monitor-{DateTime.UtcNow:yyyyMMdd}.db");
        if (File.Exists(target)) File.Delete(target);
        conn.Exec("VACUUM INTO $p", ("$p", target));

        foreach (var old in Directory.GetFiles(backupDir, "monitor-*.db").OrderByDescending(f => f).Skip(BackupsToKeep))
            File.Delete(old);
        log.LogInformation("Cópia do banco salva em {Target}", target);
    }
}
