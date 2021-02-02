using System;
using System.Collections.Generic;
using System.Text;

namespace Fistix.TaskManager.Core.DomainModel.SeedWork
{
  public class Entity
  {
    public Guid Id { get; protected set; }

    public void GenerateNewId()
    {
      Id = Guid.NewGuid();
    }
  }
}
