using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.ViewModel.Dtos;
using Fistix.TaskManager.ViewModel.Queries.Todos;
using Fistix.TaskManager.WebApp.Models;
using Fistix.TaskManager.WebApp.Services.DataServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;

namespace Fistix.TaskManager.WebApp.Services.StateServices
{
  public class TodoStateService
  {
    private BehaviorSubject<List<TodoTaskDto>> _todoSubject;
    private Subject<ApiCallResult<string>> _apiCallResult;
    private TodoDataService _todoDataService;

    public TodoStateService(TodoDataService todoDataService)
    {
      _todoDataService = todoDataService;
      _todoSubject = new BehaviorSubject<List<TodoTaskDto>>(new List<TodoTaskDto>());
      _apiCallResult = new Subject<ApiCallResult<string>>();

      GetAllTodos();
    }

    public IObservable<List<TodoTaskDto>> TodoTasksObservable
    {
      get
      {
        return _todoSubject;
      }
    }
    public IObservable<ApiCallResult<string>> ApiCallResultObservable
    {
      get
      {
        return _apiCallResult;
      }
    }

    public async void GetAllTodos()
    {
      var tasks = await _todoDataService.GetAll();
      _todoSubject.OnNext(tasks);

      _apiCallResult.OnNext(new ApiCallResult<string>()
      {
        Operation = nameof(GetAllTodoTasksQuery),
        IsSucceed = true
      });
    }
    public async void CreateTodo(CreateTodoTaskCommand command)
    {
      var apiCallResult = await _todoDataService.Post(command);
      if (apiCallResult.IsSucceed)
      {
        var tasks = new List<TodoTaskDto>(_todoSubject.Value);
        tasks.Add(apiCallResult.Payload);

        _todoSubject.OnNext(tasks);
      }

      _apiCallResult.OnNext(new ApiCallResult<string>()
      {
        IsSucceed = apiCallResult.IsSucceed,
        Operation = nameof(CreateTodoTaskCommand),
        Message = apiCallResult.Message
      });
    }
  }
}
