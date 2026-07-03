namespace EZRestAPI.Utils;

using System.Collections;

/// <summary>
/// An immutable array wrapper with structural equality, required for values
/// flowing through incremental generator pipelines to be cacheable.
/// </summary>
public readonly struct EquatableArray<T>(T[] items) : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly T[]? items = items;

    public int Count => items?.Length ?? 0;

    public bool Equals(EquatableArray<T> other) =>
        (items ?? []).AsEnumerable().SequenceEqual(other.items ?? []);

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (items is null)
        {
            return 0;
        }
        unchecked
        {
            var hash = 17;
            foreach (var item in items)
            {
                hash = (hash * 31) + (item?.GetHashCode() ?? 0);
            }

            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(items ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
