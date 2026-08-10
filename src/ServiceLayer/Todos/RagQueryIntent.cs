#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Fistix.TaskManager.Core.DomainModel.Aggregates;

namespace Fistix.TaskManager.ServiceLayer.Todos;

/// <summary>
/// Structured Ask intent: priority/status filters and a topic-only search query.
/// Date windows stay in <see cref="RagTemporalQuery"/>; this handles the rest deterministically.
/// </summary>
public sealed class RagQueryIntent
{
    public string OriginalQuestion { get; init; } = string.Empty;

    /// <summary>High / Medium / Low when the question names a priority; otherwise null.</summary>
    public string? PriorityFilter { get; init; }

    /// <summary>True unless the user asks for completed/done/finished/closed work.</summary>
    public bool ExcludeCompleted { get; init; } = true;

    /// <summary>Topic text for embedding/FTS after stripping date/priority filler.</summary>
    public string SearchQuery { get; init; } = string.Empty;

    public bool IsAdviceQuestion { get; init; }

    public static RagQueryIntent Parse(string? question)
    {
        var original = (question ?? string.Empty).Trim();
        var text = original.ToLowerInvariant();
        if (string.IsNullOrEmpty(text))
        {
            return new RagQueryIntent
            {
                OriginalQuestion = original,
                SearchQuery = string.Empty,
                ExcludeCompleted = true
            };
        }

        var priority = DetectPriority(text);
        var excludeCompleted = !ContainsAny(
            text,
            "completed",
            "complete",
            "done",
            "finished",
            "closed",
            "already done");

        var advice = ContainsAny(
            text,
            "what should i",
            "what should we",
            "work on",
            "recommend",
            "prioritize",
            "prioritise",
            "suggest",
            "what next",
            "focus on");

        var search = BuildSearchQuery(original);

        return new RagQueryIntent
        {
            OriginalQuestion = original,
            PriorityFilter = priority,
            ExcludeCompleted = excludeCompleted,
            SearchQuery = search,
            IsAdviceQuestion = advice
        };
    }

    /// <summary>Retrieval string: prefer topic-only SearchQuery, else original.</summary>
    public string EffectiveSearchQuery =>
        string.IsNullOrWhiteSpace(SearchQuery) ? OriginalQuestion : SearchQuery;

    public bool MatchesPriority(string? priority)
    {
        if (string.IsNullOrWhiteSpace(PriorityFilter))
        {
            return true;
        }

        return string.Equals(priority?.Trim(), PriorityFilter, StringComparison.OrdinalIgnoreCase);
    }

    public bool MatchesStatus(string? status)
    {
        if (!ExcludeCompleted)
        {
            return true;
        }

        return !string.Equals(status?.Trim(), "Completed", StringComparison.OrdinalIgnoreCase);
    }

    public bool MatchesTodo(TodoTask todo) =>
        MatchesPriority(todo.Priority) && MatchesStatus(todo.Status);

    public bool MatchesSource(string? priority, string? status) =>
        MatchesPriority(priority) && MatchesStatus(status);

    public static IReadOnlyList<TodoTask> OrderForRag(IEnumerable<TodoTask> todos) =>
        todos
            .OrderBy(t => PriorityRank(t.Priority))
            .ThenBy(t => t.DueDate)
            .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Ensures each topic contributes up to a quota before filling remaining slots globally.
    /// Prevents one domain (e.g. Auth) from crowding out another (e.g. Payments).
    /// </summary>
    public static IReadOnlyList<TodoTask> MergeTopicsFairly(
        IReadOnlyList<IReadOnlyList<TodoTask>> perTopicOrdered,
        int totalLimit)
    {
        if (totalLimit <= 0 || perTopicOrdered.Count == 0)
        {
            return Array.Empty<TodoTask>();
        }

        if (perTopicOrdered.Count == 1)
        {
            return OrderForRag(perTopicOrdered[0]).Take(totalLimit).ToList();
        }

        var quota = Math.Max(1, totalLimit / perTopicOrdered.Count);
        var picked = new Dictionary<Guid, TodoTask>();

        foreach (var topicList in perTopicOrdered)
        {
            foreach (var todo in OrderForRag(topicList).Take(quota))
            {
                picked[todo.ExternalId] = todo;
            }
        }

        var leftovers = perTopicOrdered
            .SelectMany(list => list)
            .Where(t => !picked.ContainsKey(t.ExternalId));

        foreach (var todo in OrderForRag(leftovers))
        {
            if (picked.Count >= totalLimit)
            {
                break;
            }

            picked[todo.ExternalId] = todo;
        }

        return OrderForRag(picked.Values).Take(totalLimit).ToList();
    }

