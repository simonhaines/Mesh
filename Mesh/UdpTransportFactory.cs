using System.Net;

namespace Mesh {
    class UdpTransportFactory {
        public IPAddress Address { get; set; }
        public int Port { get; set; }

        public ITransport Create() {
            return new UdpTransport(Address, Port);
        }
    }
}
