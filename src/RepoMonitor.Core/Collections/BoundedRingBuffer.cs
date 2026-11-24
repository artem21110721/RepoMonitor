using System.Collections;

namespace RepoMonitor.Core.Collections;

public class BoundedRingBuffer<T> : IReadOnlyCollection<T>
{
    private readonly T[] _buffer;
    private int _head;
    private int _tail;
    private int _count;

    public BoundedRingBuffer(int capacity = 50)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _buffer = new T[capacity];
    }

    public int Count => _count;
    public int Capacity => _buffer.Length;

    public void Add(T item)
    {
        _buffer[_tail] = item;
        _tail = (_tail + 1) % _buffer.Length;

        if (_count == _buffer.Length)
        {
            _head = (_head + 1) % _buffer.Length;
        }
        else
        {
            _count++;
        }
    }

    public void Clear()
    {
        Array.Clear(_buffer, 0, _buffer.Length);
        _head = 0;
        _tail = 0;
        _count = 0;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
        {
            int index = (_head + i) % _buffer.Length;
            yield return _buffer[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
