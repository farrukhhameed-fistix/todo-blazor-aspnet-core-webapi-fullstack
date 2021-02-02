using AutoMapper;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.ViewModel.Dtos;
using System;

namespace Fistix.TaskManager.Core.AutoMapperProfiles
{
  public class TodoTaskProfileMapping : Profile
  {
    public TodoTaskProfileMapping()
    {
      CreateMap<CreateTodoTaskCommand, TodoTask>()
                .ForMember(up => up.Id, m => m.Ignore())
                .ForMember(up => up.CreatedOn, m => m.MapFrom(x => DateTime.Now));

      CreateMap<TodoTask, TodoTaskDto>();
    }
  }
}
