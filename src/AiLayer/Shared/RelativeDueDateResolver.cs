#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fistix.TaskManager.AiLayer.Models;
using Fistix.TaskManager.AiLayer.Tools;

namespace Fistix.TaskManager.AiLayer.Shared;

/// <summary>
/// Resolves relative weekday phrases (next/coming Sunday, …) to concrete UTC dates
/// and corrects proposed create/update dueDate args when the transcript is clearer than the LLM.
/// </summary>
public static class RelativeDueDateResolver
{
    private static readonly (string Name, DayOfWeek Day)[] Weekdays =
    [
        ("sunday", DayOfWeek.Sunday),
        ("monday", DayOfWeek.Monday),
        ("tuesday", DayOfWeek.Tuesday),
        ("wednesday", DayOfWeek.Wednesday),
        ("thursday", DayOfWeek.Thursday),
        ("friday", DayOfWeek.Friday),
        ("saturday", DayOfWeek.Saturday)
    ];

    /// <summary>
    /// If the user prompt mentions next/coming/this &lt;weekday&gt;, overwrite dueDate on create/update calls.
    /// </summary>
    public static void ApplyToProposedCalls(
        string userPrompt,
        IList<ProposedToolCall> calls,
        DateTime? todayUtc = null)
    {
        if (string.IsNullOrWhiteSpace(userPrompt) || calls.Count == 0)
        {
            return;
        }

        if (!TryResolveFromPrompt(userPrompt, todayUtc ?? DateTime.UtcNow.Date, out var due))
        {
            return;
        }

        var dueIso = due.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (var call in calls)
        {
            if (!string.Equals(call.ToolName, TodoToolDefinitions.CreateTodo, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(call.ToolName, TodoToolDefinitions.UpdateTodo, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            call.Arguments ??= new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            call.Arguments["dueDate"] = JsonSerializer.SerializeToElement(dueIso);
        }
    }

    public static bool TryResolveFromPrompt(string prompt, DateTime todayUtc, out DateTime dueDate)
    {
        dueDate = default;
        var text = prompt.ToLowerInvariant();
        var today = todayUtc.Date;

        foreach (var (name, day) in Weekdays)
        {
            if (Regex.IsMatch(text, $@"\blast\s+{name}\b", RegexOptions.CultureInvariant))
            {
                dueDate = PreviousWeekday(today, day);
                return true;
            }

            if (Regex.IsMatch(text, $@"\b(?:next|coming)\s+{name}\b", RegexOptions.CultureInvariant))
            {
                dueDate = NextOrSameWeekday(today, day, allowToday: false);
                return true;
            }

            if (Regex.IsMatch(text, $@"\bthis\s+{name}\b", RegexOptions.CultureInvariant))
            {
                dueDate = NextOrSameWeekday(today, day, allowToday: true);
                return true;
            }
        }

        if (Regex.IsMatch(text, @"\btomorrow\b"))
        {
            dueDate = today.AddDays(1);
            return true;
        }

        // Bare weekday with scheduling cue: "visit the dentist on Sunday"
        if (Regex.IsMatch(text, @"\b(?:due|on|by|until|schedule|visit|appointment|coming)\b"))
        {
            foreach (var (name, day) in Weekdays)
            {
                if (Regex.IsMatch(text, $@"\b{name}\b", RegexOptions.CultureInvariant))
                {
                    dueDate = NextOrSameWeekday(today, day, allowToday: false);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Next occurrence of <paramref name="day"/>. If today is that weekday and
    /// <paramref name="allowToday"/> is false, returns next week's day.
    /// </summary>
    public static DateTime NextOrSameWeekday(DateTime todayUtc, DayOfWeek day, bool allowToday)
    {
        var today = todayUtc.Date;
        var delta = ((int)day - (int)today.DayOfWeek + 7) % 7;
        if (delta == 0 && !allowToday)
        {
            delta = 7;
        }

        return today.AddDays(delta);
    }

    /// <summary>
    /// Most recent occurrence of <paramref name="day"/> strictly before today
    /// (if today is that weekday, returns 7 days ago).
    /// </summary>
    public static DateTime PreviousWeekday(DateTime todayUtc, DayOfWeek day)
    {
        var today = todayUtc.Date;
        var delta = ((int)today.DayOfWeek - (int)day + 7) % 7;
        if (delta == 0)
        {
            delta = 7;
        }

        return today.AddDays(-delta);
    }
}
