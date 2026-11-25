namespace Universes.UniverseData.tycoon {
    /// <summary>
    /// A message held until its receiver listens. A client's handlers can be registered after the server
    /// first has something to say on join (the "welcome back" card): a message sent before that is lost,
    /// so it waits here until the client has asked for the state, and goes out once.
    /// </summary>
    public sealed class TY_HeldMessage<T> where T : struct {
        T message;
        bool pending;

        /// <summary>The client has asked for the state: its handlers are registered.</summary>
        public bool Listening { get; private set; }
        public bool Pending => pending;

        /// <summary>Holds <paramref name="value"/> (replacing an unsent one).</summary>
        public void Hold(T value) {
            message = value;
            pending = true;
        }

        public void MarkListening() => Listening = true;

        /// <summary>The held message, once, when the client listens.</summary>
        public bool TryTake(out T value) {
            value = message;
            if (!pending || !Listening) return false;
            pending = false;
            message = default;
            return true;
        }
    }
}
