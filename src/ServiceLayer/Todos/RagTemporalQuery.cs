#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Fistix.TaskManager.Core.DomainModel.Aggregates;

namespace Fistix.TaskManager.ServiceLayer.Todos;

public enum RagTemporalKind
{
    None = 0,
    ThisWeek,
    NextWeek,
    LastWeek,
    ThisMonth,
    NextMonth,
    LastMonth,
    Today,
    Tomorrow,
    Yesterday,
    Overdue
}

public sealed class RagTemporalWindow
{
    public RagTemporalKind Kind { get; init; }
    /// <summary>Inclusive start date (UTC date).</summary>
    public DateTime? StartDate { get; init; }
    /// <summary>Exclusive end date (UTC date). For overdue: due &lt; EndDateExclusive.</summary>
    public DateTime? EndDateExclusive { get; init; }
    public string Label { get; init; } = string.Empty;

    public bool IsTemporal => Kind != RagTemporalKind.None;
}

/// <summary>
/// Deterministic date-window detection for RAG list questions.
/// LLM must not decide which todos fall in a time window.
/// Temporal detection is driven by the question text (not a separate Ask context mode).
/// </summary>
public static class RagTemporalQuery
{
    public const int MaxTemporalResults = 50;

    public static RagTemporalWindow Detect(string? question, DateTime? utcToday = null)
    {
        var today = (utcToday ?? DateTime.UtcNow).Date;
        var text = (question ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(text))
        {
            return None();
        }

        // Week phrases (order: next → last → this) — ISO Monday–Sunday calendar weeks UTC
        if (ContainsAny(text, "next week", "coming week", "following week"))
        {
            var start = StartOfIsoWeek(today).AddDays(7);
            return Range(
                RagTemporalKind.NextWeek,
                start,
                start.AddDays(7),
                $"next week ({start:yyyy-MM-dd} to {start.AddDays(6):yyyy-MM-dd} UTC)");
        }

        if (ContainsAny(text, "last week", "previous week", "past week"))
        {
            var start = StartOfIsoWeek(today).AddDays(-7);
            return Range(
                RagTemporalKind.LastWeek,
                start,
                start.AddDays(7),
                $"last week ({start:yyyy-MM-dd} to {start.AddDays(6):yyyy-MM-dd} UTC)");
        }

        if (ContainsAny(text, "this week", "due this week", "for this week", "current week"))
        {
            return ThisWeek(today);
        }

        // Month phrases (order: next → last → this) — calendar months UTC
        if (ContainsAny(text, "next month", "coming month", "following month"))
        {
            var start = FirstOfMonth(today).AddMonths(1);
            var end = start.AddMonths(1);
            return Range(
                RagTemporalKind.NextMonth,
                start,
                end,
                $"next month ({start:yyyy-MM} UTC)");
        }

        if (ContainsAny(text, "last month", "previous month", "past month"))
        {
            var thisMonth = FirstOfMonth(today);
            var start = thisMonth.AddMonths(-1);
            return Range(
                RagTemporalKind.LastMonth,
                start,
                thisMonth,
                $"last month ({start:yyyy-MM} UTC)");
        }

        if (ContainsAny(text, "this month", "current month", "due this month", "for this month"))
        {
            var start = FirstOfMonth(today);
            var end = start.AddMonths(1);
            return Range(
                RagTemporalKind.ThisMonth,
                start,
                end,
                $"this month ({start:yyyy-MM} UTC)");
        }

        // Day phrases (order: tomorrow → yesterday → overdue → today)
        if (ContainsAny(text, "tomorrow"))
        {
            var start = today.AddDays(1);
            return Range(
                RagTemporalKind.Tomorrow,
                start,
                start.AddDays(1),
                $"tomorrow ({start:yyyy-MM-dd} UTC)");
        }

        if (ContainsAny(text, "yesterday"))
        {
            var start = today.AddDays(-1);
            return Range(
                RagTemporalKind.Yesterday,
                start,
                today,
                $"yesterday ({start:yyyy-MM-dd} UTC)");
        }

        if (ContainsAny(text, "overdue", "past due", "late tasks"))
        {
            return new RagTemporalWindow
            {
                Kind = RagTemporalKind.Overdue,
                StartDate = null,
                EndDateExclusive = today,
                Label = $"overdue (before {today:yyyy-MM-dd} UTC)"
            };
        }

        if (ContainsAny(text, "due today", "today's tasks", "tasks today") ||
            (text.Contains("today", StringComparison.Ordinal) &&
             ContainsAny(text, "due", "have", "what", "tasks", "todo", "working")))
        {
            return Range(
                RagTemporalKind.Today,
                today,
                today.AddDays(1),
                $"today ({today:yyyy-MM-dd} UTC)");
        }

        return None();
    }

