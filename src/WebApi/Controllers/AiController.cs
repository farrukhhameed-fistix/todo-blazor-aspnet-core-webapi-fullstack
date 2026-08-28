using Fistix.TaskManager.Core.Exceptions;
using Fistix.TaskManager.Core.SecurityModel;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.ViewModel.Dtos;
using Fistix.TaskManager.ViewModel.Queries.Todos;
using Fistix.TaskManager.WebApi.Extensions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Fistix.TaskManager.WebApi.Controllers;

/// <summary>
/// API endpoints for AI features (summarization, classification, etc).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AiController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<AiController> _logger;

    public AiController(IMediator mediator, ILogger<AiController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// Generates an AI summary for a task description.
    /// Title and description are loaded from the database; only the task id and force flag are required.
    /// </summary>
    [HttpPost("summarize")]
    [EnableRateLimiting(RateLimitPolicies.AiSummarize)]
    [ProducesResponseType(typeof(TaskSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<TaskSummaryDto>> Summarize([FromBody] SummarizeTodoTaskCommand command)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            _logger.LogInformation("Summarization request for todo {TodoExternalId}", command.TodoExternalId);

            var result = await _mediator.Send(command);
            return Ok(result.Payload);
        }
        catch (FeatureDisabledException ex)
        {
            _logger.LogWarning(ex, "Summarization feature disabled for todo {TodoExternalId}", command.TodoExternalId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "AI summarization is unavailable",
                Detail = ex.Message,
                Status = StatusCodes.Status503ServiceUnavailable
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid summarization request for todo {TodoExternalId}", command.TodoExternalId);
            return BadRequest(ex.Message);
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating summary for todo {TodoExternalId}", command.TodoExternalId);
            return ApiErrorResponses.UnexpectedError(HttpContext, "Failed to generate summary");
        }
    }

    /// <summary>
    /// Reads current classification status from stored metadata (no LLM call).
    /// </summary>
    [HttpGet("classify/{todoExternalId:guid}")]
    [ProducesResponseType(typeof(TaskClassificationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TaskClassificationDto>> GetClassification(Guid todoExternalId)
    {
        try
        {
            var result = await _mediator.Send(new GetTaskClassificationQuery { TodoExternalId = todoExternalId });
            return Ok(result.Payload);
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Runs or retries AI classification (rate-limited). Use GET for status polling.
    /// </summary>
    [HttpPost("classify")]
    [EnableRateLimiting(RateLimitPolicies.AiClassify)]
    [ProducesResponseType(typeof(TaskClassificationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<TaskClassificationDto>> Classify([FromBody] ClassifyTodoTaskCommand command)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            _logger.LogDebug("Classification retry for todo {TodoExternalId}, force={Force}",
                command.TodoExternalId, command.Force);

            var result = await _mediator.Send(command);
            return Ok(result.Payload);
        }
        catch (FeatureDisabledException ex)
        {
            _logger.LogWarning(ex, "Classification feature disabled for todo {TodoExternalId}", command.TodoExternalId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "AI classification is unavailable",
                Detail = ex.Message,
                Status = StatusCodes.Status503ServiceUnavailable
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid classification request for todo {TodoExternalId}", command.TodoExternalId);
            return BadRequest(ex.Message);
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error classifying todo {TodoExternalId}", command.TodoExternalId);
            return ApiErrorResponses.UnexpectedError(HttpContext, "Failed to classify task priority");
        }
    }

    /// <summary>
    /// Applies the AI-suggested priority to the task.
    /// </summary>
    [HttpPost("apply-priority")]
    [ProducesResponseType(typeof(TodoTaskDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<TodoTaskDto>> ApplyPriority([FromBody] ApplyAiPriorityCommand command)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var result = await _mediator.Send(command);
            return Ok(result.Payload);
        }
        catch (FeatureDisabledException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "AI classification is unavailable",
                Detail = ex.Message,
                Status = StatusCodes.Status503ServiceUnavailable
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying AI priority for todo {TodoExternalId}", command.TodoExternalId);
            return ApiErrorResponses.UnexpectedError(HttpContext, "Failed to apply AI priority");
        }
    }

    /// <summary>
    /// Finds todos by semantic similarity to a natural-language query.
    /// </summary>
    [HttpPost("todos/search/semantic")]
    [EnableRateLimiting(RateLimitPolicies.AiSemanticSearch)]
    [ProducesResponseType(typeof(SemanticSearchResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SemanticSearchResponseDto>> SemanticSearch(
        [FromBody] SemanticSearchTodosCommand command)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var result = await _mediator.Send(command);
            return Ok(result.Payload);
        }
        catch (FeatureDisabledException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "AI semantic search is unavailable",
                Detail = ex.Message,
                Status = StatusCodes.Status503ServiceUnavailable
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running semantic search");
            return ApiErrorResponses.UnexpectedError(HttpContext, "Failed to run semantic search");
        }
    }

    /// <summary>
    /// Answers a natural-language question about the user's tasks using RAG.
    /// </summary>
    [HttpPost("query")]
    [EnableRateLimiting(RateLimitPolicies.AiRag)]
    [ProducesResponseType(typeof(AiQueryResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AiQueryResponseDto>> Query([FromBody] AiQueryCommand command)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var result = await _mediator.Send(command);
            return Ok(result.Payload);
        }
        catch (FeatureDisabledException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "AI query is unavailable",
                Detail = ex.Message,
                Status = StatusCodes.Status503ServiceUnavailable
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running AI query");
            return ApiErrorResponses.UnexpectedError(HttpContext, "Failed to answer AI query");
        }
    }

    /// <summary>
    /// Proposes tool calls from a natural-language prompt (does not execute).
    /// </summary>
    [HttpPost("propose-tools")]
    [EnableRateLimiting(RateLimitPolicies.AiFunctionCalling)]
    [ProducesResponseType(typeof(ProposeAiToolsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ProposeAiToolsResponseDto>> ProposeTools([FromBody] ProposeAiToolsCommand command)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var result = await _mediator.Send(command);
            return Ok(result.Payload);
        }
        catch (FeatureDisabledException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "AI function calling is unavailable",
                Detail = ex.Message,
                Status = StatusCodes.Status503ServiceUnavailable
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proposing AI tools");
            return ApiErrorResponses.UnexpectedError(HttpContext, "Failed to propose AI tools");
        }
    }

    /// <summary>
    /// Client capture options for hold-to-talk (WebM + Web Speech vs local PCM captions).
    /// </summary>
    [HttpGet("voice-options")]
    [ProducesResponseType(typeof(VoiceTranscriptionOptionsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<VoiceTranscriptionOptionsDto>> GetVoiceOptions()
    {
        var result = await _mediator.Send(new GetVoiceTranscriptionOptionsQuery());
        return Ok(result.Payload);
    }

    /// <summary>
    /// Transcribes push-to-talk audio via local STT (does not create todos).
    /// </summary>
    [HttpPost("transcribe")]
    [EnableRateLimiting(RateLimitPolicies.AiTranscribe)]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [ProducesResponseType(typeof(TranscribeAudioResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<TranscribeAudioResponseDto>> Transcribe(
        IFormFile file,
        [FromServices] IValidator<TranscribeAudioCommand> validator)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails { Detail = "Audio file is required." });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            using var memory = new System.IO.MemoryStream();
            await stream.CopyToAsync(memory);

            var command = new TranscribeAudioCommand
            {
                AudioContent = memory.ToArray(),
                ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                    ? "application/octet-stream"
                    : file.ContentType,
                FileName = string.IsNullOrWhiteSpace(file.FileName) ? "audio.webm" : file.FileName,
                ContextHint = Request.Form.TryGetValue("contextHint", out var contextHint)
                    ? contextHint.ToString()
                    : null
            };

            var validation = await validator.ValidateAsync(command);
            if (!validation.IsValid)
            {
                foreach (var error in validation.Errors)
                {
                    ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
                }

                return BadRequest(ModelState);
            }

            var result = await _mediator.Send(command);
            return Ok(result.Payload);
        }
        catch (FeatureDisabledException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "AI voice transcription is unavailable",
                Detail = ex.Message,
                Status = StatusCodes.Status503ServiceUnavailable
            });
        }
        catch (SpeechToTextUnavailableException ex)
        {
            Response.Headers.RetryAfter = Math.Max(1, ex.RetryAfterSeconds).ToString();
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "Speech model is preparing",
                Detail = ex.Message,
                Status = StatusCodes.Status503ServiceUnavailable
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid transcription request",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transcribing audio");
            return ApiErrorResponses.UnexpectedError(HttpContext, "Failed to transcribe audio");
        }
    }

    /// <summary>
    /// Executes user-confirmed AI tool calls.
    /// </summary>
    [HttpPost("execute-tools")]
    [EnableRateLimiting(RateLimitPolicies.AiFunctionCalling)]
    [ProducesResponseType(typeof(ExecuteAiToolsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ExecuteAiToolsResponseDto>> ExecuteTools([FromBody] ExecuteAiToolsCommand command)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var result = await _mediator.Send(command);
            return Ok(result.Payload);
        }
        catch (FeatureDisabledException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "AI function calling is unavailable",
                Detail = ex.Message,
                Status = StatusCodes.Status503ServiceUnavailable
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing AI tools");
            return ApiErrorResponses.UnexpectedError(HttpContext, "Failed to execute AI tools");
        }
    }

    /// <summary>
    /// Starts an async sprint optimizer job. Progress is pushed over SignalR; poll get/active for status.
    /// </summary>
    [HttpPost("agent/sprint-optimizer")]
    [EnableRateLimiting(RateLimitPolicies.AiAgents)]
    [ProducesResponseType(typeof(SprintOptimizerJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SprintOptimizerJobDto>> OptimizeSprint(
        [FromBody] OptimizeSprintCommand command)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var result = await _mediator.Send(command);
            return Ok(result.Payload);
        }
        catch (FeatureDisabledException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "AI sprint optimizer is unavailable",
                Detail = ex.Message,
                Status = StatusCodes.Status503ServiceUnavailable
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails { Title = "Cannot start sprint optimizer", Detail = ex.Message });
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting sprint optimizer agent");
            return ApiErrorResponses.UnexpectedError(HttpContext, "Failed to start sprint optimizer");
        }
    }

    [HttpGet("agent/sprint-optimizer/active")]
    [ProducesResponseType(typeof(SprintOptimizerJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<SprintOptimizerJobDto>> GetActiveSprintOptimizer()
    {
        try
        {
            var result = await _mediator.Send(new GetActiveSprintOptimizerJobQuery());
            if (result.Payload is null)
            {
                return NoContent();
            }

            return Ok(result.Payload);
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("agent/sprint-optimizer/{jobExternalId:guid}")]
    [ProducesResponseType(typeof(SprintOptimizerJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SprintOptimizerJobDto>> GetSprintOptimizer(Guid jobExternalId)
    {
        try
        {
            var result = await _mediator.Send(new GetSprintOptimizerJobQuery { JobExternalId = jobExternalId });
            return Ok(result.Payload);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("agent/sprint-optimizer/{jobExternalId:guid}/cancel")]
    [ProducesResponseType(typeof(SprintOptimizerJobDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SprintOptimizerJobDto>> CancelSprintOptimizer(Guid jobExternalId)
    {
        try
        {
            var result = await _mediator.Send(new CancelSprintOptimizerJobCommand { JobExternalId = jobExternalId });
            return Ok(result.Payload);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("agent/sprint-optimizer/{jobExternalId:guid}/approve")]
    [EnableRateLimiting(RateLimitPolicies.AiAgents)]
    [ProducesResponseType(typeof(SprintOptimizerJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SprintOptimizerJobDto>> ApproveSprintOptimizerProposal(
        Guid jobExternalId,
        [FromBody] ApproveSprintOptimizerProposalCommand command)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        command.JobExternalId = jobExternalId;

        try
        {
            var result = await _mediator.Send(command);
            return Ok(result.Payload);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails { Title = "Cannot approve sprint proposal", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving sprint optimizer proposal {JobId}", jobExternalId);
            return ApiErrorResponses.UnexpectedError(HttpContext, "Failed to approve sprint proposal");
        }
    }

    [HttpPost("agent/sprint-optimizer/{jobExternalId:guid}/reject")]
    [EnableRateLimiting(RateLimitPolicies.AiAgents)]
    [ProducesResponseType(typeof(SprintOptimizerJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SprintOptimizerJobDto>> RejectSprintOptimizerProposal(Guid jobExternalId)
    {
        try
        {
            var result = await _mediator.Send(new RejectSprintOptimizerProposalCommand { JobExternalId = jobExternalId });
            return Ok(result.Payload);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails { Title = "Cannot reject sprint proposal", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting sprint optimizer proposal {JobId}", jobExternalId);
            return ApiErrorResponses.UnexpectedError(HttpContext, "Failed to reject sprint proposal");
        }
    }

    /// <summary>
    /// Starts a durable AI batch job (embedding → classify → summarize) with pause/continue/cancel.
    /// </summary>
    [HttpPost("batch")]
    [ProducesResponseType(typeof(AiBatchJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AiBatchJobDto>> StartBatch([FromBody] StartAiBatchJobCommand command)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var result = await _mediator.Send(command);
            return Ok(result.Payload);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails { Title = "Cannot start batch job", Detail = ex.Message });
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting AI batch job");
            return ApiErrorResponses.UnexpectedError(HttpContext, "Failed to start AI batch job");
        }
    }

    [HttpGet("batch/active")]
    [ProducesResponseType(typeof(AiBatchJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<AiBatchJobDto>> GetActiveBatch()
    {
        try
        {
            var result = await _mediator.Send(new GetActiveAiBatchJobQuery());
            if (result.Payload is null)
            {
                return NoContent();
            }

            return Ok(result.Payload);
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("batch/{jobExternalId:guid}")]
    [ProducesResponseType(typeof(AiBatchJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AiBatchJobDto>> GetBatch(Guid jobExternalId)
    {
        try
        {
            var result = await _mediator.Send(new GetAiBatchJobQuery { JobExternalId = jobExternalId });
            return Ok(result.Payload);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("batch/{jobExternalId:guid}/pause")]
    [ProducesResponseType(typeof(AiBatchJobDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AiBatchJobDto>> PauseBatch(Guid jobExternalId)
    {
        try
        {
            var result = await _mediator.Send(new PauseAiBatchJobCommand { JobExternalId = jobExternalId });
            return Ok(result.Payload);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("batch/{jobExternalId:guid}/continue")]
    [ProducesResponseType(typeof(AiBatchJobDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AiBatchJobDto>> ContinueBatch(Guid jobExternalId)
    {
        try
        {
            var result = await _mediator.Send(new ContinueAiBatchJobCommand { JobExternalId = jobExternalId });
            return Ok(result.Payload);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("batch/{jobExternalId:guid}/cancel")]
    [ProducesResponseType(typeof(AiBatchJobDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AiBatchJobDto>> CancelBatch(Guid jobExternalId)
    {
        try
        {
            var result = await _mediator.Send(new CancelAiBatchJobCommand { JobExternalId = jobExternalId });
            return Ok(result.Payload);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
    }
}
