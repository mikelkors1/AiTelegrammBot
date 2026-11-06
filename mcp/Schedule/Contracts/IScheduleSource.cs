using ItmoBot.Mcp.Schedule.Models;
using ItmoBot.Mcp.Schedule.ResultTypes;

namespace ItmoBot.Mcp.Schedule.Contracts;

public interface IScheduleSource
{
    Task<ScheduleResult<ScheduleSnapshot>> LoadAsync(CancellationToken cancellationToken);
}
