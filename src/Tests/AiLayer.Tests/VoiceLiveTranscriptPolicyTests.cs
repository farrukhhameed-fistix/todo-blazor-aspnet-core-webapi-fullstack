#nullable enable

using Fistix.TaskManager.ViewModel.Commands.Todos;

namespace Fistix.TaskManager.AiLayer.Tests;

public class VoiceLiveTranscriptPolicyTests
{
    [Fact]
    public void IsComplete_RejectsInterim()
    {
        Assert.False(VoiceLiveTranscriptPolicy.IsComplete("create a task buy milk", hasInterim: true));
    }

    [Fact]
    public void IsComplete_RejectsShortOrEmpty()
    {
        Assert.False(VoiceLiveTranscriptPolicy.IsComplete(null, hasInterim: false));
        Assert.False(VoiceLiveTranscriptPolicy.IsComplete("   ", hasInterim: false));
        Assert.False(VoiceLiveTranscriptPolicy.IsComplete("ok", hasInterim: false));
    }

    [Fact]
    public void IsComplete_RequiresALetter()
    {
        Assert.False(VoiceLiveTranscriptPolicy.IsComplete("12345678", hasInterim: false));
    }

    [Fact]
    public void IsComplete_AcceptsJoinedFinals()
    {
        Assert.True(VoiceLiveTranscriptPolicy.IsComplete("create a task buy milk", hasInterim: false));
    }
}
