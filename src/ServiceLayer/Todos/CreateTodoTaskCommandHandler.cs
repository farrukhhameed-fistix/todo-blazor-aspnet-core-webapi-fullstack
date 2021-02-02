using AutoMapper;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.ViewModel.Dtos;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Fistix.TaskManager.ServiceLayer.Todos
{
  public class CreateTodoTaskCommandHandler : IRequestHandler<CreateTodoTaskCommand, CreateTodoTaskCommandResult>
  {
    private readonly IMapper _mapper;
    private readonly ITodoTaskRepository _todoTaskRepository;

    public CreateTodoTaskCommandHandler(IMapper mapper, ITodoTaskRepository todoTaskRepository)
    {
      _mapper = mapper;
      _todoTaskRepository = todoTaskRepository;
    }

    public async Task<CreateTodoTaskCommandResult> Handle(CreateTodoTaskCommand command, CancellationToken cancellationToken)
    {
      var todoTask = _mapper.Map<TodoTask>(command);
      todoTask.GenerateNewId();
      await _todoTaskRepository.Create(todoTask, cancellationToken);
      todoTask = await _todoTaskRepository.Get(todoTask.Id, cancellationToken);

      return new CreateTodoTaskCommandResult()
      {
        Payload = _mapper.Map<TodoTaskDto>(todoTask)
      };
    }
  }
}
