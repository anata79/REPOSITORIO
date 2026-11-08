using Monitor.Server.Data;
using Monitor.Server.Security;

namespace Monitor.Server;

/// <summary>
/// Comandos de administração, executados no servidor:
/// <code>
/// docker compose exec -it app ./Monitor.Server usuario-criar andre "André"
/// docker compose exec -it app ./Monitor.Server usuario-senha andre
/// docker compose exec -it app ./Monitor.Server usuario-listar
/// docker compose exec -it app ./Monitor.Server usuario-remover andre
/// </code>
/// A senha é pedida no terminal (ou lida da variável MONITOR_PASSWORD).
/// </summary>
public static class Cli
{
    private static readonly string[] Commands = ["usuario-criar", "usuario-senha", "usuario-listar", "usuario-remover"];

    public static bool IsCommand(string arg) => Commands.Contains(arg);

    public static int Run(string[] args, Db db)
    {
        using var conn = db.Open();
        switch (args[0])
        {
            case "usuario-criar" when args.Length >= 3:
            {
                var username = args[1].Trim().ToLowerInvariant();
                var display = string.Join(' ', args.Skip(2)).Trim();
                if (conn.Scalar<long>("SELECT COUNT(*) FROM users WHERE username = $u", ("$u", username)) > 0)
                {
                    Console.Error.WriteLine($"O usuário {username} já existe. Para trocar a senha use: usuario-senha {username}");
                    return 1;
                }
                var password = ReadPassword();
                if (password is null) return 1;
                conn.Exec(
                    "INSERT INTO users(username, display_name, password_hash, created_utc) VALUES ($u, $d, $h, $t)",
                    ("$u", username), ("$d", display), ("$h", Secrets.HashPassword(password)),
                    ("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                Console.WriteLine($"Usuário {username} ({display}) criado.");
                return 0;
            }
            case "usuario-senha" when args.Length >= 2:
            {
                var username = args[1].Trim().ToLowerInvariant();
                if (conn.Scalar<long>("SELECT COUNT(*) FROM users WHERE username = $u", ("$u", username)) == 0)
                {
                    Console.Error.WriteLine($"Usuário {username} não encontrado.");
                    return 1;
                }
                var password = ReadPassword();
                if (password is null) return 1;
                conn.Exec("UPDATE users SET password_hash = $h WHERE username = $u",
                    ("$h", Secrets.HashPassword(password)), ("$u", username));
                Console.WriteLine($"Senha de {username} alterada.");
                return 0;
            }
            case "usuario-listar":
            {
                var users = conn.Query("SELECT username, display_name FROM users ORDER BY username",
                    r => $"{r.GetString(0)}\t{r.GetString(1)}");
                Console.WriteLine(users.Count == 0 ? "Nenhum usuário cadastrado." : string.Join(Environment.NewLine, users));
                return 0;
            }
            case "usuario-remover" when args.Length >= 2:
            {
                var removed = conn.Exec("DELETE FROM users WHERE username = $u", ("$u", args[1].Trim().ToLowerInvariant()));
                Console.WriteLine(removed > 0 ? "Usuário removido." : "Usuário não encontrado.");
                return removed > 0 ? 0 : 1;
            }
            default:
                Console.Error.WriteLine("""
                    Uso:
                      usuario-criar <login> <nome>
                      usuario-senha <login>
                      usuario-listar
                      usuario-remover <login>
                    """);
                return 2;
        }
    }

    private static string? ReadPassword()
    {
        var fromEnv = Environment.GetEnvironmentVariable("MONITOR_PASSWORD");
        string? password;
        if (!string.IsNullOrEmpty(fromEnv))
        {
            password = fromEnv;
        }
        else if (Console.IsInputRedirected)
        {
            password = Console.ReadLine();
        }
        else
        {
            Console.Write("Senha (mínimo 8 caracteres): ");
            password = ReadHidden();
            Console.Write("Repita a senha: ");
            var again = ReadHidden();
            if (password != again)
            {
                Console.Error.WriteLine("As senhas não conferem.");
                return null;
            }
        }

        var problem = Secrets.ValidatePasswordRules(password ?? "");
        if (problem is not null)
        {
            Console.Error.WriteLine(problem);
            return null;
        }
        return password;
    }

    private static string ReadHidden()
    {
        var chars = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (chars.Count > 0) chars.RemoveAt(chars.Count - 1);
                continue;
            }
            if (!char.IsControl(key.KeyChar)) chars.Add(key.KeyChar);
        }
        Console.WriteLine();
        return new string(chars.ToArray());
    }
}
