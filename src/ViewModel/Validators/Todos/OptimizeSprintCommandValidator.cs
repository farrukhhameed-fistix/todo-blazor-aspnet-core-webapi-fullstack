#nullable enable

using Fistix.TaskManager.ViewModel.Commands.Todos;
using FluentValidation;

namespace Fistix.TaskManager.ViewModel.Validators.Todos;

public class OptimizeSprintCommandValidator : AbstractValidator<OptimizeSprintCommand>
{
    public OptimizeSprintCommandValidator()
    {
        RuleFor(x => x.MaxTasks).InclusiveBetween(1, 50);
        RuleFor(x => x.DurationDays).InclusiveBetween(1, 90);
        RuleFor(x => x.Name).MaximumLength(200).When(x => !string.IsNullOrWhiteSpace(x.Name));
    }
}

public class CancelSprintOptimizerJobCommandValidator : AbstractValidator<CancelSprintOptimizerJobCommand>
{
    public CancelSprintOptimizerJobCommandValidator()
    {
        RuleFor(x => x.JobExternalId).NotEmpty();
    }
}

public class ApproveSprintOptimizerProposalCommandValidator : AbstractValidator<ApproveSprintOptimizerProposalCommand>
{
    public ApproveSprintOptimizerProposalCommandValidator()
    {
        RuleFor(x => x.JobExternalId).NotEmpty();
        RuleForEach(x => x.SelectedTaskExternalIds).NotEmpty();
        RuleFor(x => x.SelectedTaskExternalIds)
            .Must(ids => ids.Count <= 50)
            .When(x => x.SelectedTaskExternalIds.Count > 0);
    }
}

public class RejectSprintOptimizerProposalCommandValidator : AbstractValidator<RejectSprintOptimizerProposalCommand>
{
    public RejectSprintOptimizerProposalCommandValidator()
    {
        RuleFor(x => x.JobExternalId).NotEmpty();
    }
}