    /// <summary>
    /// Topic queries for retrieval. Advice questions with "A and B" split so both domains are searched.
    /// </summary>
    public IReadOnlyList<string> TopicSearchQueries()
    {
        var effective = EffectiveSearchQuery;
        if (string.IsNullOrWhiteSpace(effective))
        {
            return Array.Empty<string>();
        }

        if (!IsAdviceQuestion)
        {
            return new[] { effective };
        }

        var parts = Regex.Split(effective, @"\s+and\s+|\s*&\s*", RegexOptions.IgnoreCase)
            .Select(p => p.Trim())
            .Where(p => p.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return parts.Count >= 2 ? parts : new[] { effective };
    }

    /// <summary>
    /// Open-ended advice ("what should I work next?") with no real domain topic —
    /// skip semantic search and rank the user's open todos instead.
    /// </summary>
    public bool ShouldUseGlobalAdviceFallback()
    {
        if (!IsAdviceQuestion)
        {
            return false;
        }

        var topics = TopicSearchQueries();
        if (topics.Count == 0)
        {
            return true;
        }

        return topics.All(IsWeakAdviceTopic);
    }

    public static bool IsWeakAdviceTopic(string? topic)
    {
        var t = (topic ?? string.Empty).Trim();
        if (t.Length == 0)
        {
            return true;
        }

        if (t.Length <= 2)
        {
            return true;
        }

        if (WeakAdviceTopics.Contains(t))
        {
            return true;
        }

        // Multi-word leftovers that are still filler (e.g. "work next")
        var tokens = Regex.Split(t.ToLowerInvariant(), @"\s+")
            .Where(x => x.Length > 0)
            .ToArray();
        return tokens.Length > 0 && tokens.All(tok => WeakAdviceTopics.Contains(tok) || tok.Length <= 2);
    }

    private static readonly HashSet<string> WeakAdviceTopics = new(StringComparer.OrdinalIgnoreCase)
    {
        "work", "next", "something", "anything", "stuff", "things",
        "item", "items", "todo", "todos", "task", "tasks", "now", "today",
        "please", "me", "my", "focus"
    };

    /// <summary>
    /// Meta/planning todos that describe collecting work rather than doing domain work.
    /// </summary>
    public static bool IsMetaPlanningTask(string? title, string? description)
    {
        var blob = $"{title} {description}".ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(blob))
        {
            return false;
        }

        return ContainsAny(
            blob,
            "sprint capacity",
            "optimizer demo",
            "for optimizer",
            "collect high/medium incomplete",
            "collect high/medium",
            "incomplete auth and payments",
            "auth and payments work for",
            "story-point estimate field for next sprint");
    }

    public static bool IsMetaPlanningTask(TodoTask todo) =>
        IsMetaPlanningTask(todo.Title, todo.Description);

