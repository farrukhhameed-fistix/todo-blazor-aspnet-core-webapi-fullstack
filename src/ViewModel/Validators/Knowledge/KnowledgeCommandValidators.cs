using Fistix.TaskManager.ViewModel.Commands.Knowledge;
using FluentValidation;

namespace Fistix.TaskManager.ViewModel.Validators.Knowledge;

public class UploadKnowledgeDocumentCommandValidator : AbstractValidator<UploadKnowledgeDocumentCommand>
{
    public UploadKnowledgeDocumentCommandValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(260);
        RuleFor(x => x.Content).NotEmpty();
    }
}

public class DeleteKnowledgeDocumentCommandValidator : AbstractValidator<DeleteKnowledgeDocumentCommand>
{
    public DeleteKnowledgeDocumentCommandValidator()
    {
        RuleFor(x => x.DocumentExternalId).NotEmpty();
    }
}

public class KnowledgeQueryCommandValidator : AbstractValidator<KnowledgeQueryCommand>
{
    public KnowledgeQueryCommandValidator()
    {
        RuleFor(x => x.Question).NotEmpty().MaximumLength(2000);
    }
}
