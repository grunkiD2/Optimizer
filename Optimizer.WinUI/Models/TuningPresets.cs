namespace Optimizer.WinUI.Models;

public enum BoostMode
{
    Disabled = 0,
    Enabled = 1,
    Aggressive = 2,
    EfficientEnabled = 3,
    EfficientAggressive = 4,
    AggressiveAtGuaranteed = 5,
    EfficientAggressiveAtGuaranteed = 6
}

public class CpuTuning
{
    public int MinProcessorState { get; set; } = 5;       // %
    public int MaxProcessorState { get; set; } = 100;     // %
    public BoostMode BoostMode { get; set; } = BoostMode.Enabled;
    public int BoostPolicy { get; set; } = 60;            // %
}

public class TuningPreset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Risk { get; set; } = "Low";  // Low / Medium / High
    public CpuTuning Cpu { get; set; } = new();
}
