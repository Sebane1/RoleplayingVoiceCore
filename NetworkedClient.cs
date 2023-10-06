using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Numerics;

namespace FFXIVLooseTextureCompiler.Networking {
    public class NetworkedClient : IDisposable {
        private bool disposedValue;
        private bool connected;
        int portCycle = 0;
        private string id;
        private string _ipAddress;
        private int connectionAttempts;
        const int maxPortCycle = 50;
        public string Id { get => id; set => id = value; }
        public bool Connected { get => connected; set => connected = value; }
        public int Port { get { return 5105 + (portCycle); } }

        public event EventHandler OnSendFailed;
        public event EventHandler OnConnectionFailed;

        public NetworkedClient(string ipAddress) {
            _ipAddress = ipAddress;
        }
        public async void Start(TcpClient sendingClient) {
            try {
                sendingClient.LingerState = new LingerOption(false, 0);
                try {
                    sendingClient.Connect(new IPEndPoint(IPAddress.Parse(_ipAddress), Port));
                } catch {
                    if (portCycle < maxPortCycle) {
                        portCycle++;
                    } else {
                        portCycle = 0;
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
        public async Task<bool> SendFile(string sendID, string path) {
            try {
                TcpClient sendingClient = new TcpClient(new IPEndPoint(IPAddress.Any, Port));
                Start(sendingClient);
                using (FileStream fileStream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                    using (BinaryWriter writer = new(sendingClient.GetStream())) {
                        writer.Write(sendID);
                        writer.Write(0);
                        writer.Write(fileStream.Length);

                        CopyStream(fileStream, writer.BaseStream, (int)fileStream.Length);

                        writer.Write(30000);
                        writer.Flush();
                        fileStream.Dispose();
                        Close(sendingClient);
                        return true;
                    }
                }
            } catch {
                portCycle++;
                if (portCycle > maxPortCycle) {
                    portCycle = 0;
                }
                connectionAttempts++;
                if (connectionAttempts < 20) {
                    return await SendFile(sendID, path);
                } else {
                    OnSendFailed?.Invoke(this, EventArgs.Empty);
                    connectionAttempts = 0;
                }
            }
            connectionAttempts = 0;
            return false;
        }
        private void AuditPathContents(string path) {
            if (Directory.Exists(path)) {
                foreach (string file in Directory.GetFiles(path)) {
                    if (!file.EndsWith(".mp3") && !file.EndsWith(".ogg")) {
                        File.Delete(file);
                    }
                }
            }
        }
        public async Task<bool> SendZip(string sendID, string path) {
            try {
                TcpClient sendingClient = new TcpClient(new IPEndPoint(IPAddress.Any, Port));
                Start(sendingClient);
                string zipPath = path + ".zip";
                if (File.Exists(zipPath)) {
                    File.Delete(zipPath);
                }
                AuditPathContents(path);
                ZipFile.CreateFromDirectory(path, zipPath);
                using (FileStream fileStream = new(path + ".zip", FileMode.Open, FileAccess.Read, FileShare.Read)) {
                    using (BinaryWriter writer = new(sendingClient.GetStream())) {
                        writer.Write(sendID);
                        writer.Write(0);
                        writer.Write(fileStream.Length);

                        CopyStream(fileStream, writer.BaseStream, (int)fileStream.Length);

                        writer.Write(3600000);
                        writer.Flush();
                        fileStream.Dispose();
                        Task.Run(() => Close(sendingClient));
                    }
                }
                File.Delete(zipPath);
                return true;
            } catch {
                portCycle++;
                if (portCycle > maxPortCycle) {
                    portCycle = 0;
                }
                connectionAttempts++;
                if (connectionAttempts < 20) {
                    return await SendZip(sendID, path);
                } else {
                    OnSendFailed?.Invoke(this, EventArgs.Empty);
                    connectionAttempts = 0;
                }
            }
            connectionAttempts = 0;
            return false;
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

        public async Task<KeyValuePair<Vector3, string>> GetFile(string sendID, string tempPath, string filename = "") {
            Random random = new Random();
            string path = Path.Combine(tempPath, (!string.IsNullOrEmpty(filename) ? filename : sendID) + ".mp3");
            Vector3 position = new Vector3(-1, -1, -1);
            Directory.CreateDirectory(tempPath);
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
                    portCycle++;
                    if (portCycle > maxPortCycle) {
                        portCycle = 0;
                    }
                    connectionAttempts++;
                    if (connectionAttempts < 20) {
                        return await GetFile(sendID, tempPath);
                    } else {
                        connectionAttempts = 0;
                        connected = false;
                    }
                } catch {
                    connected = false;
                }
            }
            connectionAttempts = 0;
            return new KeyValuePair<Vector3, string>(position, path);
        }

        public async Task<string> GetZip(string sendID, string tempPath) {
            Random random = new Random();
            string path = Path.Combine(tempPath, sendID + ".zip");
            string zipDirectory = tempPath + @"\" + sendID;
            try {
                TcpClient sendingClient = new TcpClient(new IPEndPoint(IPAddress.Any, Port));
                Start(sendingClient);
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
                    Directory.CreateDirectory(tempPath);
                    try {
                        if (File.Exists(zipDirectory)) {
                            File.Delete(zipDirectory);
                        }
                    } catch {

                    }
                    ZipFile.ExtractToDirectory(path, zipDirectory, true);
                    AuditPathContents(zipDirectory);
                    if (File.Exists(path)) {
                        File.Delete(path);
                    }
                }
                Close(sendingClient);
            } catch {
                try {
                    portCycle++;
                    if (portCycle > maxPortCycle) {
                        portCycle = 0;
                    }
                    connectionAttempts++;
                    if (connectionAttempts < 20) {
                        return await GetZip(sendID, tempPath);
                    } else {
                        connectionAttempts = 0;
                        connected = false;
                    }
                } catch {
                    connected = false;
                }
            }
            connectionAttempts = 0;
            return zipDirectory;
        }
        protected virtual bool IsFileLocked(string path) {
            try {
                using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) {
                    stream.Close();
                }
            } catch (IOException) {
                //the file is unavailable because it is:
                //still being written to
                //or being processed by another thread
                //or does not exist (has already been processed)
                return true;
            }

            //file is not locked
            return false;
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
