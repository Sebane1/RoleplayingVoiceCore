using System.Net;
using System.Net.Sockets;
using System.Numerics;

namespace FFXIVLooseTextureCompiler.Networking {
    public class NetworkedClient : IDisposable {
        private bool disposedValue;
        private bool connected;
        int connectionAttempts = 0;
        private string id;
        private string _ipAddress;

        public string Id { get => id; set => id = value; }
        public bool Connected { get => connected; set => connected = value; }
        public int Port { get { return 5105 + (connectionAttempts * 100); } }

        public event EventHandler OnSendFailed;
        public event EventHandler OnConnectionFailed;

        public NetworkedClient(string ipAddress) {
            _ipAddress = ipAddress;
        }
        public async void Start(TcpClient sendingClient) {
            try {
                sendingClient.LingerState = new LingerOption(false, 5);
                try {
                    sendingClient.Connect(new IPEndPoint(IPAddress.Parse(_ipAddress), Port));
                } catch {
                    if (connectionAttempts < 10) {
                        connectionAttempts++;
                    } else {
                        connectionAttempts = 0;
                    }
                    sendingClient.Connect(new IPEndPoint(IPAddress.Parse(_ipAddress), Port));
                }
                connected = true;
            } catch {
                connected = false;
            }
        }
        public void UpdateIPAddress(string ipAddress) {
            _ipAddress = ipAddress;
        }
        public async Task<bool> SendFile(string sendID, string path, Vector3 position) {
            try {
                TcpClient sendingClient = new TcpClient(new IPEndPoint(IPAddress.Any, Port));
                Start(sendingClient);
                using (FileStream fileStream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                    using (BinaryWriter writer = new(sendingClient.GetStream())) {
                        writer.Write(sendID);
                        writer.Write(0);
                        writer.Write(position.X);
                        writer.Write(position.Y);
                        writer.Write(position.Z);
                        writer.Write(fileStream.Length);

                        CopyStream(fileStream, writer.BaseStream, (int)fileStream.Length);

                        writer.Flush();
                        fileStream.Dispose();
                        Close(sendingClient);
                        return true;
                    }
                }
            } catch {
                connectionAttempts++;
                if (connectionAttempts <= 10) {
                    return await SendFile(sendID, path, position);
                } else {
                    OnSendFailed?.Invoke(this, EventArgs.Empty);
                    connectionAttempts = 0;
                }
            }
            return true;
        }

        private void Close(TcpClient sendingClient) {
            try {
                if (sendingClient != null) {
                    sendingClient.Client?.Shutdown(SocketShutdown.Both);
                    sendingClient.Client?.Disconnect(true);
                    sendingClient?.Close();
                    sendingClient?.Dispose();
                }
            } catch {

            }
            connected = false;
        }

        public async Task<KeyValuePair<Vector3, string>> GetFile(string sendID, string tempPath) {
            string path = Path.Combine(tempPath, sendID + ".mp3");
            Vector3 position = new Vector3(-1, -1, -1);
            try {
                TcpClient sendingClient = new TcpClient(new IPEndPoint(IPAddress.Any, Port));
                Start(sendingClient);
                BinaryWriter writer = new BinaryWriter(sendingClient.GetStream());
                BinaryReader reader = new BinaryReader(sendingClient.GetStream());
                writer.Write(sendID);
                writer.Write(1);

                byte value = reader.ReadByte();
                if (value != 0) {
                    position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    long length = reader.ReadInt64();
                    using (FileStream fileStream = new FileStream(path, FileMode.Create, FileAccess.Write)) {
                        CopyStream(reader.BaseStream, fileStream, (int)length);
                    }
                }
                Close(sendingClient);
            } catch {
                try {
                    connectionAttempts++;
                    if (connectionAttempts < 10) {
                        return await GetFile(sendID, tempPath);
                    } else {
                        connectionAttempts = 0;
                    }
                } catch {
                    connected = false;
                }
            }
            return new KeyValuePair<Vector3, string>(position, path);
        }

        public async Task<Vector3> GetPosition(string sendID) {
            Vector3 position = new Vector3(-1, -1, -1);
            try {
                TcpClient sendingClient = new TcpClient(new IPEndPoint(IPAddress.Any, Port));
                Start(sendingClient);
                BinaryWriter writer = new BinaryWriter(sendingClient.GetStream());
                BinaryReader reader = new BinaryReader(sendingClient.GetStream());
                writer.Write(sendID);
                writer.Write(2);
                byte value = reader.ReadByte();
                if (value == 1) {
                    position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                }
                Close(sendingClient);
                return position;
            } catch {
                try {
                    connectionAttempts++;
                    if (connectionAttempts < 10) {
                        return await GetPosition(sendID);
                    } else {
                        connectionAttempts = 0;
                    }
                } catch {
                    connected = false;
                }
            }
            return position;
        }

        public static void CopyStream(Stream input, Stream output, int bytes) {
            byte[] buffer = new byte[32768];
            int read;
            while (bytes > 0 &&
                   (read = input.Read(buffer, 0, Math.Min(buffer.Length, bytes))) > 0) {
                output.Write(buffer, 0, read);
                bytes -= read;
            }
        }
        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~NetworkedClient()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
