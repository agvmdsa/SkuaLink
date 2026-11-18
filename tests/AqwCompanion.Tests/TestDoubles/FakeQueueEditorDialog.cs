using AqwCompanion.App.Queue;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeQueueEditorDialog : IQueueEditorDialog
{
    public List<string> Shown { get; } = [];

    public void Show(string character) => Shown.Add(character);
}
