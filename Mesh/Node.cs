using System.IO;
using System.Net;
using System.Threading;

namespace Mesh {
    public enum NodeState {
        Alive = 0,
        Suspect = 1,
        Dead = 2,
        Left = 3,
        Pruned = 4
    }

    public class Node {
        // The number of times the node has been broadcast between state changes, this
        // is used to order nodes when broadcasting so that the number of broadcasts
        // are roughly the same for all nodes
        private long counter;
        public NodeState State { get; internal set; }
        public IPAddress Address { get; internal set; }
        internal byte Generation { get; set; }
        internal long Counter {
            get => Interlocked.Read(ref counter);
        }

        internal void Update(NodeState state) {
            State = state;
            Interlocked.Exchange(ref counter, 0);  // Reset the counter
        }

        internal bool IsLaterGeneration(byte newGeneration) {
            // Rollover generation counts at 190
            return ((0 < (newGeneration - Generation)) && ((newGeneration - Generation) < 191))
                 || ((newGeneration - Generation) <= -191);
        }

        internal bool IsSupersededState(NodeState state) {
            return State < state;
        }

        internal static Node ReadFrom(Stream stream) {
            if (stream.Position >= stream.Length)
                return null;

            return new Node {
                State = Codec.ReadState(stream),
                Address = Codec.ReadIP4Address(stream),
                Generation = (byte)stream.ReadByte()
            };
        }

        internal void WriteTo(Stream stream) {
            stream.WriteState(State);
            stream.WriteIP4Address(Address);
            stream.WriteGeneration(Generation);
            Interlocked.Increment(ref counter);
        }
    }
}
