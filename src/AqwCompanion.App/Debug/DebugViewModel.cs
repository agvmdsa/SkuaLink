using AqwCompanion.App.Diagnostics;
using AqwCompanion.App.HardFarms;

namespace AqwCompanion.App.Debug;

/// <summary>What the debug window shows: the Hard Farms entries without a match and the diagnostics export.</summary>
public sealed class DebugViewModel(DiagnosticsViewModel diagnostics, HardFarmsViewModel hardFarms)
{
    public DiagnosticsViewModel Diagnostics { get; } = diagnostics;
    public HardFarmsViewModel HardFarms { get; } = hardFarms;
}
