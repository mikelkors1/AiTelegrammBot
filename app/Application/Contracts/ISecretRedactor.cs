namespace ItmoBot.Application.Contracts;

public interface ISecretRedactor
{
    string Redact(string text);
}
