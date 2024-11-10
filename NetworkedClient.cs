using Newtonsoft.Json;
using RoleplayingVoiceCore;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Numerics;

namespace FFXIVLooseTextureCompiler.Networking {
    public class NetworkedClient : IDisposable {
        private bool disposedValue;
        private bool connected;
        private string id;
        private string _ipAddress;
        int sendAttempt = 0;
        private int connectionAttempts;
        private bool alreadySendingFiles;

        public string Id { get => id; set => id = value; }
        public bool Connected { get => connected; set => connected = value; }
        public int Port { get { return 5105; } }

        public event EventHandler OnSendFailed;
        public event EventHandler<FailureMessage> OnConnectionFailed;

        public NetworkedClient(string ipAddress) {
            _ipAddress = ipAddress;
        }
        public void UpdateIPAddress(string ipAddress) {
            _ipAddress = ipAddress;
        }
        public async Task<bool> SendFile(string sendID, string path) {
            try {
                using (HttpClient httpClient = new HttpClient()) {
                    httpClient.BaseAddress = new Uri("http://" + _ipAddress + ":" + Port);
                    MemoryStream memory = new MemoryStream();
                    using (FileStream fileStream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                        using (BinaryWriter writer = new(memory)) {
                            writer.Write(sendID);
                            writer.Write(0);
                            writer.Write(fileStream.Length);

                            fileStream.CopyTo(writer.BaseStream);

                            writer.Write(60000);
                            writer.Flush();
                            memory.Position = 0;
                            var post = await httpClient.PostAsync(httpClient.BaseAddress, new StreamContent(memory));
                            if (post.StatusCode != HttpStatusCode.OK) {
                                OnConnectionFailed.Invoke(this, new FailureMessage() { Message = "Upload failed" });
                            }
                            return true;
                        }
                    }
                }
            } catch (Exception e) {
                SendFile(sendID, path);
                if (sendAttempt > 10) {
                    sendAttempt++;
                    OnConnectionFailed.Invoke(this, new FailureMessage() { Message = e.Message });
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
        public async Task<ushort> GetShort(string sendID) {
            try {
                using (HttpClient httpClient = new HttpClient()) {
                    httpClient.BaseAddress = new Uri("http://" + _ipAddress + ":" + Port);
                    httpClient.Timeout = new TimeSpan(1, 0, 0);
                    MemoryStream memory = new MemoryStream();
                    BinaryWriter writer = new BinaryWriter(memory);
                    writer.Write(sendID);
                    writer.Write(1);
                    memory.Position = 0;
                    var post = await httpClient.PostAsync(httpClient.BaseAddress, new StreamContent(memory));
                    if (post.StatusCode == HttpStatusCode.OK) {
                        BinaryReader reader = new BinaryReader(await post.Content.ReadAsStreamAsync());
                        byte value = reader.ReadByte();
                        if (value != 0) {
                            long length = reader.ReadInt64();
                            return reader.ReadUInt16();
                        }
                    }
                }
            } catch {

            }
            return ushort.MaxValue - 1;
        }
        public async Task<bool> SendShort(string sendID, ushort shortValue) {
            try {
                using (HttpClient httpClient = new HttpClient()) {
                    httpClient.BaseAddress = new Uri("http://" + _ipAddress + ":" + Port);
                    MemoryStream memory = new MemoryStream();
                    MemoryStream fileStream = new MemoryStream();
                    BinaryWriter shortWriter = new(fileStream);
                    shortWriter.Write(shortValue);
                    shortWriter.Flush();
                    fileStream.Position = 0;
                    using (BinaryWriter writer = new(memory)) {
                        writer.Write(sendID);
                        writer.Write(0);
                        writer.Write(fileStream.Length);
                        fileStream.CopyTo(writer.BaseStream);
                        writer.Write(5000);
                        writer.Flush();
                        fileStream.Flush();
                        memory.Position = 0;
                        var post = await httpClient.PostAsync(httpClient.BaseAddress, new StreamContent(memory));
                        if (post.StatusCode != HttpStatusCode.OK) {

                        }
                    }
                }
                return true;
            } catch {

            }
            connectionAttempts = 0;
            return false;
        }

        public async Task<bool> SendZip(string sendID, string path) {
            if (!alreadySendingFiles) {
                alreadySendingFiles = true;
                try {
                    using (HttpClient httpClient = new HttpClient()) {
                        httpClient.BaseAddress = new Uri("http://" + _ipAddress + ":" + Port);
                        MemoryStream memory = new MemoryStream();
                        string zipPath = path + ".zip";
                        if (File.Exists(zipPath)) {
                            File.Delete(zipPath);
                        }
                        AuditPathContents(path);
                        ZipFile.CreateFromDirectory(path, zipPath);
                        using (FileStream fileStream = new(path + ".zip", FileMode.Open, FileAccess.Read, FileShare.Read)) {
                            using (BinaryWriter writer = new(memory)) {
                                writer.Write(sendID);
                                writer.Write(0);
                                writer.Write(fileStream.Length);

                                fileStream.CopyTo(writer.BaseStream);
                                fileStream.Flush();
                                writer.Write(3600000);
                                writer.Flush();
                                memory.Position = 0;
                                var post = await httpClient.PostAsync(httpClient.BaseAddress, new StreamContent(memory));
                                if (post.StatusCode != HttpStatusCode.OK) {

                                }
                                fileStream.Dispose();
                            }
                        }
                        File.Delete(zipPath);
                        return true;
                    }
                } catch {

                }
                alreadySendingFiles = false;
            } else {
                alreadySendingFiles = false;
            }
            connectionAttempts = 0;
            return false;
        }

        public async Task<KeyValuePair<Vector3, string>> GetFile(string sendID, string tempPath, string filename = "") {
            Random random = new Random();
            string path = Path.Combine(tempPath, (!string.IsNullOrEmpty(filename) ? filename : sendID) + ".mp3");
            Vector3 position = new Vector3(-1, -1, -1);
            Directory.CreateDirectory(tempPath);
            try {
                using (HttpClient httpClient = new HttpClient()) {
                    httpClient.BaseAddress = new Uri("http://" + _ipAddress + ":" + Port);
                    MemoryStream memory = new MemoryStream();
                    BinaryWriter writer = new BinaryWriter(memory);

                    writer.Write(sendID);
                    writer.Write(1);
                    memory.Position = 0;
                    var post = await httpClient.PostAsync(httpClient.BaseAddress, new StreamContent(memory));
                    if (post.StatusCode == HttpStatusCode.OK) {
                        BinaryReader reader = new BinaryReader(await post.Content.ReadAsStreamAsync());
                        byte value = reader.ReadByte();
                        if (value != 0) {
                            position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            long length = reader.ReadInt64();
                            using (FileStream fileStream = new FileStream(path, FileMode.Create, FileAccess.Write)) {
                                reader.BaseStream.CopyTo(fileStream);
                            }
                        }
                    }
                }
            } catch (Exception e) {
                OnConnectionFailed.Invoke(this, new FailureMessage() { Message = e.Message });
            }
            connectionAttempts = 0;
            return new KeyValuePair<Vector3, string>(position, path);
        }

        public async Task<string> GetZip(string sendID, string tempPath) {
            Random random = new Random();
            string path = Path.Combine(tempPath, sendID + ".zip");
            string zipDirectory = tempPath + @"\" + sendID;
            try {
                using (HttpClient httpClient = new HttpClient()) {
                    httpClient.BaseAddress = new Uri("http://" + _ipAddress + ":" + Port);
                    MemoryStream memory = new MemoryStream();
                    BinaryWriter writer = new BinaryWriter(memory);


                    writer.Write(sendID);
                    writer.Write(1);
                    memory.Position = 0;
                    var post = await httpClient.PostAsync(httpClient.BaseAddress, new StreamContent(memory));
                    if (post.StatusCode == HttpStatusCode.OK) {
                        BinaryReader reader = new BinaryReader(await post.Content.ReadAsStreamAsync());
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
                    }
                }
            } catch {

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

    public class FailureMessage : EventArgs {
        string _message;

        public string Message { get => _message; set => _message = value; }
    }
}