    /// <summary>ISO Monday–Sunday week containing today (UTC).</summary>
    public static RagTemporalWindow ThisWeekWindow(DateTime? utcToday = null) =>
        ThisWeek((utcToday ?? DateTime.UtcNow).Date);

    public static bool Matches(TodoTask todo, RagTemporalWindow window)
    {
        if (!window.IsTemporal)
        {
            return true;
        }

        var due = todo.DueDate.Date;

        if (window.Kind == RagTemporalKind.Overdue)
        {
            return due < (window.EndDateExclusive ?? DateTime.UtcNow.Date);
        }

        return window.StartDate.HasValue
               && window.EndDateExclusive.HasValue
               && due >= window.StartDate.Value
               && due < window.EndDateExclusive.Value;
    }

    public static bool ShouldExcludeCompleted(string? question)
    {
        var text = (question ?? string.Empty).ToLowerInvariant();
        return ContainsAny(text, "have", "working", "todo", "tasks", "overdue", "due", "pending")
               || string.IsNullOrWhiteSpace(text);
    }

    /// <summary>
    /// True when the question only asks to list/show tasks in a time window.
    /// False when it needs LLM judgment on the date-filtered set (priority, yes/no, summarize, etc.).
    /// </summary>
    public static bool IsPlainListQuestion(string? question)
    {
        var text = (question ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        // Analytical / attribute filters / yes-no → LLM after due-date filter
        if (ContainsAny(
                text,
                "critical",
                "high priority",
                "medium priority",
                "low priority",
                "urgent",
                "priority",
                "priorities",
                "was there",
                "were there",
                "is there",
                "are there",
                "any critical",
                "any high",
                "any medium",
                "any low",
                "summarize",
                "summary",
                "overview",
                "which ",
                "blocking",
                "blocked",
                "important",
                "how many",
                "count of",
                "most important",
                "highest priority"))
        {
            return false;
        }

        // "any …" yes/no outside pure list phrasing (e.g. "any tasks for me last month that…")
        if (text.Contains(" any ", StringComparison.Ordinal) ||
            text.StartsWith("any ", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    public static bool IsCompleted(TodoTask todo) =>
        string.Equals(todo.Status, "Completed", StringComparison.OrdinalIgnoreCase);

    public static string BuildDeterministicAnswer(
        IReadOnlyList<TodoTask> tasks,
        RagTemporalWindow window)
    {
        if (tasks.Count == 0)
        {
            return $"No tasks due in that window ({window.Label}).";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Tasks for {window.Label} ({tasks.Count}):");
        sb.AppendLine();
        foreach (var t in tasks)
        {
            sb.Append("- ");
            sb.Append(string.IsNullOrWhiteSpace(t.Title) ? "(untitled)" : t.Title);
            sb.Append(" (");
            sb.Append(t.ExternalId);
            sb.Append(") — due ");
            sb.Append(t.DueDate.ToString("yyyy-MM-dd"));
            sb.Append(" · ");
            sb.Append(string.IsNullOrWhiteSpace(t.Priority) ? "—" : t.Priority);
            sb.Append(" · ");
            sb.Append(string.IsNullOrWhiteSpace(t.Status) ? "—" : t.Status);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static RagTemporalWindow ThisWeek(DateTime today)
    {
        var start = StartOfIsoWeek(today);
        return Range(
            RagTemporalKind.ThisWeek,
            start,
            start.AddDays(7),
            $"this week ({start:yyyy-MM-dd} to {start.AddDays(6):yyyy-MM-dd} UTC)");
    }

    /// <summary>Monday 00:00 UTC of the ISO week containing <paramref name="day"/>.</summary>
    private static DateTime StartOfIsoWeek(DateTime day)
    {
        var d = day.Date;
        var daysFromMonday = d.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)d.DayOfWeek - 1;
        return d.AddDays(-daysFromMonday);
    }

    private static DateTime FirstOfMonth(DateTime day) =>
        new(day.Year, day.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    private static RagTemporalWindow Range(
        RagTemporalKind kind,
        DateTime startInclusive,
        DateTime endExclusive,
        string label) =>
        new()
        {
            Kind = kind,
            StartDate = startInclusive,
            EndDateExclusive = endExclusive,
            Label = label
        };

    private static RagTemporalWindow None() =>
        new() { Kind = RagTemporalKind.None, Label = string.Empty };

    private static bool ContainsAny(string text, params string[] phrases) =>
        phrases.Any(p => text.Contains(p, StringComparison.Ordinal));
}
