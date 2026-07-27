using System;

namespace Fistix.TaskManager.Core.DomainModel.Constants;

public static class AiBatchStepNames
{
    public const string Embedding = "Embedding";
    public const string Classify = "Classify";
    public const string Summarize = "Summarize";

    public static readonly string[] DefaultSteps =
    [
        Embedding,
        Classify,
        Summarize
    ];

    public static bool IsKnown(string step) =>
        string.Equals(step, Embedding, StringComparison.OrdinalIgnoreCase)
        || string.Equals(step, Classify, StringComparison.OrdinalIgnoreCase)
        || string.Equals(step, Summarize, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string step)
    {
        if (string.Equals(step, Embedding, StringComparison.OrdinalIgnoreCase))
        {
            return Embedding;
        }

        if (string.Equals(step, Classify, StringComparison.OrdinalIgnoreCase))
        {
            return Classify;
        }

        if (string.Equals(step, Summarize, StringComparison.OrdinalIgnoreCase))
        {
            return Summarize;
        }

        throw new ArgumentException($"Unknown AI batch step '{step}'.", nameof(step));
    }
}
