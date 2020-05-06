using System.IO;
using System.Net;

namespace Mesh {
    internal static class Codec {
        public const byte ProtocolVersion = 1;

        public static bool CheckProtocol(this Stream stream) {
            return stream.ReadByte() == ProtocolVersion;
        }

        public static void WriteProtocol(this Stream stream) {
            if (stream.Position + 1 <= stream.Length) {
                stream.WriteByte(ProtocolVersion);
            }
        }

        public static MessageType ReadMessageType(this Stream stream) {
            return (MessageType)stream.ReadByte();
        }

        public static void WriteMessageType(this Stream stream, MessageType messageType) {
            if (stream.Position + 1 <= stream.Length) {
                stream.WriteByte((byte)messageType);
            }
        }

        public static IPAddress ReadIP4Address(this Stream stream) {
            byte[] buffer = new byte[4];
            stream.Read(buffer, 0, 4);
            return new IPAddress(buffer);
        }

        public static void WriteIP4Address(this Stream stream, IPAddress address) {
            var bytes = address.GetAddressBytes();
            if (stream.Position + bytes.Length <= stream.Length) {
                stream.Write(address.GetAddressBytes());
            }
        }

        public static NodeState ReadState(this Stream stream) {
            return (NodeState)stream.ReadByte();
        }

        public static void WriteState(this Stream stream, NodeState nodeState) {
            if (stream.Position + 1 <= stream.Length) {
                stream.WriteByte((byte)nodeState);
            }
        }

        public static byte ReadGeneration(this Stream stream) {
            return (byte)stream.ReadByte();
        }

        public static void WriteGeneration(this Stream stream, byte generation) {
            if (stream.Position + 1 <= stream.Length) {
                stream.WriteByte(generation);
            }
        }
    }
}
