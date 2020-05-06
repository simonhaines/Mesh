using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mesh {
    internal enum MessageType {
        /// <summary>A direct ping of a peer</summary>
        Ping,
        /// <summary>The acknowledgement of a direct ping</summary>
        Ack,
        /// <summary>A request for a peer to ping a suspected dead peer</summary>
        RequestPing,
        /// <summary>The reply to a request to ping a suspected dead peer</summary>
        RequestAck,
        /// <summary>A relayed ping from one peer to another through this node</summary>
        RelayedPing,
        /// <summary>The reply to a relayed ping</summary>
        RelayedAck
    }

    class Server {
        /// <summary>Called when the status of a network node is changed.</summary>
        public event Action<Node> NodeUpdated;

        readonly ITransportFactory transportFactory;
        readonly Options options;
        readonly Dictionary<IPAddress, Node> nodes;
        readonly ConcurrentDictionary<IPAddress, DateTime> acks;

        private Node localNode;
        private ITransport transport;

        public Server(ITransportFactory transportFactory, Options options) {
            this.transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            nodes = new Dictionary<IPAddress, Node>();
            acks = new ConcurrentDictionary<IPAddress, DateTime>();
        }

        public async Task Start() {
            // Create the initial interface
            transport = transportFactory.Create();
            localNode = new Node {
                State = NodeState.Alive,
                Address = transport.Address
            };

            // Start receiving packets
            Listen();

            // Wait for bootstrapping
            await Bootstrap();

            // Start the message pump
            MessagePump();

            // Detect dead nodes and prune them
            DeadNodeDetector();
        }

        private async void Listen() {
            var cancellationToken = new CancellationToken();
            transport.Start();
            while (true) {
                try {
                    var (remoteAddress, buffer) = await transport.Receive(cancellationToken);
                    var timestamp = DateTime.UtcNow;
                    using (var stream = new MemoryStream(buffer, false)) {
                        if (stream.CheckProtocol()) {
                            IPAddress source, destination, relay = null;
                            MessageType messageType = stream.ReadMessageType();
                            switch (messageType) {
                                case MessageType.RequestPing:
                                case MessageType.RequestAck:
                                    source = remoteAddress;
                                    destination = stream.ReadIP4Address();
                                    break;
                                case MessageType.RelayedPing:
                                case MessageType.RelayedAck:
                                    source = remoteAddress;
                                    destination = localNode.Address;
                                    relay = stream.ReadIP4Address();
                                    break;
                                default:
                                    source = remoteAddress;
                                    destination = localNode.Address;
                                    break;
                            }

                            // Update our node information from the remote peer
                            UpdateNodes(remoteAddress, timestamp, stream);

                            // Process the request
                            ProcessRequest(messageType, source, destination, relay);
                        } else {
                            // FIXME log incompatible request
                        }
                    }
                } catch (TaskCanceledException) {
                    transport.Stop();
                    return;
                } catch (Exception) {
                    transport.Stop();
                    transport = transportFactory.Create();
                    transport.Start();
                }
            }
        }

        private async Task Bootstrap() {
            Trace.TraceInformation("[mesh] bootstrapping...");

            // TODO from seeds

            Trace.TraceInformation($"[Mesh] bootstrapped with {nodes.Count} nodes");
        }

        private void UpdateNodes(IPAddress address, DateTime timestamp, Stream stream) {
            // Read the source's state and generation
            var sourceState = stream.ReadState();
            var sourceGeneration = stream.ReadGeneration();

            // Read the source's version of our state and generation
            var localState = stream.ReadState();
            var localGeneration = stream.ReadGeneration();

            // If the source has outdated information about the local node, or
            // the source thinks the local node is no longer alive, update the source
            if (localNode.IsLaterGeneration(localGeneration) ||
                (localState != NodeState.Alive && localGeneration == localNode.Generation)) {
                // FIXME what is happening here?
                localNode.Generation = (byte)(localGeneration + 1);

            }

            // Handle the source node, then the rest of the nodes in the stream
            Node remoteNode = new Node {
                Address = address,
                State = sourceState,
                Generation = sourceGeneration
            };
            while (remoteNode != null) {
                lock (nodes) {
                    // Process this remote node if:
                    //   1. we are aware of it, and
                    //   2. the incoming information is newer than the information we have, or
                    //   3. we have the same information, but the state has changed
                    if (nodes.TryGetValue(remoteNode.Address, out var node)
                        && (node.IsLaterGeneration(remoteNode.Generation)
                            || (node.Generation == remoteNode.Generation && node.IsSupersededState(remoteNode.State)))) {

                        // Stop escalation
                        if (node.State == NodeState.Alive && remoteNode.IsLaterGeneration(node.Generation)) {
                            // Remove any pending ACK
                            acks.TryRemove(node.Address, out var _);
                        }

                        // Update and notify
                        node.State = remoteNode.State;
                        node.Generation = remoteNode.Generation;
                        NodeUpdated?.Invoke(node);
                    } else if (node == null) {
                        node = remoteNode;
                        nodes.Add(node.Address, node);
                        NodeUpdated?.Invoke(node);
                    }

                    if (node.State != NodeState.Alive) {
                        acks.TryAdd(node.Address, DateTime.UtcNow);
                    }
                }

                // Process next node in the stream
                remoteNode = Node.ReadFrom(stream);
            }
        }

        private async Task ProcessRequest(MessageType messageType, IPAddress source, IPAddress destination, IPAddress relay) {
            switch (messageType) {
                case MessageType.Ping:
                    await SendMessageAsync(MessageType.Ack, source);
                    break;
                case MessageType.Ack:
                    acks.TryRemove(source, out var _);
                    break;
                case MessageType.RequestPing:
                    await RelayMessage(MessageType.RelayedPing, destination, source);
                    break;
                case MessageType.RequestAck:
                    await RelayMessage(MessageType.RelayedAck, destination, source);
                    break;
                case MessageType.RelayedPing:
                    await RequestMessage(MessageType.RequestAck, source, relay);
                    break;
                case MessageType.RelayedAck:
                    acks.TryRemove(source, out var _);
                    break;
            }
        }

        private async Task SendMessageAsync(MessageType messageType, IPAddress destination) {
            using (var stream = new MemoryStream(options.MaxPacketSize)) {
                stream.WriteProtocol();
                stream.WriteMessageType(messageType);
                WriteNodes(destination, stream);

                await transport.Send(destination, stream.GetBuffer(), stream.Position);
            }
        }

        private async Task RelayMessage(MessageType messageType, IPAddress destination, IPAddress source) {
            Trace.TraceInformation($"[Mesh] sending {messageType} to {destination} from {source}");
            using (var stream = new MemoryStream(options.MaxPacketSize)) {
                stream.WriteProtocol();
                stream.WriteMessageType(messageType);
                stream.WriteIP4Address(source);
                WriteNodes(destination, stream);

                await transport.Send(destination, stream.GetBuffer(), stream.Position);
            }
        }

        private async Task RequestMessage(MessageType messageType, IPAddress destination, IPAddress relay) {
            using (var stream = new MemoryStream(options.MaxPacketSize)) {
                stream.WriteProtocol();
                stream.WriteMessageType(messageType);
                stream.WriteIP4Address(destination);
                WriteNodes(relay, stream);

                await transport.Send(relay, stream.GetBuffer(), stream.Position);
            }
        }

        private void WriteNodes(IPAddress destination, Stream stream) {
            // First write the local node information
            stream.WriteState(localNode.State);
            stream.WriteGeneration(localNode.Generation);

            // Then write the destination node information, then all other known nodes
            lock (nodes) {
                // Destination node
                if (nodes.TryGetValue(destination, out var destinationNode)) {
                    stream.WriteState(destinationNode.State);
                    stream.WriteGeneration(destinationNode.Generation);
                } else {
                    stream.WriteState(NodeState.Alive);
                    stream.WriteGeneration(1);
                }

                // Remaining nodes, sorted by broadcast count, that are not in the 'dead' backoff period
                foreach (var remoteNode in nodes.Values
                    .OrderBy(n => n.Counter)
                    .Where(n => !(acks.TryGetValue(n.Address, out var ack) && DateTime.UtcNow > ack.Add(options.DeadBackoff)))
                    .Where(n => n.Address != destination)) {
                    remoteNode.WriteTo(stream);
                }
            }
        }
    }
}
