using System;

namespace RyanAssets
{
    public sealed class InstantEvent<T>
    {
        private event Action<T> Handlers;

        private bool hasLastValue;
        private T lastValue;

        public void Invoke(T value1)
        {
            hasLastValue = true;
            lastValue = value1;

            Handlers?.Invoke(value1);
        }

        public void Subscribe(Action<T> handler, bool invokeImmediately = true)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            Handlers += handler;

            if (invokeImmediately && hasLastValue)
                handler(lastValue);
        }

        public void Unsubscribe(Action<T> handler)
        {
            Handlers -= handler;
        }

        public void ClearLastValue()
        {
            hasLastValue = false;
            lastValue = default;
        }
    }
}