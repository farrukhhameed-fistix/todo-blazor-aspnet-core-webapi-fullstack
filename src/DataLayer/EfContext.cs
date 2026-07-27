using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace Fistix.TaskManager.DataLayer
{
  public class EfContext: DbContext
  {
    public EfContext(DbContextOptions<EfContext> options) : base(options)
    {}

    public DbSet<TodoTask> TodoTasks { get; set; }
    public DbSet<UserProfile> UserProfiles { get; set; }
    public DbSet<TodoAiMetadata> TodoAiMetadatas { get; set; }
    public DbSet<TodoEmbedding> TodoEmbeddings { get; set; }
    public DbSet<AiConversation> AiConversations { get; set; }
    public DbSet<ToolExecutionLog> ToolExecutionLogs { get; set; }
    public DbSet<Sprint> Sprints { get; set; }
    public DbSet<SprintTodo> SprintTodos { get; set; }
    public DbSet<AiBatchJob> AiBatchJobs { get; set; }
    public DbSet<SprintOptimizerJob> SprintOptimizerJobs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
      base.OnModelCreating(modelBuilder);

      modelBuilder.HasPostgresExtension("vector");

      TodoTaskModelConfig(modelBuilder);
      UserProfileModelConfig(modelBuilder);
      TodoAiMetadataModelConfig(modelBuilder);
      TodoEmbeddingModelConfig(modelBuilder);
      AiConversationModelConfig(modelBuilder);
      ToolExecutionLogModelConfig(modelBuilder);
      SprintModelConfig(modelBuilder);
      SprintTodoModelConfig(modelBuilder);
      AiBatchJobModelConfig(modelBuilder);
      SprintOptimizerJobModelConfig(modelBuilder);
    }

    private void TodoTaskModelConfig(ModelBuilder modelBuilder)
    {
      modelBuilder.Entity<TodoTask>(entityModel =>
      {
        entityModel.ToTable("TodoTask");
        entityModel.HasKey(k => k.Id);
        entityModel.Property(p => p.Id)
          .ValueGeneratedOnAdd();
        entityModel.Property(p => p.ExternalId)
          .HasDefaultValueSql("gen_random_uuid()")
          .IsRequired();
        entityModel.HasIndex(k => k.ExternalId)
          .IsUnique();
        entityModel.Property(p => p.CreatedByUserId)
          .IsRequired();
        entityModel.HasIndex(p => p.CreatedByUserId);
        entityModel.Property(p => p.ImportTag)
          .HasMaxLength(100);
        entityModel.HasIndex(p => new { p.CreatedByUserId, p.ImportTag });
      });
    }

    private void AiBatchJobModelConfig(ModelBuilder modelBuilder)
    {
      modelBuilder.Entity<AiBatchJob>(entityModel =>
      {
        entityModel.ToTable("AiBatchJob");
        entityModel.HasKey(k => k.Id);
        entityModel.Property(p => p.Id).ValueGeneratedOnAdd();
        entityModel.Property(p => p.ExternalId)
          .HasDefaultValueSql("gen_random_uuid()")
          .IsRequired();
        entityModel.HasIndex(k => k.ExternalId).IsUnique();
        entityModel.Property(p => p.CreatedByUserId).IsRequired();
        entityModel.HasIndex(p => p.CreatedByUserId);
        entityModel.HasIndex(p => p.Status);
        entityModel.Property(p => p.StepsCsv).HasMaxLength(200).IsRequired();
        entityModel.Property(p => p.CurrentStep).HasMaxLength(50).IsRequired();
        entityModel.Property(p => p.Status).HasMaxLength(50).IsRequired();
        entityModel.Property(p => p.ImportTag).HasMaxLength(100);
        entityModel.Property(p => p.LastError).HasMaxLength(2000);
        entityModel.Property(p => p.TodoExternalIdsJson).IsRequired();
      });
    }

    private void SprintOptimizerJobModelConfig(ModelBuilder modelBuilder)
    {
      modelBuilder.Entity<SprintOptimizerJob>(entityModel =>
      {
        entityModel.ToTable("SprintOptimizerJob");
        entityModel.HasKey(k => k.Id);
        entityModel.Property(p => p.Id).ValueGeneratedOnAdd();
        entityModel.Property(p => p.ExternalId)
          .HasDefaultValueSql("gen_random_uuid()")
          .IsRequired();
        entityModel.HasIndex(k => k.ExternalId).IsUnique();
        entityModel.Property(p => p.CreatedByUserId).IsRequired();
        entityModel.HasIndex(p => p.CreatedByUserId);
        entityModel.HasIndex(p => p.Status);
        entityModel.Property(p => p.Status).HasMaxLength(50).IsRequired();
        entityModel.Property(p => p.CurrentPhase).HasMaxLength(50).IsRequired();
        entityModel.Property(p => p.Name).HasMaxLength(200);
        entityModel.Property(p => p.StatusMessage).HasMaxLength(500);
        entityModel.Property(p => p.LastError).HasMaxLength(2000);
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
    private void TodoAiMetadataModelConfig(ModelBuilder modelBuilder)
    {
      modelBuilder.Entity<TodoAiMetadata>(entityModel =>
      {
        entityModel.ToTable("TodoAiMetadata");
        entityModel.HasKey(k => k.Id);
        entityModel.HasIndex(k => k.TodoId).IsUnique();
        
        entityModel.HasOne(m => m.TodoTask)
          .WithOne(t => t.AiMetadata)
          .HasForeignKey<TodoAiMetadata>(m => m.TodoId)
          .OnDelete(DeleteBehavior.Cascade);
      });
    }

    private void TodoEmbeddingModelConfig(ModelBuilder modelBuilder)
    {
      modelBuilder.Entity<TodoEmbedding>(entityModel =>
      {
        entityModel.ToTable("TodoEmbeddings");
        entityModel.HasKey(k => k.Id);
        entityModel.Property(p => p.Embedding)
          .HasColumnType("vector(384)");
        entityModel.Property(p => p.EmbeddingModel)
          .HasMaxLength(100)
          .IsRequired();
        entityModel.HasIndex(e => new { e.TodoId, e.EmbeddingModel }).IsUnique();
        entityModel.HasOne(e => e.TodoTask)
          .WithMany()
          .HasForeignKey(e => e.TodoId)
          .OnDelete(DeleteBehavior.Cascade);
      });
    }

    private void AiConversationModelConfig(ModelBuilder modelBuilder)
    {
      modelBuilder.Entity<AiConversation>(entityModel =>
      {
        entityModel.ToTable("AiConversations");
        entityModel.HasKey(k => k.Id);
        entityModel.Property(p => p.UserId).HasMaxLength(256).IsRequired();
        entityModel.Property(p => p.Context).HasMaxLength(50);
        entityModel.Property(p => p.Model).HasMaxLength(100);
        entityModel.HasIndex(p => p.UserId);
        entityModel.HasIndex(p => p.CreatedAt);
      });
    }

    private void ToolExecutionLogModelConfig(ModelBuilder modelBuilder)
    {
      modelBuilder.Entity<ToolExecutionLog>(entityModel =>
      {
        entityModel.ToTable("ToolExecutionLog");
        entityModel.HasKey(k => k.Id);
        entityModel.Property(p => p.UserId).HasMaxLength(256).IsRequired();
        entityModel.Property(p => p.ToolName).HasMaxLength(100).IsRequired();
        entityModel.HasIndex(p => p.UserId);
        entityModel.HasIndex(p => p.ExecutedAt);
        entityModel.HasIndex(p => p.ToolName);
      });
    }

    private void SprintModelConfig(ModelBuilder modelBuilder)
    {
      modelBuilder.Entity<Sprint>(entityModel =>
      {
        entityModel.ToTable("Sprint");
        entityModel.HasKey(k => k.Id);
        entityModel.Property(p => p.Id).ValueGeneratedOnAdd();
        entityModel.Property(p => p.ExternalId)
          .HasDefaultValueSql("gen_random_uuid()")
          .IsRequired();
        entityModel.HasIndex(k => k.ExternalId).IsUnique();
        entityModel.Property(p => p.Name).HasMaxLength(200).IsRequired();
        entityModel.Property(p => p.CreatedByUserId).IsRequired();
        entityModel.Property(p => p.Reasoning).HasMaxLength(4000);
        entityModel.HasIndex(p => p.CreatedByUserId);
        entityModel.HasIndex(p => p.CreatedAt);
      });
    }

    private void SprintTodoModelConfig(ModelBuilder modelBuilder)
    {
      modelBuilder.Entity<SprintTodo>(entityModel =>
      {
        entityModel.ToTable("SprintTodo");
        entityModel.HasKey(k => new { k.SprintId, k.TodoId });
        entityModel.HasOne(st => st.Sprint)
          .WithMany(s => s.SprintTodos)
          .HasForeignKey(st => st.SprintId)
          .OnDelete(DeleteBehavior.Cascade);
        entityModel.HasOne(st => st.TodoTask)
          .WithMany()
          .HasForeignKey(st => st.TodoId)
          .OnDelete(DeleteBehavior.Cascade);
        entityModel.HasIndex(st => st.TodoId);
      });
    }
  }
}
