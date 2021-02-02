using Fistix.TaskManager.ViewModel.Dtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace Fistix.TaskManager.ViewModel.Queries.Todos
{
  public class GetAllTodoTasksQueryResult
  {
    public List<TodoTaskDto> Payload { get; set; }
  }
}
