using System.Globalization;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Mcp.Schedule.Models;
using ItmoBot.Mcp.Schedule.ResultTypes;
using ItmoBot.Mcp.Schedule;
using ItmoBot.Mcp.Validation;
using ItmoBot.Tests.TestDoubles;
using Xunit;

namespace ItmoBot.Tests.Unit.Schedule;

public sealed class ScheduleServiceTests
{
    [Fact]
    public async Task GetDayAsync_ExistingDate_ReturnsLessonsAndEmptyDateReturnsEmptyList()
    {
        // Arrange
        DateOnly date = ScheduleChecks.ScheduleDate("2026-10-12");
        var source = new FixedScheduleSource(new ScheduleResult<ScheduleSnapshot>.Success(new ScheduleSnapshot(
            new Dictionary<DateOnly, IReadOnlyList<Lesson>>
            {
                [date] = [new Lesson(ScheduleChecks.LessonTime("09:00"), ScheduleChecks.LessonTime("10:30"), ScheduleChecks.LessonText("Алгоритмы"), ScheduleChecks.LessonText("Аудитория A"))],
            })));
        var service = new ScheduleService(source, new UserTimeZone("Europe/Moscow"), new ManualTimeProvider());

        // Act
        var existing = await service.GetDayAsync(date, CancellationToken.None);
        var empty = await service.GetDayAsync(ScheduleChecks.ScheduleDate("2026-10-18"), CancellationToken.None);

        // Assert
        var lesson = Assert.Single(Assert.IsType<ScheduleResult<ScheduleDay>.Success>(existing).Value.Lessons);
        Assert.Equal(new TimeOnly(9, 0), lesson.Start);
        Assert.Equal(new TimeOnly(10, 30), lesson.End);
        Assert.Equal("Алгоритмы", lesson.Title);
        Assert.Equal("Аудитория A", lesson.Location);
        Assert.Empty(Assert.IsType<ScheduleResult<ScheduleDay>.Success>(empty).Value.Lessons);
    }

    [Theory]
    [InlineData("2026-01-01T10:00:00Z", "Europe/Moscow", "2025-12-29", "2026-01-04")]
    [InlineData("2026-11-01T20:59:00Z", "Europe/Moscow", "2026-10-26", "2026-11-01")]
    [InlineData("2026-11-01T21:00:00Z", "Europe/Moscow", "2026-11-02", "2026-11-08")]
    [InlineData("2026-03-29T22:30:00Z", "Europe/Berlin", "2026-03-30", "2026-04-05")]
    [InlineData("2026-10-25T23:30:00Z", "Europe/Berlin", "2026-10-26", "2026-11-01")]
    public async Task GetCurrentWeekAsync_Week_UsesConfiguredZoneAndCrossesMonthYearAndDst(string now, string zone, string start, string end)
    {
        // Arrange
        var clock = new ManualTimeProvider();
        clock.Advance(DateTimeOffset.Parse(now, CultureInfo.InvariantCulture) - DateTimeOffset.UnixEpoch);
        var source = new FixedScheduleSource(new ScheduleResult<ScheduleSnapshot>.Success(new ScheduleSnapshot(
            new Dictionary<DateOnly, IReadOnlyList<Lesson>>())));
        var service = new ScheduleService(source, new UserTimeZone(zone), clock);

        // Act
        var result = await service.GetCurrentWeekAsync(CancellationToken.None);

        // Assert
        var week = Assert.IsType<ScheduleResult<ScheduleWeek>.Success>(result).Value;
        Assert.Equal(ScheduleChecks.ScheduleDate(start), week.Start);
        Assert.Equal(ScheduleChecks.ScheduleDate(end), week.End);
        Assert.Equal(7, week.Days.Count);
        Assert.Equal(7, week.Days.Select(day => day.Date).Distinct().Count());
        Assert.Equal(week.Start, week.Days[0].Date);
        Assert.Equal(week.End, week.Days[6].Date);
        Assert.Equal(DayOfWeek.Monday, week.Start.DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, week.End.DayOfWeek);
        Assert.Equal(1, source.LoadCalls);
    }

    [Fact]
    public async Task GetDayAsync_SourceFailure_IsNotReportedAsEmptySchedule()
    {
        // Arrange
        var error = new ScheduleError(ScheduleErrorCode.SourceUnavailable, "Источник недоступен.");
        var source = new FixedScheduleSource(new ScheduleResult<ScheduleSnapshot>.Failure(error));
        var service = new ScheduleService(source, new UserTimeZone("Europe/Moscow"), new ManualTimeProvider());

        // Act
        var day = await service.GetDayAsync(ScheduleChecks.ScheduleDate("2026-10-12"), CancellationToken.None);
        var week = await service.GetCurrentWeekAsync(CancellationToken.None);

        // Assert
        Assert.Equal(error, Assert.IsType<ScheduleResult<ScheduleDay>.Failure>(day).Error);
        Assert.Equal(error, Assert.IsType<ScheduleResult<ScheduleWeek>.Failure>(week).Error);
    }

    [Theory]
    [InlineData("2026-02-29")]
    [InlineData("2026-10-1")]
    [InlineData("12.10.2026")]
    [InlineData("2026-10-12T09:00:00Z")]
    public void ParseDate_InvalidCalendarDate_IsRejected(string input)
    {
        // Arrange
        var text = input;

        // Act
        var error = Assert.Throws<ArgumentException>(() => ScheduleChecks.ScheduleDate(text));

        // Assert
        Assert.Contains("YYYY-MM-DD", error.Message);
    }

    [Fact]
    public void ParseDate_ExistingLeapDay_IsAccepted()
    {
        // Arrange
        var text = "2028-02-29";

        // Act
        DateOnly date = ScheduleChecks.ScheduleDate(text);

        // Assert
        Assert.Equal(new DateOnly(2028, 2, 29), date);
    }

}
