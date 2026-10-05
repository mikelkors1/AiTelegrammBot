using ItmoBot.Tools.Contracts;
using System.Text;

namespace ItmoBot.Tools;

public sealed class SecretConsoleInput : ISecretConsoleInput
{
    public string Read(string prompt)
    {
        Console.Write(prompt);
        if (Console.IsInputRedirected)
        {
            return Console.ReadLine()?.Trim() ?? "";
        }

        var result = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (result.Length > 0)
                {
                    result.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                result.Append(key.KeyChar);
            }
        }

        Console.WriteLine();
        return result.ToString().Trim();
    }
}
