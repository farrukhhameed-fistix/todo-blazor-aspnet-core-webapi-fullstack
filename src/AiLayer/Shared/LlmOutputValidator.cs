using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Fistix.TaskManager.AiLayer.Shared;

public static class LlmOutputValidator
{
    public const string InsufficientKnowledgeContextMessage =
        "I don't have enough matching document context to answer that. Try a more specific question or upload a related file.";

    public const string UngroundedKnowledgeAnswerMessage =
        "I couldn't produce a grounded answer from the retrieved document chunks (unsupported references). Please rephrase or narrow the question.";

    public const string InsufficientContextMessage =
        "I don't have enough matching tasks in context to answer that. Try a more specific question or add related todos.";

    public const string UngroundedAnswerMessage =
        "I couldn't produce a grounded answer from the retrieved tasks (unsupported task references). Please rephrase or narrow the question.";

    private static readonly Regex GuidRegex = new(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string ValidateSummary(string? raw, int maxLength = LlmInputLimits.SummaryMaxLength)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException("AI returned an empty summary.");
        }

        var cleaned = PromptInputSanitizer.StripControlCharacters(raw.Trim());
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            throw new InvalidOperationException("AI returned an empty summary.");
        }

        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    /// <summary>
    /// Requires priority and confidence properties. Does not apply silent MEDIUM/0.5 defaults.
    /// </summary>
    public static (string Priority, float Confidence, string? Reason) ValidateClassificationJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("AI returned an empty classification response.");
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("priority", out var priorityElement) ||
            priorityElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(priorityElement.GetString()))
        {
            throw new InvalidOperationException("AI classification response missing required 'priority'.");
        }

        if (!root.TryGetProperty("confidence", out var confidenceElement))
        {
            throw new InvalidOperationException("AI classification response missing required 'confidence'.");
        }

        float confidence;
        if (confidenceElement.ValueKind == JsonValueKind.Number &&
            confidenceElement.TryGetSingle(out var number))
        {
            confidence = number;
        }
        else if (confidenceElement.ValueKind == JsonValueKind.String &&
                 float.TryParse(confidenceElement.GetString(), out var parsed))
        {
            confidence = parsed;
        }
        else
        {
            throw new InvalidOperationException("AI classification 'confidence' must be a number between 0 and 1.");
        }

        if (float.IsNaN(confidence) || float.IsInfinity(confidence))
        {
            throw new InvalidOperationException("AI classification 'confidence' is not a finite number.");
        }

        string? reason = null;
        if (root.TryGetProperty("reason", out var reasonElement) &&
            reasonElement.ValueKind == JsonValueKind.String)
        {
            reason = TruncateReason(reasonElement.GetString());
        }

        return (
            ClassificationGuardrails.NormalizePriority(priorityElement.GetString()),
            Math.Clamp(confidence, 0f, 1f),
            reason);
    }

    public static string? TruncateReason(string? reason, int maxLength = LlmInputLimits.ReasonMaxLength)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var cleaned = PromptInputSanitizer.StripControlCharacters(reason.Trim());
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    /// <summary>
    /// Validates RAG answer length and that any Guids mentioned appear in retrieved sources.
    /// Returns the sanitized answer, or throws <see cref="InvalidOperationException"/> when ungrounded/empty.
    /// </summary>
    public static string ValidateRagAnswer(
        string? raw,
        IReadOnlyCollection<Guid> sourceTodoIds,
        int maxLength = LlmInputLimits.RagAnswerMaxLength)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException("AI returned an empty RAG answer.");
        }

        var cleaned = PromptInputSanitizer.StripControlCharacters(raw.Trim());
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            throw new InvalidOperationException("AI returned an empty RAG answer.");
        }

        if (cleaned.Length > maxLength)
        {
            cleaned = cleaned[..maxLength];
        }

        var mentioned = ExtractGuids(cleaned);
        if (mentioned.Count == 0)
        {
            return cleaned;
        }

        var allowed = new HashSet<Guid>(sourceTodoIds);
        if (mentioned.Any(id => !allowed.Contains(id)))
        {
            throw new InvalidOperationException("RAG answer references todo ids not present in retrieved sources.");
        }

        return cleaned;
    }

    public static IReadOnlyList<Guid> ExtractGuids(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<Guid>();
        }

        var ids = new List<Guid>();
        foreach (Match match in GuidRegex.Matches(text))
        {
            if (Guid.TryParse(match.Value, out var id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    public static string ValidateAgentText(
        string? raw,
        int maxLength = LlmInputLimits.AgentTextMaxLength)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        return PromptInputSanitizer.SanitizeAndTruncate(raw, maxLength);
    }
}
