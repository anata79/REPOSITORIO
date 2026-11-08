using System.Diagnostics;
using System.Windows.Automation;
using Monitor.Core;

namespace Monitor.Agent;

/// <summary>Lê a janela em primeiro plano: programa, título e, nos navegadores, o domínio do site.</summary>
internal sealed class WindowProbe
{
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi",
    };

    private readonly Dictionary<uint, (string Process, string? AppName)> _processCache = [];
    private (IntPtr Hwnd, string? Title, string? Domain, DateTime At) _urlCache;
    private readonly Dictionary<IntPtr, DateTime> _slowWindows = [];

    public Sample Read(DateTimeOffset at, bool locked)
    {
        var idle = Native.IdleTime();
        if (locked)
            return new Sample(at, true, idle, null, null, null, null);

        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return new Sample(at, false, idle, null, null, null, null);

        var title = Native.WindowTitle(hwnd);
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        var (process, appName) = DescribeProcess(pid);

        if (process == "applicationframehost" && Native.RealProcessOfFrameHost(hwnd, pid) is { } realPid)
            (process, appName) = DescribeProcess(realPid);

        string? domain = null;
        if (process is not null && Browsers.Contains(process))
            domain = BrowserDomain(hwnd, process, title);

        return new Sample(at, false, idle, process, appName, domain, title);
    }

    private (string? Process, string? AppName) DescribeProcess(uint pid)
    {
        if (pid == 0) return (null, null);
        if (_processCache.TryGetValue(pid, out var cached)) return cached;

        string? name = null;
        string? description = null;
        var path = Native.ProcessImagePath(pid);
        if (path is not null)
        {
            name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                description = string.IsNullOrWhiteSpace(info.FileDescription) ? info.ProductName : info.FileDescription;
            }
            catch (Exception)
            {
                // Sem descrição: fica só o nome do programa.
            }
        }
        else
        {
            try
            {
                using var p = Process.GetProcessById((int)pid);
                name = p.ProcessName.ToLowerInvariant();
            }
            catch (Exception)
            {
                return (null, null);
            }
        }

        if (_processCache.Count > 500) _processCache.Clear();
        var result = (name!, description?.Trim());
        _processCache[pid] = result;
        return result;
    }

    /// <summary>
    /// Lê a barra de endereço pela automação de interface do Windows (a mesma usada por leitores de tela).
    /// Só consulta de novo quando a janela ou o título mudam, e desiste de janelas que demoram a responder.
    /// </summary>
    private string? BrowserDomain(IntPtr hwnd, string process, string? title)
    {
        var now = DateTime.UtcNow;
        if (_urlCache.Hwnd == hwnd && _urlCache.Title == title && now - _urlCache.At < TimeSpan.FromMinutes(2))
            return _urlCache.Domain;

        if (_slowWindows.TryGetValue(hwnd, out var until) && until > now)
            return _urlCache.Hwnd == hwnd ? _urlCache.Domain : null;

        string? value = null;
        var task = Task.Run(() => ReadAddressBar(hwnd, process));
        if (task.Wait(TimeSpan.FromSeconds(2)))
        {
            value = task.Result;
        }
        else
        {
            _slowWindows[hwnd] = now.AddMinutes(1);
            Log.Write($"Navegador demorou para responder ({process}); tentando de novo em 1 minuto.");
        }

        var domain = DomainParser.FromAddressBar(value);

        // Enquanto a pessoa digita na barra, o texto não é um endereço: mantém o último domínio da mesma janela.
        if (domain is null && _urlCache.Hwnd == hwnd)
            domain = _urlCache.Domain;

        _urlCache = (hwnd, title, domain, now);
        return domain;
    }

    private static string? ReadAddressBar(IntPtr hwnd, string process)
    {
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            AutomationElement? edit = null;

            if (process.Equals("firefox", StringComparison.OrdinalIgnoreCase))
            {
                edit = root.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "urlbar-input"));
            }

            edit ??= root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

            if (edit is not null && edit.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
                return ((ValuePattern)pattern).Current.Value;
        }
        catch (Exception)
        {
            // Janela fechou ou navegador não respondeu.
        }
        return null;
    }
}
