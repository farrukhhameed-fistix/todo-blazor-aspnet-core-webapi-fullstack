#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.AiLayer.Abstractions;
using Fistix.TaskManager.AiLayer.Shared;
using Microsoft.Extensions.Logging;

namespace Fistix.TaskManager.ServiceLayer.Knowledge;

/// <summary>Rewrites a user question into a short search query for retrieval.</summary>
public sealed class KnowledgeQueryRewriter
{
    private readonly ILlmProviderService _llm;
    private readonly ILogger<KnowledgeQueryRewriter> _logger;

    public KnowledgeQueryRewriter(ILlmProviderService llm, ILogger<KnowledgeQueryRewriter> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<string> RewriteAsync(
        string sanitizedQuestion,
        string? missingHint = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sanitizedQuestion))
        {
            return sanitizedQuestion;
        }

        try
        {
            var hintBlock = string.IsNullOrWhiteSpace(missingHint)
                ? string.Empty
                : $"""
                  Focus the search on what was missing previously: {PromptInputSanitizer.SanitizeAndTruncate(missingHint, 256)}
                  """;

            var prompt = $"""
                Rewrite the user question into a short keyword-focused search query for a document knowledge base.
                Output ONLY the search query on one line. No quotes, no explanation.
                Preserve exact tokens, env var names, ticket ids, and proper nouns.
                {hintBlock}
                Question: {sanitizedQuestion}
                """;

            var raw = await _llm.GetCompletionAsync(prompt, cancellationToken);
            var rewritten = PromptInputSanitizer.SanitizeAndTruncate(
                raw, LlmInputLimits.ToolSearchQueryMaxLength * 4);
            // Take first line only.
            var newline = rewritten.IndexOfAny(['\r', '\n']);
            if (newline >= 0)
            {
                rewritten = rewritten[..newline].Trim();
            }

            if (string.IsNullOrWhiteSpace(rewritten))
            {
                return sanitizedQuestion;
            }

            _logger.LogInformation("Knowledge query rewrite applied");
            return rewritten;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Knowledge query rewrite failed; using original question");
            return sanitizedQuestion;
        }
    }
}