    /// <summary>Deterministic advice list helper (tests / fallbacks). Live Ask uses LLM with pre-sorted context.</summary>
    public static string BuildDeterministicAdviceAnswer(
        IReadOnlyList<TodoTask> todos,
        DateTime? utcToday = null)
    {
        var today = (utcToday ?? DateTime.UtcNow).Date;
        if (todos.Count == 0)
        {
            return "No matching open tasks found for that advice question.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("Suggested next work (High priority first, then earliest due — overdue stays in focus):");
        sb.AppendLine();
        var i = 1;
        foreach (var t in todos)
        {
            var due = t.DueDate.Date;
            var overdueTag = due < today && !RagTemporalQuery.IsCompleted(t) ? " · overdue" : string.Empty;
            sb.Append(i++);
            sb.Append(". ");
            sb.Append(string.IsNullOrWhiteSpace(t.Title) ? "(untitled)" : t.Title);
            sb.Append(" (");
            sb.Append(t.ExternalId);
            sb.Append(") — due ");
            sb.Append(due.ToString("yyyy-MM-dd"));
            sb.Append(" · ");
            sb.Append(string.IsNullOrWhiteSpace(t.Priority) ? "—" : t.Priority);
            sb.Append(" · ");
            sb.Append(string.IsNullOrWhiteSpace(t.Status) ? "—" : t.Status);
            sb.Append(overdueTag);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    public static int PriorityRank(string? priority) =>
        (priority ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "high" => 0,
            "medium" => 1,
            "low" => 2,
            _ => 3
        };

    private static string? DetectPriority(string text)
    {
        if (ContainsAny(text, "high priority", "highest priority", "any high", "critical", "urgent"))
        {
            return "High";
        }

        if (ContainsAny(text, "medium priority", "any medium"))
        {
            return "Medium";
        }

        if (ContainsAny(text, "low priority", "any low"))
        {
            return "Low";
        }

        return null;
    }

    private static string BuildSearchQuery(string original)
    {
        var q = original.Trim();

        foreach (var phrase in TemporalStripPhrases
                     .Concat(PriorityStripPhrases)
                     .Concat(StatusStripPhrases)
                     .Concat(LeadInStripPhrases)
                     .OrderByDescending(p => p.Length))
        {
            q = ReplaceIgnoreCase(q, phrase, " ");
        }

        q = Regex.Replace(q, @"[?!.]+", " ");
        q = Regex.Replace(q, @"\s+", " ").Trim();

        // Trailing standalone "next" (advice), not "next week" (already stripped).
        if (q.EndsWith(" next", StringComparison.OrdinalIgnoreCase))
        {
            q = q[..^5].Trim();
        }

        if (string.IsNullOrWhiteSpace(q))
        {
            return original.Trim();
        }

        // Avoid returning only stopwords like "tasks" / "for"
        var meaningful = Regex.Replace(q, @"\b(tasks?|todos?|for|my|the|a|an|about|related|to|on|in|of)\b",
            " ", RegexOptions.IgnoreCase);
        meaningful = Regex.Replace(meaningful, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(meaningful) ? q : meaningful;
    }

    private static string ReplaceIgnoreCase(string input, string oldValue, string newValue)
    {
        if (string.IsNullOrEmpty(oldValue))
        {
            return input;
        }

        var idx = input.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
        while (idx >= 0)
        {
            input = input.Remove(idx, oldValue.Length).Insert(idx, newValue);
            idx = input.IndexOf(oldValue, Math.Min(idx + newValue.Length, input.Length),
                StringComparison.OrdinalIgnoreCase);
        }

        return input;
    }

    private static bool ContainsAny(string text, params string[] phrases) =>
        phrases.Any(p => text.Contains(p, StringComparison.Ordinal));

    private static readonly string[] TemporalStripPhrases =
    [
        "following week", "coming week", "previous week", "past week", "next week", "last week",
        "this week", "current week", "due this week", "for this week",
        "following month", "coming month", "previous month", "past month", "next month", "last month",
        "this month", "current month", "due this month", "for this month",
        "tomorrow", "yesterday", "overdue", "today"
    ];

    private static readonly string[] PriorityStripPhrases =
    [
        "highest priority", "high priority", "medium priority", "low priority",
        "any critical", "any high", "any medium", "any low",
        "critical", "urgent"
    ];

    private static readonly string[] StatusStripPhrases =
    [
        "already done", "completed", "complete", "finished", "closed", "pending", "done"
    ];

    private static readonly string[] LeadInStripPhrases =
    [
        "which tasks are about", "which tasks", "what tasks are about", "what tasks",
        "what should i work on for", "what should i work on", "what should we work on",
        "what should i", "what should we",
        "list my", "show me", "tell me",
        "tasks about", "tasks related to", "tasks matching",
        "here are", "recommend", "prioritize", "prioritise", "suggest",
        "matching your criteria", "related to",
        "list", "show"
    ];
}
