using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Fistix.TaskManager.DataLayer
{
  public class EfContext: DbContext
  {
    public EfContext(DbContextOptions<EfContext> options) : base(options)
    {}

    public DbSet<TodoTask> TodoTasks { get; set; }
    public DbSet<UserProfile> UserProfiles { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
      base.OnModelCreating(modelBuilder);

      TodoTaskModelConfig(modelBuilder);
      UserProfileModelConfig(modelBuilder);
    }

    private void TodoTaskModelConfig(ModelBuilder modelBuilder)
    {
      modelBuilder.Entity<TodoTask>(entityModel =>
      {
        entityModel.ToTable("TodoTask");
        entityModel.HasKey(k => k.Id);        
      });
    }
    private void UserProfileModelConfig(ModelBuilder modelBuilder)
    {
      modelBuilder.Entity<UserProfile>(entityModel =>
      {
        entityModel.ToTable("UserProfile");
        entityModel.HasKey(k => k.Id);
      });
    }
  }
}
