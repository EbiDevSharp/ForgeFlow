namespace ForgeFlow.Domain.Routings;

public enum WorkCenterType : byte
{
    Machine = 1,
    AssemblyLine = 2,
    WorkCell = 3,
    QualityStation = 4,
    ExternalVendor = 5
}

public enum RoutingStatus : byte
{
    Draft = 1,
    Active = 2,
    Obsolete = 3
}

public enum ProductionOrderOperationStatus : byte
{
    Pending = 1,
    Ready = 2,
    InProgress = 3,
    Paused = 4,
    Completed = 5,
    Skipped = 6
}
