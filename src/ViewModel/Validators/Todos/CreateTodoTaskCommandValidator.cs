using Fistix.TaskManager.ViewModel.Commands.Todos;
using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace Fistix.TaskManager.ViewModel.Validators.Todos
{
  public class CreateTodoTaskCommandValidator : AbstractValidator<CreateTodoTaskCommand>
  {
    public CreateTodoTaskCommandValidator()
    {
      RuleFor(x => x.Title).NotEmpty();
      RuleFor(x => x.DueDate).NotEmpty().GreaterThan(DateTime.Now).WithMessage("Due Date should be future date");
    }
  }
}
