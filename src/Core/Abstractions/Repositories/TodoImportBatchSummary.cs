#nullable enable

using System;

namespace Fistix.TaskManager.Core.Abstractions.Repositories;

/// <summary>Aggregated CSV/import batch for a single ImportTag owned by a user.</summary>
public sealed record TodoImportBatchSummary(
    string ImportTag,
    int TodoCount,
    DateTime OldestCreatedOn,
    DateTime NewestCreatedOn,
    int MissingEmbeddings,
    int MissingClassify,
    int MissingSummary);
