namespace ForgeFlow.Domain.Boms;

public enum BomVersionStatus : byte
{
    Draft = 1,
    Approved = 2,
    Active = 3,
    Superseded = 4,
    Obsolete = 5
}

public enum BomOutputType : byte
{
    MainProduct = 1,
    CoProduct = 2,
    ByProduct = 3,
    Waste = 4
}

public enum ApplicabilityOperator : byte
{
    Equals = 1,
    NotEquals = 2,
    In = 3,
    NotIn = 4
}
