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
            TodoToolDefinitions.MarkComplete => RequireGuid(args, "id"),
            TodoToolDefinitions.SetPriority => ValidateSetPriority(args),
            TodoToolDefinitions.SearchTodos => ValidateSearch(args),
            TodoToolDefinitions.GetStatistics => ToolArgumentValidationResult.Ok(),
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
        var idResult = RequireGuid(args, "id");
        if (!idResult.IsValid)
        {
            return idResult;
        }

        var title = GetString(args, "title");
        if (title is not null && title.Length > LlmInputLimits.TitleMaxLength)
        {
            return ToolArgumentValidationResult.Fail($"Argument 'title' exceeds {LlmInputLimits.TitleMaxLength} characters.");
        }

        var description = GetString(args, "description");
        if (description is not null && description.Length > LlmInputLimits.DescriptionMaxLength)
        {
            return ToolArgumentValidationResult.Fail(
                $"Argument 'description' exceeds {LlmInputLimits.DescriptionMaxLength} characters.");
        }

        var status = GetString(args, "status");
        if (status is not null && !IsAllowedStatus(status))
        {
            return ToolArgumentValidationResult.Fail(
                "Argument 'status' must be one of: Pending, InProgress, Completed.");
        }

        var priority = GetString(args, "priority");
        if (priority is not null)
        {
            _ = ClassificationGuardrails.NormalizePriority(priority);
        }

        if (GetString(args, "dueDate") is { Length: > 0 } dueRaw &&
            !DateTime.TryParse(dueRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
        {
            return ToolArgumentValidationResult.Fail("Argument 'dueDate' must be a valid date/time.");
        }

        return ToolArgumentValidationResult.Ok();
    }

    private static ToolArgumentValidationResult ValidateSetPriority(Dictionary<string, JsonElement> args)
    {
        var idResult = RequireGuid(args, "id");
        if (!idResult.IsValid)
        {
            return idResult;
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
        if (string.IsNullOrWhiteSpace(query))
        {
            return ToolArgumentValidationResult.Fail("Missing required argument 'query'.");
        }

        if (query.Length > LlmInputLimits.ToolSearchQueryMaxLength)
        {
            return ToolArgumentValidationResult.Fail(
                $"Argument 'query' exceeds {LlmInputLimits.ToolSearchQueryMaxLength} characters.");
        }

        return ToolArgumentValidationResult.Ok();
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
