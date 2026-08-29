using MdfTracker.Api.Models;

namespace MdfTracker.Api.Responses;

/// <summary>Feeds the dashboard's KPI cards and the success/failure bar chart.</summary>
public class OverviewResponse
{
    public int TotalSessions { get; set; }

    public int ActiveSessions { get; set; }

    public int CompletedSessions { get; set; }

    /// <summary>Percentage, 0-100, over completed sessions.</summary>
    public decimal SuccessRate { get; set; }

    public decimal? LowestFps { get; set; }

    public decimal? HighestFps { get; set; }

    public decimal? AverageFps { get; set; }

    public int TotalFrames { get; set; }

    public List<AlgorithmBreakdownItem> ByAlgorithm { get; set; } = new();
}

public class AlgorithmBreakdownItem
{
    public TrackerAlgorithm TrackerAlgorithm { get; set; }

    public int SuccessCount { get; set; }

    public int FailureCount { get; set; }

    public int TotalCount { get; set; }

    public decimal SuccessRate { get; set; }

    public decimal? AverageFps { get; set; }
}
