using AqwCompanion.App.Infrastructure.AutoStart;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeAutoStart : IAutoStart
{
    public bool Enabled { get; set; }

    public bool Fails { get; set; }

    public bool Supported { get; set; } = true;

    public bool SetResult { get; set; } = true;

    public int SetCalls { get; private set; }

    public bool IsSupported => Supported;

    public bool IsEnabled() => Enabled;

    public bool TrySet(bool enabled)
    {
        SetCalls++;
        if (Fails)
        {
            return false;
        }

        if (!SetResult)
        {
            return false;
        }

        Enabled = enabled;
        return true;
    }
}
