namespace ItmoBot.Hosting.Contracts;

public interface IBotSessionFactory
{
    ValueTask<IBotSession> CreateAsync();
}
