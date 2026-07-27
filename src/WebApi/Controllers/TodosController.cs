using Fistix.TaskManager.Core.Exceptions;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.ViewModel.Dtos;
using Fistix.TaskManager.ViewModel.Queries.Todos;
using Fistix.TaskManager.WebApi.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<TodosController> _logger;

    public TodosController(IMediator mediator, ILogger<TodosController> logger)
    {
      _mediator = mediator;
      _logger = logger;
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

        return Created($"api/todos/{result.Payload.ExternalId}", result.Payload);
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
      catch (ForbiddenAccessException)
      {
        return Forbid();
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error creating todo task");
        return ApiErrorResponses.UnexpectedError(HttpContext, "Failed to create task");
      }
    }

    [HttpGet]
    [ProducesResponseType(typeof(GetAllTodoTasksQueryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllTodoTasks()
    {
      try
      {
        var result = await _mediator.Send(new GetAllTodoTasksQuery());
        return Ok(result);
      }
      catch (ForbiddenAccessException)
      {
        return Forbid();
      }
    }

    [HttpPut("{externalId}")]
    [ProducesResponseType(typeof(TodoTaskDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateTask(Guid externalId, [FromBody] UpdateTodoTaskCommand command)
    {
      if (!ModelState.IsValid)
        return BadRequest(ModelState);

      if (externalId != command.ExternalId)
        return BadRequest(new ProblemDetails { Detail = "Route id does not match request body." });

      try
      {
        var result = await _mediator.Send(command);
        return Ok(result.Payload);
      }
      catch (Core.Exceptions.NotFoundException)
      {
        return NotFound();
      }
      catch (ForbiddenAccessException)
      {
        return Forbid();
      }
      catch (BadHttpRequestException ex)
      {
        return BadRequest(new ProblemDetails { Detail = ex.Message });
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error updating todo task {ExternalId}", externalId);
        return ApiErrorResponses.UnexpectedError(HttpContext, "Failed to update task");
      }
    }

    /// <summary>
    /// Imports todos from CSV without creating AI metadata or enqueueing AI work.
    /// </summary>
    [HttpPost("import/csv")]
    [ProducesResponseType(typeof(TodoCsvImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ImportCsv(
      IFormFile? file,
      [FromForm] string? importTag,
      [FromForm] bool dryRun = false,
      [FromForm] bool replaceExistingTag = false)
    {
      if (file is null || file.Length == 0)
      {
        return BadRequest(new ProblemDetails { Detail = "CSV file is required." });
      }

      try
      {
        await using var stream = file.OpenReadStream();
        using var reader = new System.IO.StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        var result = await _mediator.Send(new ImportTodoTasksFromCsvCommand
        {
          CsvContent = content,
          ImportTag = importTag,
          DryRun = dryRun,
          ReplaceExistingTag = replaceExistingTag
        });

        return Ok(result.Payload);
      }
      catch (ForbiddenAccessException)
      {
        return Forbid();
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error importing todos from CSV");
        return ApiErrorResponses.UnexpectedError(HttpContext, "Failed to import CSV");
      }
    }

    [HttpGet("imports")]
    [ProducesResponseType(typeof(List<TodoImportBatchDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetImportBatches()
    {
      try
      {
        var result = await _mediator.Send(new GetTodoImportBatchesQuery());
        return Ok(result.Payload);
      }
      catch (ForbiddenAccessException)
      {
        return Forbid();
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error listing todo import batches");
        return ApiErrorResponses.UnexpectedError(HttpContext, "Failed to list imports");
      }
    }

    [HttpDelete("import/{importTag}")]
    [ProducesResponseType(typeof(DeleteImportedTodosResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteImported(string importTag)
    {
      if (string.IsNullOrWhiteSpace(importTag))
      {
        return BadRequest(new ProblemDetails { Detail = "Import tag is required." });
      }

      try
      {
        var result = await _mediator.Send(new DeleteImportedTodosCommand { ImportTag = importTag });
        return Ok(result.Payload);
      }
      catch (ForbiddenAccessException)
      {
        return Forbid();
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error deleting imported todos for tag {ImportTag}", importTag);
        return ApiErrorResponses.UnexpectedError(HttpContext, "Failed to delete imported todos");
      }
    }
  }
}