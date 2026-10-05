using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Models;

public sealed record class ReplySegment(string Text, bool IsCode, CodeLanguage? Language);
