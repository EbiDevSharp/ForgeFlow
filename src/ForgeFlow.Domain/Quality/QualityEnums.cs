namespace ForgeFlow.Domain.Quality;

public enum QualityInspectionStatus : byte
{
    Pending = 1,
    InReview = 2,
    Completed = 3,
    Cancelled = 4
}

public enum QualityInspectionSourceType : byte
{
    Receipt = 1,
    ProductionReceipt = 2,
    ManualHold = 3
}
