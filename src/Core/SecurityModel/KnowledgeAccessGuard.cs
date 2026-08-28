using Fistix.TaskManager.Core.Abstractions.Services;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.Core.Exceptions;

namespace Fistix.TaskManager.Core.SecurityModel;

public static class KnowledgeAccessGuard
{
    public static void EnsureOwner(KnowledgeDocument document, ICurrentUserService currentUser)
    {
        var userId = TodoAccessGuard.RequireCurrentUserId(currentUser);
        if (document.CreatedByUserId != userId)
        {
            throw new ForbiddenAccessException();
        }
    }

    public static void EnsureOwner(KnowledgeIngestJob job, ICurrentUserService currentUser)
    {
        var userId = TodoAccessGuard.RequireCurrentUserId(currentUser);
        if (job.CreatedByUserId != userId)
        {
            throw new ForbiddenAccessException();
        }
    }
}
