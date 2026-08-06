#nullable enable

using Fistix.TaskManager.AiLayer.Abstractions;
using Fistix.TaskManager.AiLayer.Implementations;
using Fistix.TaskManager.AiLayer.Models;
using Fistix.TaskManager.AiLayer.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fistix.TaskManager.AiLayer.Tests;

public class RAGPipelineFaithfulnessTests
{
    private sealed class FakeLlm : ILlmProviderService
    {
        public string? LastPrompt { get; private set; }
        public string Response { get; set; } = "ok";
        public int CallCount { get; private set; }

        public Task<string> GetCompletionAsync(string prompt, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastPrompt = prompt;
            return Task.FromResult(Response);
        }
    }

    [Fact]
    public async Task EmptySources_DoesNotCallLlm()
    {
        var llm = new FakeLlm { Response = "should not be used" };
        var pipeline = new RAGPipeline(llm, new AiConfiguration { Provider = "ollama" }, NullLogger<RAGPipeline>.Instance);

        var result = await pipeline.ExecuteAsync(new RagPipelineRequest
        {
            Question = "What is due this week?",
            Context = "week",
            SourceTodos = []
        });

        Assert.Equal(0, llm.CallCount);
        Assert.Equal(LlmOutputValidator.InsufficientContextMessage, result.Answer);
    }

    [Fact]
    public async Task UngroundedGuid_ReplacesAnswer()
    {
        var sourceId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var foreign = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var llm = new FakeLlm { Response = $"See todo {foreign} for details." };
        var pipeline = new RAGPipeline(llm, new AiConfiguration { Provider = "ollama" }, NullLogger<RAGPipeline>.Instance);

        var result = await pipeline.ExecuteAsync(new RagPipelineRequest
        {
            Question = "What should I do?",
            Context = "workload",
            SourceTodos =
            [
                new RagSourceTodo
                {
                    ExternalId = sourceId,
                    Title = "Real task",
                    Description = "desc",
                    Priority = "High",
                    Status = "Pending",
                    DueDate = DateTime.UtcNow
                }
            ]
        });

        Assert.Equal(1, llm.CallCount);
        Assert.Equal(LlmOutputValidator.UngroundedAnswerMessage, result.Answer);
        Assert.Contains(sourceId, result.SourceTodoIds);
    }

    [Fact]
    public async Task GroundedAnswer_PassesThrough()
    {
        var sourceId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var llm = new FakeLlm { Response = $"Task {sourceId} titled Real task is high priority." };
        var pipeline = new RAGPipeline(llm, new AiConfiguration { Provider = "ollama" }, NullLogger<RAGPipeline>.Instance);

        var result = await pipeline.ExecuteAsync(new RagPipelineRequest
        {
            Question = "Priorities?",
            Context = "workload",
            SourceTodos =
            [
                new RagSourceTodo
                {
                    ExternalId = sourceId,
                    Title = "Real task",
                    Description = "desc",
                    Priority = "High",
                    Status = "Pending",
                    DueDate = DateTime.UtcNow
                }
            ]
        });

        Assert.Contains(sourceId.ToString(), result.Answer, StringComparison.OrdinalIgnoreCase);
    }
}
