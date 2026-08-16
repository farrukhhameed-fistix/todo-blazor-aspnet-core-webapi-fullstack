#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Fistix.TaskManager.AiLayer.Tools;

namespace Fistix.TaskManager.AiLayer.Shared;

public sealed class ToolArgumentValidationResult
{
    public bool IsValid { get; init; }
    public string? Error { get; init; }

    public static ToolArgumentValidationResult Ok() => new() { IsValid = true };

    public static ToolArgumentValidationResult Fail(string error) =>
        new() { IsValid = false, Error = error };
}

/// <summary>Deterministic per-tool argument schema checks (allowlist of keys, types, lengths).</summary>
public static class ToolArgumentValidator
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Pending",
        "InProgress",
        "Completed"
    };

    public static bool IsAllowedStatus(string? status) =>
        !string.IsNullOrWhiteSpace(status) && AllowedStatuses.Contains(status.Trim());

    public static string NormalizeStatus(string status) =>
        AllowedStatuses.First(s => s.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));

    public static ToolArgumentValidationResult Validate(string toolName, Dictionary<string, JsonElement>? args)
    {
        args ??= new Dictionary<string, JsonElement>();

        if (!TodoToolDefinitions.IsAllowed(toolName))
        {
            return ToolArgumentValidationResult.Fail($"Tool '{toolName}' is not allowed.");
        }

        var normalized = TodoToolDefinitions.NormalizeName(toolName);
        return normalized switch
        {
            TodoToolDefinitions.CreateTodo => ValidateCreate(args),
            TodoToolDefinitions.UpdateTodo => ValidateUpdate(args),
            TodoToolDefinitions.MarkComplete => RequireIdOrIndex(args),
            TodoToolDefinitions.SetPriority => ValidateSetPriority(args),
            TodoToolDefinitions.SearchTodos => ValidateSearch(args),
            TodoToolDefinitions.GetStatistics => ToolArgumentValidationResult.Ok(),
            TodoToolDefinitions.SetSemanticSearch => ValidateSetSemanticSearch(args),
            TodoToolDefinitions.OpenTodo => RequireIndex(args),
            TodoToolDefinitions.CloseTodo => ToolArgumentValidationResult.Ok(),
            TodoToolDefinitions.StartEdit => ValidateOptionalIndex(args),
            TodoToolDefinitions.CancelEdit => ToolArgumentValidationResult.Ok(),
            TodoToolDefinitions.SaveEdit => ToolArgumentValidationResult.Ok(),
            TodoToolDefinitions.RegenerateSummary => ValidateOptionalIndex(args),
            TodoToolDefinitions.RegeneratePriority => ValidateOptionalIndex(args),
            TodoToolDefinitions.ApplySuggestedPriority => ValidateOptionalIndex(args),
            _ => ToolArgumentValidationResult.Fail($"Tool '{normalized}' is not implemented.")
        };
    }

    private static ToolArgumentValidationResult ValidateCreate(Dictionary<string, JsonElement> args)
    {
        var title = GetString(args, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return ToolArgumentValidationResult.Fail("Missing required argument 'title'.");
        }

        if (title.Length > LlmInputLimits.TitleMaxLength)
        {
            return ToolArgumentValidationResult.Fail($"Argument 'title' exceeds {LlmInputLimits.TitleMaxLength} characters.");
        }

        var description = GetString(args, "description");
        if (description is not null && description.Length > LlmInputLimits.DescriptionMaxLength)
        {
            return ToolArgumentValidationResult.Fail(
                $"Argument 'description' exceeds {LlmInputLimits.DescriptionMaxLength} characters.");
        }

        var category = GetString(args, "category");
        if (category is not null && category.Length > LlmInputLimits.CategoryMaxLength)
        {
            return ToolArgumentValidationResult.Fail(
                $"Argument 'category' exceeds {LlmInputLimits.CategoryMaxLength} characters.");
        }

        var priority = GetString(args, "priority");
        if (priority is not null)
        {
            var normalized = ClassificationGuardrails.NormalizePriority(priority);
            if (!string.Equals(normalized, "HIGH", StringComparison.Ordinal) &&
                !string.Equals(normalized, "MEDIUM", StringComparison.Ordinal) &&
                !string.Equals(normalized, "LOW", StringComparison.Ordinal))
            {
                return ToolArgumentValidationResult.Fail("Argument 'priority' must be High, Medium, or Low.");
            }
        }

        if (GetString(args, "dueDate") is { Length: > 0 } dueRaw &&
            !DateTime.TryParse(dueRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
        {
            return ToolArgumentValidationResult.Fail("Argument 'dueDate' must be a valid date/time.");
        }

        return ToolArgumentValidationResult.Ok();
    }

    private static ToolArgumentValidationResult ValidateUpdate(Dictionary<string, JsonElement> args)
    {
        var target = ValidateOptionalIdOrIndex(args);
        if (!target.IsValid)
        {
            return target;
        }

        var title = GetString(args, "title");
        var description = GetString(args, "description");
        var status = GetString(args, "status");
        var priority = GetString(args, "priority");
        var dueDate = GetString(args, "dueDate");

        var hasField =
            !string.IsNullOrWhiteSpace(title) ||
            !string.IsNullOrWhiteSpace(description) ||
            !string.IsNullOrWhiteSpace(status) ||
            !string.IsNullOrWhiteSpace(priority) ||
            !string.IsNullOrWhiteSpace(dueDate);

        if (!hasField)
        {
            return ToolArgumentValidationResult.Fail(
                "Provide at least one field to update (title, description, priority, status, dueDate).");
        }

        if (title is not null && title.Length > LlmInputLimits.TitleMaxLength)
        {
            return ToolArgumentValidationResult.Fail($"Argument 'title' exceeds {LlmInputLimits.TitleMaxLength} characters.");
        }

        if (description is not null && description.Length > LlmInputLimits.DescriptionMaxLength)
        {
            return ToolArgumentValidationResult.Fail(
                $"Argument 'description' exceeds {LlmInputLimits.DescriptionMaxLength} characters.");
        }

        if (status is not null && !IsAllowedStatus(status))
        {
            return ToolArgumentValidationResult.Fail(
                "Argument 'status' must be one of: Pending, InProgress, Completed.");
        }

        if (priority is not null)
        {
            _ = ClassificationGuardrails.NormalizePriority(priority);
        }

        if (dueDate is { Length: > 0 } &&
            !DateTime.TryParse(dueDate, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
        {
            return ToolArgumentValidationResult.Fail("Argument 'dueDate' must be a valid date/time.");
        }

        return ToolArgumentValidationResult.Ok();
    }

    private static ToolArgumentValidationResult ValidateSetPriority(Dictionary<string, JsonElement> args)
    {
        var target = ValidateOptionalIdOrIndex(args);
        if (!target.IsValid)
        {
            return target;
        }

        var priority = GetString(args, "priority");
        if (string.IsNullOrWhiteSpace(priority))
        {
            return ToolArgumentValidationResult.Fail("Missing required argument 'priority'.");
        }

        _ = ClassificationGuardrails.NormalizePriority(priority);
        return ToolArgumentValidationResult.Ok();
    }

    private static ToolArgumentValidationResult ValidateSearch(Dictionary<string, JsonElement> args)
    {
        var query = GetString(args, "query");
        var status = GetString(args, "status");
        var dueFrom = GetString(args, "dueFrom");
        var dueTo = GetString(args, "dueTo");

        var hasQuery = !string.IsNullOrWhiteSpace(query);
        var hasStatus = !string.IsNullOrWhiteSpace(status);
        var hasDueFrom = !string.IsNullOrWhiteSpace(dueFrom);
        var hasDueTo = !string.IsNullOrWhiteSpace(dueTo);

        // Empty args = show all / clear grid filters.
        if (hasQuery && query!.Length > LlmInputLimits.ToolSearchQueryMaxLength)
        {
            return ToolArgumentValidationResult.Fail(
                $"Argument 'query' exceeds {LlmInputLimits.ToolSearchQueryMaxLength} characters.");
        }

        if (hasStatus && !IsAllowedStatus(status))
        {
            return ToolArgumentValidationResult.Fail(
                "Argument 'status' must be one of: Pending, InProgress, Completed.");
        }

        if (hasDueFrom && !DateTime.TryParse(dueFrom, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
        {
            return ToolArgumentValidationResult.Fail("Argument 'dueFrom' must be a valid date (YYYY-MM-DD).");
        }

        if (hasDueTo && !DateTime.TryParse(dueTo, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
        {
            return ToolArgumentValidationResult.Fail("Argument 'dueTo' must be a valid date (YYYY-MM-DD).");
        }

        if (HasArg(args, "semantic") && !TryGetBool(args, "semantic", out _))
        {
            return ToolArgumentValidationResult.Fail("Argument 'semantic' must be a boolean when provided.");
        }

        return ToolArgumentValidationResult.Ok();
    }

    private static ToolArgumentValidationResult ValidateSetSemanticSearch(Dictionary<string, JsonElement> args)
    {
        if (!TryGetBool(args, "enabled", out _))
        {
            return ToolArgumentValidationResult.Fail("Missing or invalid required argument 'enabled' (boolean).");
        }

        return ToolArgumentValidationResult.Ok();
    }

    private static ToolArgumentValidationResult ValidateOptionalIndex(Dictionary<string, JsonElement> args)
    {
        if (!HasArg(args, "index"))
        {
            return ToolArgumentValidationResult.Ok();
        }

        return RequireIndex(args);
    }

    /// <summary>
    /// Id/index are optional so open-task voice commands can omit the target.
    /// When present they must still be valid.
    /// </summary>
    private static ToolArgumentValidationResult ValidateOptionalIdOrIndex(Dictionary<string, JsonElement> args)
    {
        var hasId = !string.IsNullOrWhiteSpace(GetString(args, "id"));
        var hasIndex = HasArg(args, "index");
        if (!hasId && !hasIndex)
        {
            return ToolArgumentValidationResult.Ok();
        }

        return RequireIdOrIndex(args);
    }

    private static ToolArgumentValidationResult RequireIdOrIndex(Dictionary<string, JsonElement> args)
    {
        var hasId = !string.IsNullOrWhiteSpace(GetString(args, "id"));
        var hasIndex = HasArg(args, "index");

        if (!hasId && !hasIndex)
        {
            return ToolArgumentValidationResult.Fail("Provide either 'id' (guid) or 'index' (1-based grid row).");
        }

        if (hasId)
        {
            var idResult = RequireGuid(args, "id");
            if (!idResult.IsValid)
            {
                return idResult;
            }
        }

        if (hasIndex)
        {
            var indexResult = RequireIndex(args);
            if (!indexResult.IsValid)
            {
                return indexResult;
            }
        }

        return ToolArgumentValidationResult.Ok();
    }

    private static ToolArgumentValidationResult RequireIndex(Dictionary<string, JsonElement> args)
    {
        if (!TryGetInt(args, "index", out var index) || index < 1)
        {
            return ToolArgumentValidationResult.Fail("Argument 'index' must be a positive integer (1-based grid row).");
        }

        return ToolArgumentValidationResult.Ok();
    }

    private static bool HasArg(Dictionary<string, JsonElement> args, string name) =>
        args.Keys.Any(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));

    private static bool TryGetInt(Dictionary<string, JsonElement> args, string name, out int value)
    {
        value = 0;
        foreach (var pair in args)
        {
            if (!string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            switch (pair.Value.ValueKind)
            {
                case JsonValueKind.Number when pair.Value.TryGetInt32(out value):
                    return true;
                case JsonValueKind.String when int.TryParse(pair.Value.GetString(), out value):
                    return true;
                default:
                    return false;
            }
        }

        return false;
    }

    private static bool TryGetBool(Dictionary<string, JsonElement> args, string name, out bool value)
    {
        value = false;
        foreach (var pair in args)
        {
            if (!string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            switch (pair.Value.ValueKind)
            {
                case JsonValueKind.True:
                    value = true;
                    return true;
                case JsonValueKind.False:
                    value = false;
                    return true;
                case JsonValueKind.String when bool.TryParse(pair.Value.GetString(), out var parsed):
                    value = parsed;
                    return true;
                default:
                    return false;
            }
        }

        return false;
    }

    private static ToolArgumentValidationResult RequireGuid(Dictionary<string, JsonElement> args, string name)
    {
        var raw = GetString(args, name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ToolArgumentValidationResult.Fail($"Missing required argument '{name}'.");
        }

        if (!Guid.TryParse(raw, out _))
        {
            return ToolArgumentValidationResult.Fail($"Argument '{name}' must be a valid GUID.");
        }

        return ToolArgumentValidationResult.Ok();
    }

    private static string? GetString(Dictionary<string, JsonElement> args, string name)
    {
        foreach (var pair in args)
        {
            if (!string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return pair.Value.ValueKind switch
            {
                JsonValueKind.String => pair.Value.GetString(),
                JsonValueKind.Number => pair.Value.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => pair.Value.ToString()
            };
        }

        return null;
    }
}
