using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace Monitor.Agent;

/// <summary>
/// Ícone visível ao lado do relógio. Não abre janelas nem faz perguntas ao funcionário.
/// Também acompanha bloqueio de tela, suspensão e desligamento.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly AgentWorker _worker;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _status;

    public TrayContext(AgentWorker worker)
    {
        _worker = worker;

        _status = new ToolStripMenuItem("Monitoramento ativo") { Enabled = false };
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Monitor Portoconseg") { Enabled = false, Font = new Font(SystemFonts.MenuFont ?? SystemFonts.DefaultFont, FontStyle.Bold) });
        menu.Items.Add(new ToolStripMenuItem("Registra horários de uso e os programas e sites em uso no expediente.") { Enabled = false });
        menu.Items.Add(new ToolStripMenuItem("Não registra o que é digitado, não tira fotos da tela e não lê conversas.") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_status);
        menu.Opening += (_, _) => _status.Text = StatusText();

        _icon = new NotifyIcon
        {
            Icon = BuildIcon(),
            Text = "Monitor Portoconseg: monitoramento ativo",
            ContextMenuStrip = menu,
            Visible = true,
        };

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionEnding += OnSessionEnding;
        Application.ApplicationExit += (_, _) => Shutdown();

        _worker.Start();
    }

    private string StatusText()
    {
        var client = _worker.Client;
        if (client.LastError is not null) return "Sem conexão com o servidor. Os dados ficam guardados e são enviados depois.";
        return client.LastSuccess is { } t ? $"Último envio: {t:HH:mm}" : "Aguardando o primeiro envio";
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
            case SessionSwitchReason.SessionLogoff:
                _worker.SetLocked(true);
                break;
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
            case SessionSwitchReason.SessionLogon:
                _worker.SetLocked(false);
                break;
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            Log.Write("Computador entrando em suspensão.");
            _worker.FlushAll();
        }
        else if (e.Mode == PowerModes.Resume)
        {
            Log.Write("Computador voltou da suspensão.");
        }
    }

    private void OnSessionEnding(object sender, SessionEndingEventArgs e)
    {
        Log.Write($"Sessão encerrando ({e.Reason}).");
        _worker.FlushAll();
    }

    private bool _shutDown;

    private void Shutdown()
    {
        if (_shutDown) return;
        _shutDown = true;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionEnding -= OnSessionEnding;
        _worker.FlushAll();
        _worker.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Shutdown();
        base.Dispose(disposing);
    }

    /// <summary>Ícone desenhado na hora: círculo azul com um visto branco.</summary>
    private static Icon BuildIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var fill = new SolidBrush(Color.FromArgb(31, 95, 139));
            g.FillEllipse(fill, 1, 1, 30, 30);
            using var pen = new Pen(Color.White, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, [new Point(9, 17), new Point(14, 22), new Point(23, 11)]);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
