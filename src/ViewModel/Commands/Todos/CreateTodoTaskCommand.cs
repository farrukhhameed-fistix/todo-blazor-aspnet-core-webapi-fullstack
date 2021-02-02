using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace Fistix.TaskManager.ViewModel.Commands.Todos
{
  public class CreateTodoTaskCommand : IRequest<CreateTodoTaskCommandResult>
  {
    public string Title { get; set; }
    public string Description { get; set; }
    public DateTime DueDate { get; set; }
  }
}
