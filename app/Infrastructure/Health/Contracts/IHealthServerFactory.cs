using ItmoBot.Shared.ValueObjects;

namespace ItmoBot.Infrastructure.Health.Contracts;

public interface IHealthServerFactory
{
    WebApplication Create(HealthState state, PortNumber port);
}
