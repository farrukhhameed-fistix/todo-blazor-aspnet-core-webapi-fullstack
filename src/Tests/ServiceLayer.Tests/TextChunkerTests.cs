#nullable enable

using Fistix.TaskManager.ServiceLayer.Knowledge;
using Xunit;

namespace Fistix.TaskManager.ServiceLayer.Tests;

public class TextChunkerTests
{
    [Fact]
    public void Split_Empty_ReturnsNoChunks()
    {
        Assert.Empty(TextChunker.Split(null, 80, 10));
        Assert.Empty(TextChunker.Split("   ", 80, 10));
    }

    [Fact]
    public void Split_RespectsChunkSizeAndOverlap()
    {
        var text = string.Join(' ', Enumerable.Repeat("word", 80));
        var chunks = TextChunker.Split(text, chunkSize: 60, chunkOverlap: 10);

        Assert.True(chunks.Count >= 2);
        Assert.All(chunks, c => Assert.True(c.Content.Length <= 60 || c.Content.Length <= 70));
        for (var i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(chunks[i].Content));
        }
    }

    [Fact]
    public void Split_CapturesMarkdownHeading()
    {
        var text = """
            # Payments

            Charge the customer after Auth0 login succeeds.
            Store the Stripe customer id on the profile.
            """;

        var chunks = TextChunker.Split(text, chunkSize: 500, chunkOverlap: 20);
        Assert.NotEmpty(chunks);
        Assert.Equal("Payments", chunks[0].Heading);
    }

    [Fact]
    public void Split_SmallText_IsSingleChunk()
    {
        var chunks = TextChunker.Split("Short note about Auth.", 800, 100);
        Assert.Single(chunks);
        Assert.Equal(0, chunks[0].Ordinal);
        Assert.Contains("Auth", chunks[0].Content);
    }

    [Fact]
    public void Split_TruncatesHeadingToMaxLength()
    {
        var longTitle = new string('A', TextChunker.MaxHeadingLength + 200);
        var text = $"# {longTitle}\n\nBody about Auth0 silent refresh.";
        var chunks = TextChunker.Split(text, chunkSize: 800, chunkOverlap: 100);

        Assert.NotEmpty(chunks);
        Assert.NotNull(chunks[0].Heading);
        Assert.Equal(TextChunker.MaxHeadingLength, chunks[0].Heading!.Length);
    }

    [Fact]
    public void Split_IgnoresHashWithoutSpace_AsHeading()
    {
        // PDF extractors sometimes emit "#Title" glued to body without a space after #.
        var glued = "#" + new string('B', 600) + " Auth0 login";
        var chunks = TextChunker.Split(glued, chunkSize: 800, chunkOverlap: 100);

        Assert.NotEmpty(chunks);
        Assert.Null(chunks[0].Heading);
    }
}
