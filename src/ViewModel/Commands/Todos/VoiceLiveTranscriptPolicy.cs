#nullable enable

using System;
using System.Linq;

namespace Fistix.TaskManager.ViewModel.Commands.Todos;

/// <summary>
/// Completeness check for browser Web Speech finals. Interim-only text is never a command.
/// </summary>
public static class VoiceLiveTranscriptPolicy
{
    public const int MinChars = 8;

    public static bool IsComplete(string? finals, bool hasInterim)
    {
        if (hasInterim)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(finals))
        {
            return false;
        }

        var trimmed = finals.Trim();
        return trimmed.Length >= MinChars && trimmed.Any(char.IsLetter);
    }
}
