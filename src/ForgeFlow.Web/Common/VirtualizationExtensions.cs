namespace ForgeFlow.Web.Common;

public static class VirtualizationExtensions
{
    public static ICollection<T> AsCollection<T>(this IReadOnlyList<T>? list)
    {
        if (list is null) return Array.Empty<T>();
        if (list is ICollection<T> col) return col;
        return list.ToList();
    }

    public static ICollection<T> AsCollection<T>(this IReadOnlyCollection<T>? collection)
    {
        if (collection is null) return Array.Empty<T>();
        if (collection is ICollection<T> col) return col;
        return collection.ToList();
    }

    public static ICollection<T> AsCollection<T>(this IEnumerable<T>? enumerable)
    {
        if (enumerable is null) return Array.Empty<T>();
        if (enumerable is ICollection<T> col) return col;
        return enumerable.ToList();
    }
}
