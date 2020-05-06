using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Mesh {
    interface ITransport {
        public IPAddress Address { get; }
        public Task<(IPAddress, byte[])> Receive(CancellationToken cancellationToken);

        public Task Broadcast(byte[] buffer, long length);
        public Task Send(IPAddress address, byte[] buffer, long length);
        public void Start();
        public void Stop();
    }
}
