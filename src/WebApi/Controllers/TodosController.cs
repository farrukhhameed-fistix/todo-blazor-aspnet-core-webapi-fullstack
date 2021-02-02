using Fistix.TaskManager.Core.SecurityModel;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.ViewModel.Dtos;
using Fistix.TaskManager.ViewModel.Queries.Todos;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fistix.TaskManager.WebApi.Controllers
{
  [Route("api/[controller]")]
  [ApiController]
  [Authorize]
  public class TodosController : ControllerBase
  {
    private readonly IMediator _mediator;

    public TodosController(IMediator mediator)
    {
      _mediator = mediator;
    }

    [HttpPost]
    [ProducesResponseType(typeof(TodoTaskDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateTask([FromBody] CreateTodoTaskCommand command)
    {
      if (!ModelState.IsValid)
        return BadRequest(ModelState);

      try
      {       
        var result = await _mediator.Send(command);

        return Created($"api/todos/{result.Payload.Id}", result.Payload);
      }
      catch (BadHttpRequestException ex)
      {
        return BadRequest(new ProblemDetails()
        {
          Detail = ex.Message
        });
      }      
      catch (InvalidOperationException ex)
      {
        return Conflict(new ProblemDetails()
        {
          Detail = ex.Message
        });
      }      
    }

    [HttpGet]
    [Authorize(PolicyNames.IsAdmin)]
    [ProducesResponseType(typeof(GetAllTodoTasksQueryResult), StatusCodes.Status200OK)]    
    public async Task<IActionResult> GetAllTodoTasks()
    {      
      var result = await _mediator.Send(new GetAllTodoTasksQuery());

      return Ok(result);
    }
  }
}
