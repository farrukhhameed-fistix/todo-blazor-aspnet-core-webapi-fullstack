#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Fistix.TaskManager.AiLayer.Tools;

/// <summary>
/// Canonical descriptions of todo management tools available for LLM function calling.
/// </summary>
public static class TodoToolDefinitions
{
    public const string CreateTodo = "create_todo";
    public const string UpdateTodo = "update_todo";
    public const string MarkComplete = "mark_complete";
    public const string SetPriority = "set_priority";
    public const string SearchTodos = "search_todos";
    public const string GetStatistics = "get_statistics";
    public const string SetSemanticSearch = "set_semantic_search";
    public const string OpenTodo = "open_todo";
    public const string CloseTodo = "close_todo";
    public const string StartEdit = "start_edit";
    public const string CancelEdit = "cancel_edit";
    public const string SaveEdit = "save_edit";
    public const string RegenerateSummary = "regenerate_summary";
    public const string RegeneratePriority = "regenerate_priority";
    public const string ApplySuggestedPriority = "apply_suggested_priority";

    public static readonly HashSet<string> AllowedToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        CreateTodo,
        UpdateTodo,
        MarkComplete,
        SetPriority,
        SearchTodos,
        GetStatistics,
        SetSemanticSearch,
        OpenTodo,
        CloseTodo,
        StartEdit,
        CancelEdit,
        SaveEdit,
        RegenerateSummary,
        RegeneratePriority,
        ApplySuggestedPriority
    };

    /// <summary>UI-only tools applied by the client; server execution is a no-op acknowledgment.</summary>
    public static readonly HashSet<string> UiOnlyToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        SetSemanticSearch,
        OpenTodo,
        CloseTodo,
        StartEdit,
        CancelEdit,
        SaveEdit,
        RegenerateSummary,
        RegeneratePriority,
        ApplySuggestedPriority
    };

    /// <summary>Safe/read/UI tools that clients may auto-apply without a confirm step.</summary>
    public static readonly HashSet<string> AutoApplyToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        SearchTodos,
        GetStatistics,
        SetSemanticSearch,
        OpenTodo,
        CloseTodo,
        StartEdit,
        CancelEdit,
        RegenerateSummary,
        RegeneratePriority
    };

    public static string BuildCatalogForPrompt()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Available tools (use exact tool names):");
        builder.AppendLine("- create_todo(title: string, description: string, priority?: High|Medium|Low, dueDate?: ISO-8601 datetime, category?: string)");
        builder.AppendLine("- update_todo(index?: number, id?: guid, title?: string, description?: string, priority?: High|Medium|Low, status?: string, dueDate?: ISO-8601 datetime) // omit index/id for the current open task");
        builder.AppendLine("- mark_complete(index?: number, id?: guid) // also use for delete/remove/done — marks completed (no hard delete)");
        builder.AppendLine("- set_priority(index?: number, id?: guid, priority: High|Medium|Low) // omit index/id for the current open task");
        builder.AppendLine("- search_todos(query?: string, semantic?: boolean, status?: Pending|InProgress|Completed, dueFrom?: YYYY-MM-DD, dueTo?: YYYY-MM-DD)");
        builder.AppendLine("  // query = topic keywords only. Put status/date constraints in status/dueFrom/dueTo. Omit all filters to show every task / clear search.");
        builder.AppendLine("- set_semantic_search(enabled: boolean)");
        builder.AppendLine("- get_statistics()");
        builder.AppendLine("- open_todo(index: number) // open details for visible grid row # (1-based)");
        builder.AppendLine("- close_todo() // close details/edit dialog");
        builder.AppendLine("- start_edit(index?: number) // open edit dialog for row # or current open task");
        builder.AppendLine("- cancel_edit() // cancel edit dialog");
        builder.AppendLine("- save_edit() // persist the open edit form; do not use this just to change a field");
        builder.AppendLine("- regenerate_summary(index?: number) // AI summary for row # or current open task");
        builder.AppendLine("- regenerate_priority(index?: number) // AI priority suggestion for row # or current open task");
        builder.AppendLine("- apply_suggested_priority(index?: number) // apply AI suggested priority");
        builder.AppendLine("Prefer index (visible grid row number) over id. Prefer one-shot update_todo/mark_complete/set_priority over open→edit→save unless the prompt says an edit form is already open — then patch fields with update_todo/set_priority and omit index.");
        return builder.ToString();
    }

    public static bool IsUiOnly(string? toolName) =>
        !string.IsNullOrWhiteSpace(toolName) && UiOnlyToolNames.Contains(toolName.Trim());

    public static bool IsAutoApply(string? toolName) =>
        !string.IsNullOrWhiteSpace(toolName) && AutoApplyToolNames.Contains(toolName.Trim());

    public static bool IsAllowed(string? toolName) =>
        !string.IsNullOrWhiteSpace(toolName) && AllowedToolNames.Contains(toolName.Trim());

    public static string NormalizeName(string toolName) =>
        AllowedToolNames.First(n => n.Equals(toolName.Trim(), StringComparison.OrdinalIgnoreCase));
}
