using ItmoBot.Tools.ResultTypes;

namespace ItmoBot.Tools.Contracts;

public interface IEnvironmentPreparer
{
    EnvironmentPreparationResult Prepare(string path = ".env");
}
