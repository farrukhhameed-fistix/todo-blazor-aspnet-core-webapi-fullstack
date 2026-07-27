using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fistix.TaskManager.DataLayer.Repositories
{
  public class TodoTaskRepository : ITodoTaskRepository
  {
    private readonly EfContext _context;

    public TodoTaskRepository(EfContext context)
    {
      _context = context;
    }

    public async Task<bool> Create(TodoTask todoTask, CancellationToken cancellationToken)
    {
      _context.TodoTasks.Add(todoTask);
      var effectedRecords = await _context.SaveChangesAsync(cancellationToken);

      return effectedRecords > 0;
    }

    public async Task CreateManyAsync(IReadOnlyList<TodoTask> todoTasks, CancellationToken cancellationToken)
    {
      _context.TodoTasks.AddRange(todoTasks);
      await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> DeleteByImportTagAsync(Guid ownerExternalId, string importTag, CancellationToken cancellationToken)
    {
      var todos = await _context.TodoTasks
        .Where(t => t.CreatedByUserId == ownerExternalId && t.ImportTag == importTag)
        .ToListAsync(cancellationToken);

      if (todos.Count == 0)
      {
        return 0;
      }

      _context.TodoTasks.RemoveRange(todos);
      await _context.SaveChangesAsync(cancellationToken);
      return todos.Count;
    }

    public async Task<List<TodoTask>> GetByOwnerAndImportTagAsync(
      Guid ownerExternalId,
      string importTag,
      CancellationToken cancellationToken)
    {
      return await _context.TodoTasks
        .Include(t => t.AiMetadata)
        .Where(t => t.CreatedByUserId == ownerExternalId && t.ImportTag == importTag)
        .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TodoImportBatchSummary>> GetImportBatchesByOwnerAsync(
      Guid ownerExternalId,
      CancellationToken cancellationToken)
    {
      var todos = await _context.TodoTasks
        .AsNoTracking()
        .Include(t => t.AiMetadata)
        .Where(t => t.CreatedByUserId == ownerExternalId
                    && t.ImportTag != null
                    && t.ImportTag != "")
        .Select(t => new
        {
          ImportTag = t.ImportTag!,
          t.CreatedOn,
          t.Id,
          HasSummary = t.AiMetadata != null
                       && t.AiMetadata.AiSummary != null
                       && t.AiMetadata.AiSummary != "",
          HasClassify = t.AiMetadata != null
                        && t.AiMetadata.ClassificationStatus == ClassificationStatus.Completed
        })
        .ToListAsync(cancellationToken);

      if (todos.Count == 0)
      {
        return [];
      }

      var todoIds = todos.Select(t => t.Id).ToList();
      var embeddedIds = await _context.TodoEmbeddings
        .AsNoTracking()
        .Where(e => todoIds.Contains(e.TodoId))
        .Select(e => e.TodoId)
        .Distinct()
        .ToListAsync(cancellationToken);
      var embeddedSet = embeddedIds.ToHashSet();

      return todos
        .GroupBy(t => t.ImportTag)
        .Select(g => new TodoImportBatchSummary(
          g.Key,
          g.Count(),
          g.Min(x => x.CreatedOn),
          g.Max(x => x.CreatedOn),
          g.Count(x => !embeddedSet.Contains(x.Id)),
          g.Count(x => !x.HasClassify),
          g.Count(x => !x.HasSummary)))
        .OrderByDescending(s => s.NewestCreatedOn)
        .ToList();
    }

    public async Task<bool> Delete(Guid externalId, CancellationToken cancellationToken)
    {
      var todoTask = await _context.TodoTasks.FirstOrDefaultAsync(t => t.ExternalId == externalId, cancellationToken);
      if (todoTask == null)
        throw new NotFoundException();
      
      _context.TodoTasks.Remove(todoTask);
      var effectedRecords = await _context.SaveChangesAsync(cancellationToken);

      return effectedRecords > 0;
    }

    public async Task<TodoTask> Get(Guid externalId, CancellationToken calcellationToken)
    {
      var entity = await _context.TodoTasks
        .Include(t => t.AiMetadata)
        .FirstOrDefaultAsync(t => t.ExternalId == externalId, calcellationToken);
      if (entity == null)
        throw new NotFoundException();

      return entity;
    }

    public async Task<List<TodoTask>> GetAll(CancellationToken cancellationToken)
    {
      return await _context.TodoTasks
        .Include(t => t.AiMetadata)
        .ToListAsync(cancellationToken);
    }

    public async Task<List<TodoTask>> GetByOwner(Guid ownerExternalId, CancellationToken cancellationToken)
    {
      return await _context.TodoTasks
        .Include(t => t.AiMetadata)
        .Where(t => t.CreatedByUserId == ownerExternalId)
        .ToListAsync(cancellationToken);
    }

    public async Task<bool> Update(TodoTask todoTask, CancellationToken calcellationToken)
    {
      _context.TodoTasks.Update(todoTask);
      var effectedRecords = await _context.SaveChangesAsync();

      return effectedRecords > 0;
    }
  }
}
