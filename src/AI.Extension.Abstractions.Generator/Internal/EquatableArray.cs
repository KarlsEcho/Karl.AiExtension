using System;
using System.Collections;
using System.Collections.Generic;

namespace AI.Extension.Abstractions.Generator.Internal;

/// <summary>
/// 值语义的数组包装：让含数组的增量生成模型可按值比较，
/// 避免每次编译都判定为「模型变化」而重复生成。
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    public static readonly EquatableArray<T> Empty = new(Array.Empty<T>());

    private readonly T[]? _values;

    public EquatableArray(T[] values) => _values = values;

    public int Length => _values?.Length ?? 0;

    public T this[int index] => _values![index];

    public T[] ToArray() => _values ?? Array.Empty<T>();

    public bool Equals(EquatableArray<T> other)
    {
        T[] left = _values ?? Array.Empty<T>();
        T[] right = other._values ?? Array.Empty<T>();
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_values is null)
        {
            return 0;
        }

        unchecked
        {
            int hash = 17;
            foreach (T value in _values)
            {
                hash = (hash * 31) + (value?.GetHashCode() ?? 0);
            }

            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_values ?? Array.Empty<T>())).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
