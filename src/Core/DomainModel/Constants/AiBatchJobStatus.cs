using System;

namespace Fistix.TaskManager.Core.DomainModel.Constants;

public static class AiBatchJobStatus
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Paused = "Paused";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public const string Stuck = "Stuck";

    public static bool IsActive(string status) =>
        string.Equals(status, Running, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, Paused, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, Pending, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, Stuck, StringComparison.OrdinalIgnoreCase);
}
