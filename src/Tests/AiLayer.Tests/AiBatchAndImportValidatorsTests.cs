using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.ViewModel.Validators.Todos;
using FluentValidation.TestHelper;

namespace Fistix.TaskManager.AiLayer.Tests;

public class AiBatchAndImportValidatorsTests
{
    [Fact]
    public void Import_Fails_WhenCsvContentEmpty()
    {
        var validator = new ImportTodoTasksFromCsvCommandValidator();
        var result = validator.TestValidate(new ImportTodoTasksFromCsvCommand { CsvContent = "" });
        result.ShouldHaveValidationErrorFor(x => x.CsvContent);
    }

    [Fact]
    public void Import_Fails_WhenImportTagTooLong()
    {
        var validator = new ImportTodoTasksFromCsvCommandValidator();
        var result = validator.TestValidate(new ImportTodoTasksFromCsvCommand
        {
            CsvContent = "Title,Description,DueDate\nA,B,2026-08-01",
            ImportTag = new string('x', 101)
        });
        result.ShouldHaveValidationErrorFor(x => x.ImportTag);
    }

    [Fact]
    public void Import_Passes_WithContent()
    {
        var validator = new ImportTodoTasksFromCsvCommandValidator();
        var result = validator.TestValidate(new ImportTodoTasksFromCsvCommand
        {
            CsvContent = "Title,Description,DueDate\nA,B,2026-08-01",
            ImportTag = "import-1"
        });
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Delete_Fails_WhenTagEmpty()
    {
        var validator = new DeleteImportedTodosCommandValidator();
        var result = validator.TestValidate(new DeleteImportedTodosCommand { ImportTag = "" });
        result.ShouldHaveValidationErrorFor(x => x.ImportTag);
    }

    [Fact]
    public void StartBatch_Fails_WithoutTagOrIds()
    {
        var validator = new StartAiBatchJobCommandValidator();
        var result = validator.TestValidate(new StartAiBatchJobCommand
        {
            BatchSize = 5,
            DelayMsBetweenItems = 100
        });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void StartBatch_Fails_WhenBatchSizeOutOfRange()
    {
        var validator = new StartAiBatchJobCommandValidator();
        var result = validator.TestValidate(new StartAiBatchJobCommand
        {
            ImportTag = "t1",
            BatchSize = 0
        });
        result.ShouldHaveValidationErrorFor(x => x.BatchSize);
    }

    [Fact]
    public void StartBatch_Passes_WithImportTag()
    {
        var validator = new StartAiBatchJobCommandValidator();
        var result = validator.TestValidate(new StartAiBatchJobCommand
        {
            ImportTag = "import-abc",
            BatchSize = 5,
            DelayMsBetweenItems = 500
        });
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Pause_Fails_WhenJobIdEmpty()
    {
        var validator = new PauseAiBatchJobCommandValidator();
        var result = validator.TestValidate(new PauseAiBatchJobCommand { JobExternalId = Guid.Empty });
        result.ShouldHaveValidationErrorFor(x => x.JobExternalId);
    }

    [Fact]
    public void Continue_Fails_WhenJobIdEmpty()
    {
        var validator = new ContinueAiBatchJobCommandValidator();
        var result = validator.TestValidate(new ContinueAiBatchJobCommand { JobExternalId = Guid.Empty });
        result.ShouldHaveValidationErrorFor(x => x.JobExternalId);
    }

    [Fact]
    public void Cancel_Fails_WhenJobIdEmpty()
    {
        var validator = new CancelAiBatchJobCommandValidator();
        var result = validator.TestValidate(new CancelAiBatchJobCommand { JobExternalId = Guid.Empty });
        result.ShouldHaveValidationErrorFor(x => x.JobExternalId);
    }
}
