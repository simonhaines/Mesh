using System;
using System.Collections.Generic;
using System.Text;

namespace Mesh {
    public class UdpTransport : ITransport {

        private class MeshServer : UdpServer {
            // Turn a concurrent queue into an async queue
            private readonly SemaphoreSlim semaphore;
            private readonly ConcurrentQueue<Tuple<IPAddress, byte[]>> queue;

            public MeshServer(IPAddress address, int port)
                : base(address, port) {
                queue = new ConcurrentQueue<Tuple<IPAddress, byte[]>>();
                semaphore = new SemaphoreSlim(0);
            }

            public async Task<(IPAddress, byte[])> Take(CancellationToken cancellationToken) {
                while (true) {
                    await semaphore.WaitAsync(cancellationToken);
                    if (queue.TryDequeue(out Tuple<IPAddress, byte[]> buffer)) {
                        return (buffer.Item1, buffer.Item2);
                    }
                }
            }

            protected override void OnStarted() {
                ReceiveAsync();
            }

            protected override void OnReceived(EndPoint endpoint, byte[] buffer, long offset, long size) {
                if (endpoint is IPEndPoint ip) {
                    var data = buffer;
                    if (offset != 0 || size != buffer.Length) {
                        data = new byte[size];
                        Array.Copy(buffer, offset, data, 0, size);
                    }
                    queue.Enqueue(new Tuple<IPAddress, byte[]>(ip.Address, data));
                    semaphore.Release(1);
                }
                ReceiveAsync();
            }
        }

        readonly MeshServer server;
        readonly int port;

        public UdpTransport(IPAddress address, int port) {
            server = new MeshServer(address, port);
            this.port = port;
        }

        public IPAddress Address {
            get => server.Endpoint.Address;
        }

        public Task<(IPAddress, byte[])> Receive(CancellationToken cancellationToken = default) {
            return server.Take(cancellationToken);
        }

        public Task Send(IPAddress address, byte[] buffer, long length) {
            var endPoint = new IPEndPoint(address, port);
            server.SendAsync(endPoint, buffer, 0, length);
            return Task.CompletedTask;
        }

        public Task Broadcast(byte[] buffer, long length) {
            server.MulticastAsync(buffer, 0, length);
            return Task.CompletedTask;
        }

        public void Start() {
            server.Start();
        }

        public void Stop() {
            server.Stop();
        }
    }
}
