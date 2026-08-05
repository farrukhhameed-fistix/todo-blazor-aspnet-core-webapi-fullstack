#nullable enable

using System.Security.Cryptography;
using System.Text;
using Fistix.TaskManager.AiLayer.Shared;

namespace Fistix.TaskManager.AiLayer.Observability;

public static class AiPayloadRedactor
{
    public static string? Preview(string? value, AiObservabilitySettings settings)
    {
        if (!settings.CapturePayloadPreview || string.IsNullOrEmpty(value))
        {
            return null;
        }

        var max = Math.Clamp(settings.PayloadPreviewMaxChars, 8, 4000);
        if (value.Length <= max)
        {
            return value;
        }

        return value[..max] + "…";
    }

    public static string Hash(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "empty";
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes.AsSpan(0, 8)).ToLowerInvariant();
    }

    public static int CharCount(string? value) => value?.Length ?? 0;
}
