using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.ViewModel.Dtos;
using Fistix.TaskManager.ViewModel.Queries.Todos;
using Fistix.TaskManager.WebApp.Extentions;
using Fistix.TaskManager.WebApp.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Fistix.TaskManager.WebApp.Services.DataServices
{
  public class TodoDataService
  {
    private HttpClient _httpClient;

    public TodoDataService(HttpClient httpClient)
    {
      _httpClient = httpClient;
    }

    public async Task<List<TodoTaskDto>> GetAll()
    {
      var result = await _httpClient.GetFromJsonAsync<GetAllTodoTasksQueryResult>("api/todos");
      return result.Payload;
    }

    public async Task<ApiCallResult<TodoTaskDto>> Post(CreateTodoTaskCommand command)
    {
      ApiCallResult<TodoTaskDto> result = new ApiCallResult<TodoTaskDto>();

      var response = await _httpClient.PostAsJsonAsync<CreateTodoTaskCommand>("api/todos", command);
      if (response.IsSuccessStatusCode)
      {
        result.Payload = await response.Content.ReadFromJsonAsync<TodoTaskDto>();
        result.IsSucceed = true;
      }
      else
      {
        result.IsSucceed = false;
        result.Message = await response.GetErrorMessage();
      }

      return result;
    }

    public async Task<ApiCallResult<TodoTaskDto>> Put(UpdateTodoTaskCommand command)
    {
      var result = new ApiCallResult<TodoTaskDto>();
      var response = await _httpClient.PutAsJsonAsync($"api/todos/{command.ExternalId}", command);

      if (response.IsSuccessStatusCode)
      {
        result.Payload = await response.Content.ReadFromJsonAsync<TodoTaskDto>();
        result.IsSucceed = true;
      }
      else
      {
        result.IsSucceed = false;
        result.Message = await response.GetErrorMessage();
      }

      return result;
    }

    public async Task<ApiCallResult<TaskSummaryDto>> Summarize(Guid todoExternalId, bool force = false)
    {
      var result = new ApiCallResult<TaskSummaryDto>();
      var command = new SummarizeTodoTaskCommand
      {
        TodoExternalId = todoExternalId,
        Force = force
      };

      var response = await _httpClient.PostAsJsonAsync("api/ai/summarize", command);
      if (response.IsSuccessStatusCode)
      {
        result.Payload = await response.Content.ReadFromJsonAsync<TaskSummaryDto>();
        result.IsSucceed = true;
      }
      else
      {
        result.IsSucceed = false;
        result.Message = await response.GetErrorMessage();
      }

      return result;
    }

    public async Task<ApiCallResult<TaskClassificationDto>> GetClassification(Guid todoExternalId)
    {
      var result = new ApiCallResult<TaskClassificationDto>();
      var response = await _httpClient.GetAsync($"api/ai/classify/{todoExternalId}");

      if (response.IsSuccessStatusCode)
      {
        result.Payload = await response.Content.ReadFromJsonAsync<TaskClassificationDto>();
        result.IsSucceed = true;
      }
      else
      {
        result.IsSucceed = false;
        result.Message = await response.GetErrorMessage();
      }

      return result;
    }

    public async Task<ApiCallResult<TaskClassificationDto>> Classify(Guid todoExternalId, bool force = false)
    {
      var result = new ApiCallResult<TaskClassificationDto>();
      var command = new ClassifyTodoTaskCommand
      {
        TodoExternalId = todoExternalId,
        Force = force
      };

      var response = await _httpClient.PostAsJsonAsync("api/ai/classify", command);
      if (response.IsSuccessStatusCode)
      {
        result.Payload = await response.Content.ReadFromJsonAsync<TaskClassificationDto>();
        result.IsSucceed = true;
      }
      else
      {
        result.IsSucceed = false;
        result.Message = await response.GetErrorMessage();
      }

      return result;
    }

    public async Task<ApiCallResult<TodoTaskDto>> ApplyAiPriority(Guid todoExternalId)
    {
      var result = new ApiCallResult<TodoTaskDto>();
      var command = new ApplyAiPriorityCommand { TodoExternalId = todoExternalId };

      var response = await _httpClient.PostAsJsonAsync("api/ai/apply-priority", command);
      if (response.IsSuccessStatusCode)
      {
        result.Payload = await response.Content.ReadFromJsonAsync<TodoTaskDto>();
        result.IsSucceed = true;
      }
      else
      {
        result.IsSucceed = false;
        result.Message = await response.GetErrorMessage();
      }

      return result;
    }

    public async Task<ApiCallResult<SemanticSearchResponseDto>> SemanticSearch(string query, int limit = 10)
    {
      var result = new ApiCallResult<SemanticSearchResponseDto>();
      var command = new SemanticSearchTodosCommand
      {
        Query = query,
        Limit = limit
      };

      var response = await _httpClient.PostAsJsonAsync("api/ai/todos/search/semantic", command);
      if (response.IsSuccessStatusCode)
      {
        result.Payload = await response.Content.ReadFromJsonAsync<SemanticSearchResponseDto>();
        result.IsSucceed = true;
      }
      else
      {
        result.IsSucceed = false;
        result.Message = await response.GetErrorMessage();
      }

      return result;
    }

    public async Task<ApiCallResult<AiQueryResponseDto>> AiQuery(string question)
    {
      var result = new ApiCallResult<AiQueryResponseDto>();
      var command = new AiQueryCommand
      {
        Question = question
      };

      var response = await _httpClient.PostAsJsonAsync("api/ai/query", command);
      if (response.IsSuccessStatusCode)
      {
        result.Payload = await response.Content.ReadFromJsonAsync<AiQueryResponseDto>();
        result.IsSucceed = true;
      }
      else
      {
        result.IsSucceed = false;
        result.Message = await response.GetErrorMessage();
      }

      return result;
    }

    public async Task<ApiCallResult<ProposeAiToolsResponseDto>> ProposeAiTools(string prompt)
    {
      var result = new ApiCallResult<ProposeAiToolsResponseDto>();
      var command = new ProposeAiToolsCommand { Prompt = prompt };

      var response = await _httpClient.PostAsJsonAsync("api/ai/propose-tools", command);
      if (response.IsSuccessStatusCode)
      {
        result.Payload = await response.Content.ReadFromJsonAsync<ProposeAiToolsResponseDto>();
        result.IsSucceed = true;
      }
      else
      {
        result.IsSucceed = false;
        result.Message = await response.GetErrorMessage();
      }

      return result;
    }

    public async Task<ApiCallResult<TranscribeAudioResponseDto>> TranscribeAudio(
      Stream fileStream,
      string fileName,
      string contentType)
    {
      var result = new ApiCallResult<TranscribeAudioResponseDto>();
      using var content = new MultipartFormDataContent();
      var streamContent = new StreamContent(fileStream);
      if (!string.IsNullOrWhiteSpace(contentType))
      {
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
      }

      content.Add(streamContent, "file", fileName);

      var response = await _httpClient.PostAsync("api/ai/transcribe", content);
      if (response.IsSuccessStatusCode)
      {
        result.Payload = await response.Content.ReadFromJsonAsync<TranscribeAudioResponseDto>();
        result.IsSucceed = true;
      }
      else
      {
        result.IsSucceed = false;
        result.Message = await response.GetErrorMessage();
      }

      return result;
    }

    public async Task<ApiCallResult<ExecuteAiToolsResponseDto>> ExecuteAiTools(List<ProposedToolCallDto> confirmedCalls)
    {
      var result = new ApiCallResult<ExecuteAiToolsResponseDto>();
      var command = new ExecuteAiToolsCommand { ConfirmedCalls = confirmedCalls };

      var response = await _httpClient.PostAsJsonAsync("api/ai/execute-tools", command);
      if (response.IsSuccessStatusCode)
      {
        result.Payload = await response.Content.ReadFromJsonAsync<ExecuteAiToolsResponseDto>();
        result.IsSucceed = true;
      }
      else
      {
        result.IsSucceed = false;
        result.Message = await response.GetErrorMessage();
      }

      return result;
    }

    public async Task<ApiCallResult<SprintOptimizerJobDto>> OptimizeSprint(
      int maxTasks = 12,
      int durationDays = 14,
      string? name = null)
    {
      var result = new ApiCallResult<SprintOptimizerJobDto>();
      var command = new OptimizeSprintCommand
      {
        MaxTasks = maxTasks,
        DurationDays = durationDays,
        Name = name
      };

      var response = await _httpClient.PostAsJsonAsync("api/ai/agent/sprint-optimizer", command);
      if (response.IsSuccessStatusCode)
      {
        result.Payload = await response.Content.ReadFromJsonAsync<SprintOptimizerJobDto>();
        result.IsSucceed = true;
      }
      else
      {
        result.IsSucceed = false;
        result.Message = await response.GetErrorMessage();
      }

      return result;
    }

    public async Task<ApiCallResult<SprintOptimizerJobDto?>> GetActiveSprintOptimizer()
    {
      var result = new ApiCallResult<SprintOptimizerJobDto?>();
      var response = await _httpClient.GetAsync("api/ai/agent/sprint-optimizer/active");
      if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
      {
        result.IsSucceed = true;
        result.Payload = null;
        return result;
      }

      if (response.IsSuccessStatusCode)
      {
        result.Payload = await response.Content.ReadFromJsonAsync<SprintOptimizerJobDto>();
        result.IsSucceed = true;
      }
      else
      {
        result.IsSucceed = false;
        result.Message = await response.GetErrorMessage();
      }

      return result;
    }

    public async Task<ApiCallResult<SprintOptimizerJobDto>> GetSprintOptimizer(Guid jobExternalId)
    {
      var result = new ApiCallResult<SprintOptimizerJobDto>();
      var response = await _httpClient.GetAsync($"api/ai/agent/sprint-optimizer/{jobExternalId}");
      if (response.IsSuccessStatusCode)
      {
        result.Payload = await response.Content.ReadFromJsonAsync<SprintOptimizerJobDto>();
        result.IsSucceed = true;
      }
      else
      {
        result.IsSucceed = false;
        result.Message = await response.GetErrorMessage();
      }

      return result;
    }

    public async Task<ApiCallResult<SprintOptimizerJobDto>> CancelSprintOptimizer(Guid jobExternalId)
    {
      var result = new ApiCallResult<SprintOptimizerJobDto>();
      var response = await _httpClient.PostAsync($"api/ai/agent/sprint-optimizer/{jobExternalId}/cancel", null);
      if (response.IsSuccessStatusCode)
      {
        result.Payload = await response.Content.ReadFromJsonAsync<SprintOptimizerJobDto>();
        result.IsSucceed = true;
      }
      else
      {
        result.IsSucceed = false;
        result.Message = await response.GetErrorMessage();
      }

      return result;
    }

    public async Task<ApiCallResult<List<SprintDto>>> GetSprints()
    {
      var result = new ApiCallResult<List<SprintDto>>();
      var response = await _httpClient.GetAsync("api/sprints");
      if (response.IsSuccessStatusCode)
      {
        result.Payload = await response.Content.ReadFromJsonAsync<List<SprintDto>>() ?? new List<SprintDto>();
        result.IsSucceed = true;
      }
      else
      {
        result.IsSucceed = false;
        result.Message = await response.GetErrorMessage();
      }

      return result;
    }

    public async Task<ApiCallResult<SprintDto>> GetSprint(Guid externalId)
    {
      var result = new ApiCallResult<SprintDto>();
      var response = await _httpClient.GetAsync($"api/sprints/{externalId}");
      if (response.IsSuccessStatusCode)
      {
        result.Payload = await response.Content.ReadFromJsonAsync<SprintDto>();
        result.IsSucceed = true;
      }
      else
      {
        result.IsSucceed = false;
        result.Message = await response.GetErrorMessage();
      }

      return result;
    }

    public async Task<ApiCallResult<TodoCsvImportResultDto>> ImportCsv(
      Stream fileStream,
      string fileName,
      string? importTag,
      bool dryRun,
      bool replaceExistingTag)
    {
      var result = new ApiCallResult<TodoCsvImportResultDto>();
      using var content = new MultipartFormDataContent();
      var streamContent = new StreamContent(fileStream);
      content.Add(streamContent, "file", fileName);
      if (!string.IsNullOrWhiteSpace(importTag))
      {
        content.Add(new StringContent(importTag), "importTag");
      }

      content.Add(new StringContent(dryRun ? "true" : "false"), "dryRun");
      content.Add(new StringContent(replaceExistingTag ? "true" : "false"), "replaceExistingTag");

      var response = await _httpClient.PostAsync("api/todos/import/csv", content);
      if (response.IsSuccessStatusCode)
      {
        result.Payload = await response.Content.ReadFromJsonAsync<TodoCsvImportResultDto>();
        result.IsSucceed = true;
      }
      else
      {
        result.IsSucceed = false;
        result.Message = await response.GetErrorMessage();
      }

      return result;
    }

    public async Task<ApiCallResult<DeleteImportedTodosResultDto>> DeleteImported(string importTag)
    {
      var result = new ApiCallResult<DeleteImportedTodosResultDto>();
      var response = await _httpClient.DeleteAsync($"api/todos/import/{Uri.EscapeDataString(importTag)}");
      if (response.IsSuccessStatusCode)
      {
        result.Payload = await response.Content.ReadFromJsonAsync<DeleteImportedTodosResultDto>();
        result.IsSucceed = true;
      }
      else
      {
        result.IsSucceed = false;
        result.Message = await response.GetErrorMessage();
      }

      return result;
    }

    public async Task<ApiCallResult<List<TodoImportBatchDto>>> GetImportBatches()
    {
      var result = new ApiCallResult<List<TodoImportBatchDto>>();
      var response = await _httpClient.GetAsync("api/todos/imports");
      if (response.IsSuccessStatusCode)
      {
        result.Payload = await response.Content.ReadFromJsonAsync<List<TodoImportBatchDto>>() ?? [];
        result.IsSucceed = true;
      }
      else
      {
        result.IsSucceed = false;
        result.Message = await response.GetErrorMessage();
      }

      return result;
    }

    public async Task<ApiCallResult<AiBatchJobDto>> StartAiBatch(StartAiBatchJobCommand command)
    {
      var result = new ApiCallResult<AiBatchJobDto>();
      var response = await _httpClient.PostAsJsonAsync("api/ai/batch", command);
      if (response.IsSuccessStatusCode)
      {
        result.Payload = await response.Content.ReadFromJsonAsync<AiBatchJobDto>();
        result.IsSucceed = true;
      }
      else
      {
        result.IsSucceed = false;
        result.Message = await response.GetErrorMessage();
      }

      return result;
    }

    public async Task<ApiCallResult<AiBatchJobDto?>> GetActiveAiBatch()
    {
      var result = new ApiCallResult<AiBatchJobDto?>();
      var response = await _httpClient.GetAsync("api/ai/batch/active");
      if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
      {
        result.IsSucceed = true;
        result.Payload = null;
        return result;
      }

      if (response.IsSuccessStatusCode)
      {
        result.Payload = await response.Content.ReadFromJsonAsync<AiBatchJobDto>();
        result.IsSucceed = true;
      }
      else
      {
        result.IsSucceed = false;
        result.Message = await response.GetErrorMessage();
      }

      return result;
    }

    public async Task<ApiCallResult<AiBatchJobDto>> GetAiBatch(Guid jobExternalId)
    {
      var result = new ApiCallResult<AiBatchJobDto>();
      var response = await _httpClient.GetAsync($"api/ai/batch/{jobExternalId}");
      if (response.IsSuccessStatusCode)
      {
        result.Payload = await response.Content.ReadFromJsonAsync<AiBatchJobDto>();
        result.IsSucceed = true;
      }
      else
      {
        result.IsSucceed = false;
        result.Message = await response.GetErrorMessage();
      }

      return result;
    }

    public async Task<ApiCallResult<AiBatchJobDto>> PauseAiBatch(Guid jobExternalId) =>
      await PostBatchAction($"api/ai/batch/{jobExternalId}/pause");

    public async Task<ApiCallResult<AiBatchJobDto>> ContinueAiBatch(Guid jobExternalId) =>
      await PostBatchAction($"api/ai/batch/{jobExternalId}/continue");

    public async Task<ApiCallResult<AiBatchJobDto>> CancelAiBatch(Guid jobExternalId) =>
      await PostBatchAction($"api/ai/batch/{jobExternalId}/cancel");

    private async Task<ApiCallResult<AiBatchJobDto>> PostBatchAction(string url)
    {
      var result = new ApiCallResult<AiBatchJobDto>();
      var response = await _httpClient.PostAsync(url, null);
      if (response.IsSuccessStatusCode)
      {
        result.Payload = await response.Content.ReadFromJsonAsync<AiBatchJobDto>();
        result.IsSucceed = true;
      }
      else
      {
        result.IsSucceed = false;
        result.Message = await response.GetErrorMessage();
      }

      return result;
    }
  }
}
