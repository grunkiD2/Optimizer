namespace Optimizer.WinUI.Models;

public class AnomalyAlert
{
    public string MetricName { get; set; } = "";
    public double Value { get; set; }
    public double ExpectedValue { get; set; }
    public double Severity { get; set; }  // 0-1
    public DateTime DetectedAt { get; set; } = DateTime.Now;
    public string Description { get; set; } = "";
}

// Training input for recommendation acceptance model
public class RecommendationFeature
{
    public string Category { get; set; } = "";
    public string Severity { get; set; } = "";
    public float DayOfWeek { get; set; }
    public float HourOfDay { get; set; }
    public bool Accepted { get; set; }
}

public class RecommendationPrediction
{
    public bool Accepted { get; set; }
    public float Probability { get; set; }
    public float Score { get; set; }
}
