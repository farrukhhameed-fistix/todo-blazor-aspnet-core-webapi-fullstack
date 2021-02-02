using Fistix.TaskManager.Core.DomainModel.Aggregates;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Fistix.TaskManager.Core.Abstractions.Repositories
{
  public interface ITodoTaskRepository
  {
    public Task<bool> Create(TodoTask todoTask, CancellationToken cancellationToken);
    public Task<bool> Update(TodoTask todoTask, CancellationToken calcellationToken);
    public Task<bool> Delete(Guid id, CancellationToken cancellationToken);
    public Task<TodoTask> Get(Guid id, CancellationToken cancellationToken);
    public Task<List<TodoTask>> GetAll(CancellationToken cancellationToken);
  }
}
