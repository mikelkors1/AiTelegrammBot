using ItmoBot.Application.Contracts;
using ItmoBot.Configuration.Models;

namespace ItmoBot.Infrastructure.Logging;

public sealed class SecretRedactorFactory : ISecretRedactorFactory
{
    public ISecretRedactor Create(Settings settings)
    {
        return new SecretRedactor(settings);
    }
}
