using ItmoBot.Shared.Errors;

namespace ItmoBot.Configuration.ResultTypes;

public abstract record class SettingsLoadResult
{
    private SettingsLoadResult()
    {
    }

    public sealed record class Success(ItmoBot.Configuration.Models.Settings Settings) : SettingsLoadResult;

    public sealed record class Failure(AppError Error) : SettingsLoadResult
    {
        public string Message
        {
            get
            {
                return Error.Message;
            }
        }
    }
}
