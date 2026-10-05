namespace ItmoBot.Hosting.Contracts;

public interface IApplicationRunner
{
    Task<int> RunAsync(ApplicationCommand command);
}
