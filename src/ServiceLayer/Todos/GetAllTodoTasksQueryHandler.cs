using AutoMapper;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.ViewModel.Dtos;
using Fistix.TaskManager.ViewModel.Queries.Todos;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Fistix.TaskManager.ServiceLayer.Todos
{
  public class GetAllTodoTasksQueryHandler : IRequestHandler<GetAllTodoTasksQuery, GetAllTodoTasksQueryResult>
  {
    private readonly IMapper _mapper;
    private readonly ITodoTaskRepository _todoTaskRepository;

    public GetAllTodoTasksQueryHandler(IMapper mapper, ITodoTaskRepository todoTaskRepository)
    {
      _mapper = mapper;
      _todoTaskRepository = todoTaskRepository;
    }

    public async Task<GetAllTodoTasksQueryResult> Handle(GetAllTodoTasksQuery query, CancellationToken cancellationToken)
    {
      var tasks = await _todoTaskRepository.GetAll(cancellationToken);

      return new GetAllTodoTasksQueryResult()
      {
        Payload = _mapper.Map<List<TodoTaskDto>>(tasks)
      };
    }
  }
}