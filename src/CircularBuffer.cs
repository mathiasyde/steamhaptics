
// generic circular buffer implementation
// should be thread safe for one producer and one consumer
public class CircularBuffer<T> where T : new(){
    private T[] buffer;

    private long head;
    private long tail;

    public long Capacity => buffer.Length;

    public long Available => Volatile.Read(ref head) - Volatile.Read(ref tail);
    public long Free => Capacity - Available;

    public bool IsEmpty => Available == 0;
    public bool IsFull => Available == Capacity;

    public CircularBuffer(long capacity) {
        buffer = new T[capacity];
        head = 0;
        tail = 0;
    }

    public void Preallocate() {
        for (int i = 0; i < buffer.Length; i++) {
            buffer[i] = new T();
        }
    }

    // try write an item to the buffer, if there is space available, and advance the head index
    public bool TryWrite(T item) {
        long write = Volatile.Read(ref head);
        long read = Volatile.Read(ref tail);

        // if the buffer is full, return false and do not write the item
        if (write - read == Capacity) {
            return false;
        }        

        buffer[write % Capacity] = item;
        Volatile.Write(ref head, write + 1);
        return true;
    }

    // try read the current item from the buffer, if available, and advance the tail index
    // if the buffer is empty, return false and default the item
    public bool TryRead(out T item) {
        long write = Volatile.Read(ref head);
        long read = Volatile.Read(ref tail);

        if (read == write) {
            item = new T();
            return false;
        }

        item = buffer[read % Capacity];
        Volatile.Write(ref tail, read + 1);
        return true;
    }

    public bool TryPeek(out T item) {
        long read = Volatile.Read(ref tail);
        long write = Volatile.Read(ref head);

        if (read == write) {
            item = new T();
            return false;
        }

        item = buffer[read % Capacity];
        return true;
    }
}
