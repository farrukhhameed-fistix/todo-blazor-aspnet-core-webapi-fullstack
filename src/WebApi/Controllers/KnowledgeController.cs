#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Fistix.TaskManager.Core.Exceptions;
using Fistix.TaskManager.Core.SecurityModel;
using Fistix.TaskManager.ViewModel.Commands.Knowledge;
using Fistix.TaskManager.ViewModel.Dtos;
using Fistix.TaskManager.ViewModel.Queries.Knowledge;
using Fistix.TaskManager.WebApi.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;

namespace Fistix.TaskManager.WebApi.Controllers;

[ApiController]
[Route("api/ai/knowledge")]
[Authorize]
public class KnowledgeController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<KnowledgeController> _logger;

    public KnowledgeController(IMediator mediator, ILogger<KnowledgeController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [HttpPost("documents")]
    [EnableRateLimiting(RateLimitPolicies.AiKnowledgeRag)]
    [ProducesResponseType(typeof(KnowledgeUploadResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Upload(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails { Detail = "A .txt, .md, or .pdf file is required." });
        }

        try
        {
            var extension = Path.GetExtension(file.FileName);
            var isPdf = string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase);

            UploadKnowledgeDocumentCommand command;
            if (isPdf)
            {
                await using var stream = file.OpenReadStream();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                command = new UploadKnowledgeDocumentCommand
                {
                    FileName = file.FileName,
                    ContentType = file.ContentType ?? "application/pdf",
                    BinaryContent = ms.ToArray()
                };
            }
            else
            {
                await using var stream = file.OpenReadStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var content = await reader.ReadToEndAsync();
                command = new UploadKnowledgeDocumentCommand
                {
                    FileName = file.FileName,
                    ContentType = file.ContentType ?? string.Empty,
                    Content = content
                };
            }

            var result = await _mediator.Send(command);
            return Ok(result.Payload);
        }
        catch (FeatureDisabledException ex)
        {
            return Unavailable("Knowledge Lab is unavailable", ex);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Detail = ex.Message });
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading knowledge document");
            return ApiErrorResponses.UnexpectedError(HttpContext, "Failed to upload document");
        }
    }

    [HttpGet("documents")]
    [ProducesResponseType(typeof(List<KnowledgeDocumentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ListDocuments()
    {
        try
        {
            var result = await _mediator.Send(new ListKnowledgeDocumentsQuery());
            return Ok(result.Payload);
        }
        catch (FeatureDisabledException ex)
        {
            return Unavailable("Knowledge Lab is unavailable", ex);
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing knowledge documents");
            return ApiErrorResponses.UnexpectedError(HttpContext, "Failed to list documents");
        }
    }

    [HttpGet("documents/{id:guid}")]
    [ProducesResponseType(typeof(KnowledgeDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetDocument(Guid id)
    {
        try
        {
            var result = await _mediator.Send(new GetKnowledgeDocumentQuery { DocumentExternalId = id });
            return Ok(result.Payload);
        }
        catch (FeatureDisabledException ex)
        {
            return Unavailable("Knowledge Lab is unavailable", ex);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading knowledge document {DocumentId}", id);
            return ApiErrorResponses.UnexpectedError(HttpContext, "Failed to load document");
        }
    }

    [HttpGet("documents/{id:guid}/chunks")]
    [ProducesResponseType(typeof(List<KnowledgeChunkDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ListChunks(Guid id)
    {
        try
        {
            var result = await _mediator.Send(new ListKnowledgeChunksQuery { DocumentExternalId = id });
            return Ok(result.Payload);
        }
        catch (FeatureDisabledException ex)
        {
            return Unavailable("Knowledge Lab is unavailable", ex);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing chunks for {DocumentId}", id);
            return ApiErrorResponses.UnexpectedError(HttpContext, "Failed to list chunks");
        }
    }

    [HttpDelete("documents/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> DeleteDocument(Guid id)
    {
        try
        {
            await _mediator.Send(new DeleteKnowledgeDocumentCommand { DocumentExternalId = id });
            return Ok();
        }
        catch (FeatureDisabledException ex)
        {
            return Unavailable("Knowledge Lab is unavailable", ex);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting knowledge document {DocumentId}", id);
            return ApiErrorResponses.UnexpectedError(HttpContext, "Failed to delete document");
        }
    }

    [HttpGet("ingest/{jobId:guid}")]
    [ProducesResponseType(typeof(KnowledgeIngestJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetIngestJob(Guid jobId)
    {
        try
        {
            var result = await _mediator.Send(new GetKnowledgeIngestJobQuery { JobExternalId = jobId });
            return Ok(result.Payload);
        }
        catch (FeatureDisabledException ex)
        {
            return Unavailable("Knowledge Lab is unavailable", ex);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading ingest job {JobId}", jobId);
            return ApiErrorResponses.UnexpectedError(HttpContext, "Failed to load ingest job");
        }
    }

    [HttpPost("query")]
    [EnableRateLimiting(RateLimitPolicies.AiKnowledgeRag)]
    [ProducesResponseType(typeof(KnowledgeQueryResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Query([FromBody] KnowledgeQueryCommand command)
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
            return Unavailable("Knowledge Lab is unavailable", ex);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (ForbiddenAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running knowledge RAG query");
            return ApiErrorResponses.UnexpectedError(HttpContext, "Failed to answer question");
        }
    }

    private ObjectResult Unavailable(string title, FeatureDisabledException ex) =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
        {
            Title = title,
            Detail = ex.Message,
            Status = StatusCodes.Status503ServiceUnavailable
        });
}
