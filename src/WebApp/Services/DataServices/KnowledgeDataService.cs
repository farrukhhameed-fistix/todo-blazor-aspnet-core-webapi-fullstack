#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.ViewModel.Commands.Knowledge;
using Fistix.TaskManager.ViewModel.Dtos;
using Fistix.TaskManager.WebApp.Extentions;
using Fistix.TaskManager.WebApp.Models;

namespace Fistix.TaskManager.WebApp.Services.DataServices;

public sealed class KnowledgeDataService
{
    private readonly HttpClient _httpClient;

    public KnowledgeDataService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ApiCallResult<KnowledgeUploadResultDto>> UploadAsync(
        Stream fileStream,
        string fileName,
        string? contentType)
    {
        var result = new ApiCallResult<KnowledgeUploadResultDto>();
        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(fileStream);
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        }

        content.Add(streamContent, "file", fileName);
        var response = await _httpClient.PostAsync("api/ai/knowledge/documents", content);
        if (response.IsSuccessStatusCode)
        {
            result.Payload = await response.Content.ReadFromJsonAsync<KnowledgeUploadResultDto>();
            result.IsSucceed = true;
        }
        else
        {
            result.IsSucceed = false;
            result.Message = await response.GetErrorMessage();
        }

        return result;
    }

    public async Task<ApiCallResult<List<KnowledgeDocumentDto>>> ListDocumentsAsync()
    {
        var result = new ApiCallResult<List<KnowledgeDocumentDto>>();
        var response = await _httpClient.GetAsync("api/ai/knowledge/documents");
        if (response.IsSuccessStatusCode)
        {
            result.Payload = await response.Content.ReadFromJsonAsync<List<KnowledgeDocumentDto>>() ?? [];
            result.IsSucceed = true;
        }
        else
        {
            result.IsSucceed = false;
            result.Message = await response.GetErrorMessage();
        }

        return result;
    }

    public async Task<ApiCallResult<List<KnowledgeChunkDto>>> ListChunksAsync(Guid documentExternalId)
    {
        var result = new ApiCallResult<List<KnowledgeChunkDto>>();
        var response = await _httpClient.GetAsync($"api/ai/knowledge/documents/{documentExternalId}/chunks");
        if (response.IsSuccessStatusCode)
        {
            result.Payload = await response.Content.ReadFromJsonAsync<List<KnowledgeChunkDto>>() ?? [];
            result.IsSucceed = true;
        }
        else
        {
            result.IsSucceed = false;
            result.Message = await response.GetErrorMessage();
        }

        return result;
    }

    public async Task<ApiCallResult<KnowledgeIngestJobDto>> GetIngestJobAsync(Guid jobExternalId)
    {
        var result = new ApiCallResult<KnowledgeIngestJobDto>();
        var response = await _httpClient.GetAsync($"api/ai/knowledge/ingest/{jobExternalId}");
        if (response.IsSuccessStatusCode)
        {
            result.Payload = await response.Content.ReadFromJsonAsync<KnowledgeIngestJobDto>();
            result.IsSucceed = true;
        }
        else
        {
            result.IsSucceed = false;
            result.Message = await response.GetErrorMessage();
        }

        return result;
    }

    public async Task<ApiCallResult<bool>> DeleteDocumentAsync(Guid documentExternalId)
    {
        var result = new ApiCallResult<bool>();
        var response = await _httpClient.DeleteAsync($"api/ai/knowledge/documents/{documentExternalId}");
        if (response.IsSuccessStatusCode)
        {
            result.Payload = true;
            result.IsSucceed = true;
        }
        else
        {
            result.IsSucceed = false;
            result.Message = await response.GetErrorMessage();
        }

        return result;
    }

    public async Task<ApiCallResult<KnowledgeQueryResponseDto>> QueryAsync(
        string question,
        Guid? documentExternalId,
        CancellationToken cancellationToken = default)
    {
        var result = new ApiCallResult<KnowledgeQueryResponseDto>();
        var command = new KnowledgeQueryCommand
        {
            Question = question,
            DocumentExternalId = documentExternalId
        };

        var response = await _httpClient.PostAsJsonAsync("api/ai/knowledge/query", command, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            result.Payload = await response.Content.ReadFromJsonAsync<KnowledgeQueryResponseDto>(cancellationToken);
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
