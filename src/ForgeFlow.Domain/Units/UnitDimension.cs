namespace ForgeFlow.Domain.Units;

/// <summary>Shipped immutable physical unit dimensions shared as global reference values across organizations.</summary>
public enum UnitDimension : byte
{
    Mass = 1,
    Volume = 2,
    Length = 3,
    Area = 4,
    Count = 5,
    Time = 6
}
