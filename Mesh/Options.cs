using System;

namespace Mesh {
    public class Options {
        /// <summary>The number of milliseconds between network pings.</summary>
        public TimeSpan ProtocolPeriod { get; set; } = TimeSpan.FromMilliseconds(2000);
        /// <summary>The number of milliseconds to wait for ping acknowledgements.</summary>
        public TimeSpan AckTimeout { get; set; } = TimeSpan.FromMilliseconds(1000);
        /// <summary>The number of milliseconds before declaring a remote node dead.</summary>
        public TimeSpan DeadTimeout { get; set; } = TimeSpan.FromMilliseconds(10000);
        /// <summary>The number of milliseconds before a dead node is pruned.</summary>
        public TimeSpan DeadBackoff { get; set; } = TimeSpan.FromMilliseconds(30000);
        /// <summary>The number of milliseconds before a dead node is pruned.</summary>
        public TimeSpan PruneTimeout { get; set; } = TimeSpan.FromMilliseconds(60000);
        /// <summary>The number of remote nodes to contact for network pings.</summary>
        public int FanOutFactor { get; set; } = 1;
        /// <summary>The maximum length of a single network packet (MTU).</summary>
        public int MaxPacketSize { get; set; } = 500;
    }
}
