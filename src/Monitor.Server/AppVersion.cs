using System.Reflection;

namespace Monitor.Server;

/// <summary>Versão do sistema (arquivo VERSION na raiz do repositório).</summary>
public static class AppVersion
{
    public static string Current { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
}
