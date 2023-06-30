using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;

namespace FFXIVLooseTextureCompiler.Networking {
    public class NetworkedClient : IDisposable {
        private bool disposedValue;
        private bool connected;
        private TcpClient sendingClient;
        int connectionAttempts = 0;
        private string id;
        private string _ipAddress;

        public string Id { get => id; set => id = value; }
        public bool Connected { get => connected; set => connected = value; }
        public event EventHandler OnSendFailed;
        public event EventHandler OnConnectionFailed;

        public NetworkedClient(string ipAddress) {
            try {
                sendingClient = new TcpClient(new IPEndPoint(IPAddress.Any, 5400));
                sendingClient.LingerState = new LingerOption(false, 5);
            } catch {

            }
            _ipAddress = ipAddress;
        }
        public void Start() {
            try {
                if (sendingClient == null) {
                    sendingClient = new TcpClient(new IPEndPoint(IPAddress.Any, 5400));
                    sendingClient.LingerState = new LingerOption(false, 5);
                }
                try {
                    sendingClient.Connect(new IPEndPoint(IPAddress.Parse(_ipAddress), 5400));
                } catch {
                    sendingClient.Connect(new IPEndPoint(IPAddress.Parse(_ipAddress), 5400));
                }
                connected = true;
            } catch {
                connected = false;
            }
        }

        public async Task<bool> SendFile(string sendID, string path) {
            if (connected) {
                try {
                    using FileStream fileStream = new(path, FileMode.Open, FileAccess.Read);
                    BinaryWriter writer = new(sendingClient.GetStream());

                    writer.Write(sendID);
                    writer.Write(0);
                    writer.Write(fileStream.Length);

                    CopyStream(fileStream, writer.BaseStream, (int)fileStream.Length);

                    writer.Flush();
                    fileStream.Dispose();
                    return true;
                } catch {
                    Close();
                    connectionAttempts++;

                    if (connectionAttempts >= 10) {
                        return await SendFile(sendID, path);
                    } else {
                        OnSendFailed?.Invoke(this, EventArgs.Empty);
                    }
                }
            } else {
                try {
                    Start();

                    connectionAttempts++;
                    if (connectionAttempts >= 10) {
                        return await SendFile(sendID, path);
                    } else {
                        OnConnectionFailed?.Invoke(this, EventArgs.Empty);
                    }
                } catch {

                }
            }
            return true;
        }


        private void Close() {
            sendingClient.Client.Shutdown(SocketShutdown.Both);
            sendingClient.Client.Disconnect(true);
            sendingClient.Close();
            connected = false;
        }

        public async Task<string> GetFile(string sendID, string tempPath) {
        sendMod:
            string path = Path.Combine(tempPath, sendID + ".mp3");
            try {
                BinaryWriter writer = new BinaryWriter(sendingClient.GetStream());
                BinaryReader reader = new BinaryReader(sendingClient.GetStream());
                writer.Write(sendID);
                writer.Write(1);
                byte value = reader.ReadByte();
                if (value != 0) {
                    long length = reader.ReadInt64();
                    using (FileStream fileStream = new FileStream(path, FileMode.Create, FileAccess.Write)) {
                        CopyStream(reader.BaseStream, fileStream, (int)length);
                    }
                }
            } catch {
                if (sendingClient != null) {
                    if (sendingClient.Client != null) {
                        try {
                            sendingClient.Client.Shutdown(SocketShutdown.Both);
                            sendingClient.Client.Disconnect(true);
                            sendingClient.Close();
                        } catch {

                        }
                    }
                }
                try {
                    sendingClient = new TcpClient(new IPEndPoint(IPAddress.Any, 5400));
                    sendingClient.Connect(new IPEndPoint(IPAddress.Parse(_ipAddress), 5400));
                    goto sendMod;
                } catch {
                    connected = false;
                }
            }
            return path;
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
                if (disposing) {
                    try {
                        sendingClient.Client.Shutdown(SocketShutdown.Both);
                        sendingClient.Close();
                        sendingClient.Dispose();
                    } catch {

                    }
                }

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
