using System.Runtime.InteropServices;
using System.Text;

namespace Monitor.Agent;

/// <summary>Funções do Windows usadas pelo agente.</summary>
internal static partial class Native
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder exeName, ref uint size);

    /// <summary>Tempo desde o último uso de teclado ou mouse nesta sessão.</summary>
    public static TimeSpan IdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        var now = unchecked((uint)Environment.TickCount);
        var idleMs = unchecked(now - info.dwTime);
        // O contador pode voltar alguns milissegundos (documentado pela Microsoft); valores absurdos viram zero.
        return idleMs > int.MaxValue ? TimeSpan.Zero : TimeSpan.FromMilliseconds(idleMs);
    }

    public static string? WindowTitle(IntPtr hWnd)
    {
        var length = GetWindowTextLengthW(hWnd);
        if (length <= 0) return null;
        var sb = new StringBuilder(length + 1);
        GetWindowTextW(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string? ProcessImagePath(uint processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var size = 1024u;
            var sb = new StringBuilder((int)size);
            return QueryFullProcessImageNameW(handle, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>
    /// Aplicativos da Microsoft Store aparecem dentro de "ApplicationFrameHost".
    /// Procura a janela filha que pertence ao aplicativo de verdade.
    /// </summary>
    public static uint? RealProcessOfFrameHost(IntPtr frame, uint framePid)
    {
        uint? found = null;
        EnumChildWindows(frame, (child, _) =>
        {
            GetWindowThreadProcessId(child, out var pid);
            if (pid != 0 && pid != framePid)
            {
                found = pid;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
