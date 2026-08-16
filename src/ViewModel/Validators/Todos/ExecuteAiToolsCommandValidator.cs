using Fistix.TaskManager.ViewModel.Commands.Todos;
using FluentValidation;
using System;
using System.Linq;

namespace Fistix.TaskManager.ViewModel.Validators.Todos;

public class ExecuteAiToolsCommandValidator : AbstractValidator<ExecuteAiToolsCommand>
{
    private static readonly string[] AllowedTools =
    [
        "create_todo",
        "update_todo",
        "mark_complete",
        "set_priority",
        "search_todos",
        "get_statistics",
        "set_semantic_search",
        "open_todo",
        "close_todo",
        "start_edit",
        "cancel_edit",
        "save_edit",
        "regenerate_summary",
        "regenerate_priority",
        "apply_suggested_priority"
    ];

    public ExecuteAiToolsCommandValidator()
    {
        RuleFor(x => x.ConfirmedCalls)
            .NotNull()
            .Must(calls => calls.Count > 0)
            .WithMessage("At least one confirmed tool call is required.")
            .Must(calls => calls.Count <= 20)
            .WithMessage("A maximum of 20 tool calls can be executed at once.");

        RuleForEach(x => x.ConfirmedCalls).ChildRules(call =>
        {
            call.RuleFor(c => c.ToolName)
                .NotEmpty()
                .Must(name => AllowedTools.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase))
                .WithMessage("Unknown or disallowed tool name.");

            call.RuleFor(c => c.Arguments).NotNull();
        });
    }
}
