using System;
using System.Collections.Generic;
using System.Text;
using Fistix.TaskManager.ViewModel.Enums;

namespace Fistix.TaskManager.ViewModel.Dtos
{
  public class TodoTaskDto
  {
    public Guid Id { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public DateTime DueDate { get; set; }
    public TodoTaskStatus Status { get; set; }
  }
}
