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
