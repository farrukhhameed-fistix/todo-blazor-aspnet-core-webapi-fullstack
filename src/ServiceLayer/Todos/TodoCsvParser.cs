#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Fistix.TaskManager.ServiceLayer.Todos;

public sealed class TodoCsvRow
{
    public int RowNumber { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public DateTime DueDate { get; init; }
    public string Status { get; init; } = "Pending";
    public string Priority { get; init; } = "Medium";
    public string? Category { get; init; }
    public string? ExpectedPriority { get; init; }
}

public sealed class TodoCsvParseResult
{
    public List<TodoCsvRow> Rows { get; } = [];
    public List<(int RowNumber, string Message)> Errors { get; } = [];
}

/// <summary>
/// Minimal CSV parser for todo import (RFC4180-ish, quoted fields supported).
/// </summary>
public static class TodoCsvParser
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Pending", "NotStarted", "InProgress", "Completed"
    };

    private static readonly HashSet<string> AllowedPriorities = new(StringComparer.OrdinalIgnoreCase)
    {
        "High", "Medium", "Low"
    };

    public static TodoCsvParseResult Parse(string csvContent, int maxRows = 200)
    {
        var result = new TodoCsvParseResult();
        if (string.IsNullOrWhiteSpace(csvContent))
        {
            result.Errors.Add((0, "CSV content is empty."));
            return result;
        }

        using var reader = new StringReader(csvContent);
        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            result.Errors.Add((0, "CSV header row is missing."));
            return result;
        }

        var headers = SplitCsvLine(headerLine)
            .Select(h => h.Trim())
            .ToList();

        var map = BuildHeaderMap(headers);
        if (!map.ContainsKey("title") || !map.ContainsKey("description") || !map.ContainsKey("duedate"))
        {
            result.Errors.Add((0, "CSV must include Title, Description, and DueDate columns."));
            return result;
        }

        var rowNumber = 1;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            rowNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (result.Rows.Count >= maxRows)
            {
                result.Errors.Add((rowNumber, $"Maximum of {maxRows} data rows exceeded."));
                break;
            }

            var fields = SplitCsvLine(line);
            try
            {
                var title = GetField(fields, map, "title");
                var description = GetField(fields, map, "description");
                var dueRaw = GetField(fields, map, "duedate");

                if (string.IsNullOrWhiteSpace(title))
                {
                    result.Errors.Add((rowNumber, "Title is required."));
                    continue;
                }

                if (title.Length > 200)
                {
                    result.Errors.Add((rowNumber, "Title exceeds 200 characters."));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(description))
                {
                    result.Errors.Add((rowNumber, "Description is required."));
                    continue;
                }

                if (description.Length > 4000)
                {
                    result.Errors.Add((rowNumber, "Description exceeds 4000 characters."));
                    continue;
                }

                if (!TryParseDueDate(dueRaw, out var dueDate))
                {
                    result.Errors.Add((rowNumber, $"Invalid DueDate '{dueRaw}'. Use yyyy-MM-dd or ISO-8601."));
                    continue;
                }

                var status = NormalizeStatus(GetField(fields, map, "status"));
                if (status is null)
                {
                    result.Errors.Add((rowNumber, "Status must be Pending, NotStarted, InProgress, or Completed."));
                    continue;
                }

                var priority = NormalizePriority(GetField(fields, map, "priority"));
                if (priority is null)
                {
                    result.Errors.Add((rowNumber, "Priority must be High, Medium, or Low."));
                    continue;
                }

                var category = GetField(fields, map, "category");
                if (!string.IsNullOrWhiteSpace(category) && category.Length > 100)
                {
                    result.Errors.Add((rowNumber, "Category exceeds 100 characters."));
                    continue;
                }

                result.Rows.Add(new TodoCsvRow
                {
                    RowNumber = rowNumber,
                    Title = title.Trim(),
                    Description = description.Trim(),
                    DueDate = DateTime.SpecifyKind(dueDate.Date, DateTimeKind.Utc),
                    Status = status,
                    Priority = priority,
                    Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
                    ExpectedPriority = NullIfEmpty(GetField(fields, map, "expectedpriority"))
                });
            }
            catch (Exception ex)
            {
                result.Errors.Add((rowNumber, $"Failed to parse row: {ex.Message}"));
            }
        }

        return result;
    }

    private static Dictionary<string, int> BuildHeaderMap(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            var key = headers[i].Trim().ToLowerInvariant().Replace(" ", string.Empty);
            if (!map.ContainsKey(key))
            {
                map[key] = i;
            }
        }

        return map;
    }

    private static string GetField(IReadOnlyList<string> fields, IReadOnlyDictionary<string, int> map, string key)
    {
        if (!map.TryGetValue(key, out var index) || index >= fields.Count)
        {
            return string.Empty;
        }

        return fields[index];
    }

    private static bool TryParseDueDate(string raw, out DateTime dueDate)
    {
        dueDate = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var formats = new[] { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ", "M/d/yyyy", "MM/dd/yyyy" };
        if (DateTime.TryParseExact(raw.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dueDate))
        {
            return true;
        }

        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dueDate);
    }

    private static string? NormalizeStatus(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "Pending";
        }

        var match = AllowedStatuses.FirstOrDefault(s => string.Equals(s, raw.Trim(), StringComparison.OrdinalIgnoreCase));
        return match;
    }

    private static string? NormalizePriority(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "Medium";
        }

        var match = AllowedPriorities.FirstOrDefault(s => string.Equals(s, raw.Trim(), StringComparison.OrdinalIgnoreCase));
        return match;
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (c == ',' && !inQuotes)
            {
                fields.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(c);
        }

        fields.Add(sb.ToString());
        return fields;
    }
}
