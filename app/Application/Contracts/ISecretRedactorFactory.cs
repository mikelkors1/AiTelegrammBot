using ItmoBot.Configuration.Models;

namespace ItmoBot.Application.Contracts;

public interface ISecretRedactorFactory
{
    ISecretRedactor Create(Settings settings);
}
