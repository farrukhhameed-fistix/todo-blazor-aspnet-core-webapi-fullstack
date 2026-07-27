#nullable enable

using FluentValidation;
using Fistix.TaskManager.ViewModel.Commands.Todos;

namespace Fistix.TaskManager.ViewModel.Validators.Todos;

public class ImportTodoTasksFromCsvCommandValidator : AbstractValidator<ImportTodoTasksFromCsvCommand>
{
    public ImportTodoTasksFromCsvCommandValidator()
    {
        RuleFor(x => x.CsvContent)
            .NotEmpty()
            .WithMessage("CSV content is required.");

        RuleFor(x => x.ImportTag)
            .MaximumLength(100)
            .When(x => !string.IsNullOrWhiteSpace(x.ImportTag));
    }
}

public class DeleteImportedTodosCommandValidator : AbstractValidator<DeleteImportedTodosCommand>
{
    public DeleteImportedTodosCommandValidator()
    {
        RuleFor(x => x.ImportTag)
            .NotEmpty()
            .MaximumLength(100);
    }
}

public class StartAiBatchJobCommandValidator : AbstractValidator<StartAiBatchJobCommand>
{
    public StartAiBatchJobCommandValidator()
    {
        RuleFor(x => x.BatchSize)
            .InclusiveBetween(1, 50);

        RuleFor(x => x.DelayMsBetweenItems)
            .InclusiveBetween(0, 60_000);

        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.ImportTag)
                       || (x.TodoExternalIds is { Count: > 0 }))
            .WithMessage("Provide ImportTag or TodoExternalIds.");
    }
}

public class PauseAiBatchJobCommandValidator : AbstractValidator<PauseAiBatchJobCommand>
{
    public PauseAiBatchJobCommandValidator()
    {
        RuleFor(x => x.JobExternalId).NotEmpty();
    }
}

public class ContinueAiBatchJobCommandValidator : AbstractValidator<ContinueAiBatchJobCommand>
{
    public ContinueAiBatchJobCommandValidator()
    {
        RuleFor(x => x.JobExternalId).NotEmpty();
    }
}

public class CancelAiBatchJobCommandValidator : AbstractValidator<CancelAiBatchJobCommand>
{
    public CancelAiBatchJobCommandValidator()
    {
        RuleFor(x => x.JobExternalId).NotEmpty();
    }
}
