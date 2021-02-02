using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace Fistix.TaskManager.ViewModel.Queries.Todos
{
  public class GetAllTodoTasksQuery : IRequest<GetAllTodoTasksQueryResult>
  {
  }
}
