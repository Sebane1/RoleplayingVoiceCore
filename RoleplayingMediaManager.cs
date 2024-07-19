using AIDataProxy.XTTS;
using ElevenLabs;
using ElevenLabs.History;
using ElevenLabs.Models;
using ElevenLabs.User;
using ElevenLabs.Voices;
using FFXIVLooseTextureCompiler.Networking;
using NAudio.Lame;
using RoleplayingMediaCore.AudioRecycler;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RoleplayingMediaCore {
    public class RoleplayingMediaManager {
        private string _apiKey;
        private ElevenLabsClient? _api;
        private NetworkedClient _networkedClient;
        private CharacterVoices _characterVoices = new CharacterVoices();
        SubscriptionInfo _info = new SubscriptionInfo();
        public event EventHandler? VoicesUpdated;
        public event EventHandler<ValidationResult>? OnApiValidationComplete;
        public event EventHandler<VoiceFailure>? OnVoiceFailed;
        public event EventHandler<string> XTTSStatus;
        private IReadOnlyList<HistoryItem> _history;
        private bool apiValid;
        private string rpVoiceCache;
        private IReadOnlyList<Voice> _elevenlabsVoices;
        private Voice _elevenLabsVoice;
        private string _xttsVoice;
        private string _voiceTypeElevenlabs;

        private string _installPythonBathc = "call winget install -e -i --id=Python.Python.3.10 --source=winget --scope=machine";

        private string _batchInstall = "call winget install Microsoft.VisualStudio.2022.BuildTools --force --override \"--passive --wait --add Microsoft.VisualStudio.Workload.VCTools; include Recommended\"\r\n" +
            "call python -m pip install --upgrade pip\r\n" +
            "call pip3 install --upgrade pip\r\n" +
            "call pip install --upgrade pip setuptools wheel\r\n" +
            "call python -m venv venv\r\n" +
            "call venv\\Scripts\\activate\r\n" +
            "call pip install xtts-api-server\r\n" +
            "call pip install torch==2.1.1+cu118 torchaudio==2.1.1+cu118 --index-url https://download.pytorch.org/whl/cu118\r\n" +
            "call python -m xtts_api_server --deepspeed";

        private string _batchLaunch = "call venv\\Scripts\\activate.bat\r\n" +
            "call python -m xtts_api_server --deepspeed";
        private string[] _xttsVoices;
        private string _voiceTypeXTTS;
        private bool xttsAlreadyEnabled;
        private bool _xttsReady;
        private string _basePath;
        private string installBatchFile;
        private bool _pythonAutoInstalled;
        private string pythonBatchFile;

        public event EventHandler<string> InitializationCallbacks;
        public RoleplayingMediaManager(string apiKey, string cache, NetworkedClient client, CharacterVoices? characterVoices = null, EventHandler<string> initializationCallbacks = null) {
            rpVoiceCache = cache;
            _networkedClient = client;
            if (string.IsNullOrWhiteSpace(apiKey)) {
                apiValid = false;
            } else {
                _apiKey = apiKey;
                if (!string.IsNullOrEmpty(apiKey)) {
                    apiValid = true;
                }
                // Spin a new thread for this
                Task.Run(() => {
                    SetAPI(apiKey);
                });
                if (characterVoices != null) {
                    _characterVoices = characterVoices;
                }
            }
            InitializationCallbacks += initializationCallbacks;
            RefreshElevenlabsSubscriptionInfo();
            GetVoiceListElevenlabs();
            string batchScript = @"cd /d" + cache + "\r\n" + _batchInstall;
            installBatchFile = Path.Combine(cache, "install.bat");
            File.WriteAllText(installBatchFile, batchScript);
            batchScript = @"cd /d" + cache + "\r\n" + _installPythonBathc;
            pythonBatchFile = Path.Combine(cache, "pythonInstall.bat");
            File.WriteAllText(pythonBatchFile, batchScript);
        }
        public void InstallPython() {
            var processStart = new ProcessStartInfo(pythonBatchFile);
            processStart.RedirectStandardOutput = true;
            processStart.RedirectStandardError = true;
            processStart.WindowStyle = ProcessWindowStyle.Hidden;
            processStart.UseShellExecute = false;
            processStart.RedirectStandardInput = true;
            processStart.CreateNoWindow = true;
            Process process = new Process();
            process.StartInfo = processStart;
            process.OutputDataReceived += Process_OutputDataReceived;
            process.ErrorDataReceived += Process_ErrorDataReceived;
            process.EnableRaisingEvents = true;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            InitializationCallbacks?.Invoke(this, "[Roleplaying Voice Core] Python not detected, attempting to auto install.");
            process.WaitForExit();
            _pythonAutoInstalled = true;
        }
        public void InitializeXTTS() {
            Task.Run(() => {
                if (!xttsAlreadyEnabled) {
                    xttsAlreadyEnabled = true;
                    if (!Environment.GetEnvironmentVariable("Path").Contains("Python")) {
                        InstallPython();
                    }
                    if (!XTTSCommunicator.SetSpeakers(Path.Combine(rpVoiceCache, "speakers"))) {
                        if (!File.Exists(Path.Combine(rpVoiceCache, "xtts_models\\v2.0.2\\model.pth"))) {
                            InstallXTTS(rpVoiceCache);
                        } else {
                            LaunchXTTS(rpVoiceCache);
                        }
                    } else {
                        InitializationCallbacks?.Invoke(this, "[Roleplaying Voice Core] Player voices are ready!");
                        _xttsReady = true;
                    }
                }
            });
        }

        public void InstallXTTS(string cache) {
            InitializationCallbacks?.Invoke(this, "[Roleplaying Voice Core] Attempting to install C++ build tools please accept the UAC prompt that appears, as this dependency is required for XTTS installation to function. If the UAC prompt was rejected, restart Artemis to try again.");
            var processStart = new ProcessStartInfo(installBatchFile);
            processStart.RedirectStandardOutput = true;
            processStart.RedirectStandardError = true;
            processStart.WindowStyle = ProcessWindowStyle.Hidden;
            processStart.UseShellExecute = false;
            processStart.RedirectStandardInput = true;
            processStart.CreateNoWindow = true;
            Process process = new Process();
            process.StartInfo = processStart;
            process.OutputDataReceived += Process_OutputDataReceived;
            process.ErrorDataReceived += Process_ErrorDataReceived;
            process.EnableRaisingEvents = true;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            InitializationCallbacks?.Invoke(this, "[Roleplaying Voice Core] Installing dependencies, this may take a while. You can keep playing while you wait (we'll let you know when its done)");
        }

        private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e) {
            try {
                if (e != null) {
                    XTTSStatus?.Invoke(this, e.Data);
                    if (!string.IsNullOrEmpty(e.Data)) {
                        if (e.Data.Contains("Uvicorn running on")) {
                            InitializationCallbacks?.Invoke(this, "[Roleplaying Voice Core] Player voices are ready!");
                            _xttsReady = true;
                        }
                        if (e.Data.Contains("error: Microsoft Visual C++ 14.0 or greater is required.")) {
                            InitializationCallbacks?.Invoke(this, "[Roleplaying Voice Core] XTTS installation failed. Missing Microsoft C++ 14.0 or greater. Install that, and re-launch Artemis.");
                            _xttsReady = true;
                        }
                    }
                }
            } catch {

            }
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e) {
            try {
                if (XTTSStatus != null) {
                    XTTSStatus?.Invoke(this, e.Data);
                }
                if (e.Data.Contains("error: Microsoft Visual C++ 14.0 or greater is required.")) {
                    InitializationCallbacks?.Invoke(this, "[Roleplaying Voice Core] XTTS installation failed. Missing Microsoft C++ 14.0 or greater. Install that, and re-launch Artemis.");
                    _xttsReady = true;
                }
            } catch {

            }
        }

        public void LaunchXTTS(string cache) {
            UpdateBatch(cache);
            string folder = @"cd /d" + cache + "\r\n" + _batchLaunch;
            string batchFile = Path.Combine(cache, "launchXTTS.bat");
            File.WriteAllText(batchFile, folder);
            var processStart = new ProcessStartInfo(batchFile);
            processStart.RedirectStandardOutput = true;
            processStart.RedirectStandardError = true;
            processStart.WindowStyle = ProcessWindowStyle.Hidden;
            processStart.UseShellExecute = false;
            processStart.RedirectStandardInput = true;
            processStart.CreateNoWindow = true;
            Process process = new Process();
            process.StartInfo = processStart;
            process.OutputDataReceived += Process_OutputDataReceived;
            process.ErrorDataReceived += Process_ErrorDataReceived;
            process.EnableRaisingEvents = true;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            InitializationCallbacks?.Invoke(this, "[Roleplaying Voice Core] Initializing player voices.");
        }

        public void UpdateBatch(string cache) {
            string batch = $"@echo off\r\n\r\nrem This file is UTF-8 encoded, so we need to update the current code page while executing it\r\nfor /f \"tokens=2 delims=:.\" %%a in ('\"%SystemRoot%\\System32\\chcp.com\"') do (\r\n    set _OLD_CODEPAGE=%%a\r\n)\r\nif defined _OLD_CODEPAGE (\r\n    \"%SystemRoot%\\System32\\chcp.com\" 65001 > nul\r\n)\r\n\r\nset VIRTUAL_ENV={Path.Combine(cache, "venv\\")}\r\n\r\nif not defined PROMPT set PROMPT=$P$G\r\n\r\nif defined _OLD_VIRTUAL_PROMPT set PROMPT=%_OLD_VIRTUAL_PROMPT%\r\nif defined _OLD_VIRTUAL_PYTHONHOME set PYTHONHOME=%_OLD_VIRTUAL_PYTHONHOME%\r\n\r\nset _OLD_VIRTUAL_PROMPT=%PROMPT%\r\nset PROMPT=(venv) %PROMPT%\r\n\r\nif defined PYTHONHOME set _OLD_VIRTUAL_PYTHONHOME=%PYTHONHOME%\r\nset PYTHONHOME=\r\n\r\nif defined _OLD_VIRTUAL_PATH set PATH=%_OLD_VIRTUAL_PATH%\r\nif not defined _OLD_VIRTUAL_PATH set _OLD_VIRTUAL_PATH=%PATH%\r\n\r\nset PATH=%VIRTUAL_ENV%\\Scripts;%PATH%\r\nset VIRTUAL_ENV_PROMPT=(venv) \r\n\r\n:END\r\nif defined _OLD_CODEPAGE (\r\n    \"%SystemRoot%\\System32\\chcp.com\" %_OLD_CODEPAGE% > nul\r\n    set _OLD_CODEPAGE=\r\n)\r\n";
            File.WriteAllText(Path.Combine(cache, "venv\\Scripts\\activate.bat"), batch);
        }
        public CharacterVoices CharacterVoices { get => _characterVoices; set => _characterVoices = value; }
        public string ApiKey { get => _apiKey; set => _apiKey = value; }
        public SubscriptionInfo Info { get => _info; set => _info = value; }
        public NetworkedClient NetworkedClient { get => _networkedClient; set => _networkedClient = value; }
        public bool XttsReady { get => _xttsReady; set => _xttsReady = value; }
        public string BasePath { get => _basePath; set => _basePath = value; }

        public async Task<bool> ApiValidation(string key) {
            if (!string.IsNullOrWhiteSpace(key) && key.All(c => char.IsAsciiLetterOrDigit(c))) {
                var api = new ElevenLabsClient(key);
                apiValid = true;
                try {
                    await api.UserEndpoint.GetUserInfoAsync();
                } catch (Exception e) {
                    var errorMain = e.Message.ToString();
                    if (errorMain.Contains("invalid_api_key")) {
                        apiValid = false;
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(key)) {
                apiValid = false;
            }
            ValidationResult validationResult = new ValidationResult();
            validationResult.ValidationSuceeded = apiValid;
            OnApiValidationComplete?.Invoke(this, validationResult);
            if (apiValid) {
                return true;
            }
            return false;
        }

        public async Task<string[]> GetVoiceListElevenlabs() {
            ValidationResult state = new ValidationResult();
            List<string> voicesNames = new List<string>();
            if (_api != null) {
                int failure = 0;
                while (failure < 10) {
                    try {
                        _elevenlabsVoices = await _api.VoicesEndpoint.GetAllVoicesAsync();
                        break;
                    } catch (Exception e) {
                        var errorVoiceList = e.Message.ToString();
                        if (errorVoiceList.Contains("invalid_api_key")) {
                            apiValid = false;
                            state.ValidationState = 3;
                            OnApiValidationComplete?.Invoke(this, state);
                        } else {
                            failure++;
                        }
                    }
                }
            }
            voicesNames.Add("None");
            if (_elevenlabsVoices != null) {
                foreach (var voice in _elevenlabsVoices) {
                    voicesNames.Add(voice.Name);
                }
            }
            return voicesNames.ToArray();
        }
        public async Task<string[]> GetVoiceListXTTS() {
            ValidationResult state = new ValidationResult();
            List<string> voicesNames = new List<string>();
            _xttsVoices = Directory.GetFiles(Path.Combine(rpVoiceCache, "speakers"));
            voicesNames.Add("None");
            if (_xttsVoices != null) {
                foreach (var voice in _xttsVoices) {
                    voicesNames.Add(Path.GetFileNameWithoutExtension(voice));
                }
            }
            return voicesNames.ToArray();
        }
        public void SetAPI(string apiKey) {
            _api = new ElevenLabsClient(apiKey);
            apiValid = true;
            ValidationResult validationResult = new ValidationResult();
            validationResult.ValidationSuceeded = apiValid;
            OnApiValidationComplete?.Invoke(this, validationResult);
        }
        public void RefreshElevenlabsSubscriptionInfo() {
            Task.Run(async delegate {
                ValidationResult state = new ValidationResult();
                SubscriptionInfo? value = null;
                if (_api != null) {
                    int failure = 0;
                    while (failure < 10) {
                        try {
                            value = await _api.UserEndpoint.GetSubscriptionInfoAsync();
                            break;
                        } catch (Exception e) {
                            var errorSubInfo = e.Message.ToString();
                            if (errorSubInfo.Contains("invalid_api_key")) {
                                apiValid = false;
                                state.ValidationState = 3;
                                OnApiValidationComplete?.Invoke(this, state);
                            } else {
                                failure++;
                            }
                        }
                    }
                }
                _info = value;
            }
            );
        }

        public async void SetVoiceElevenlabs(string voiceType) {
            _voiceTypeElevenlabs = voiceType.ToLower();
            ValidationResult state = new ValidationResult();
            if (_api != null) {
                try {
                    _elevenlabsVoices = await _api.VoicesEndpoint.GetAllVoicesAsync();
                } catch (Exception e) {
                    var errorVoiceGen = e.Message.ToString();
                    if (errorVoiceGen.Contains("invalid_api_key")) {
                        apiValid = false;
                        state.ValidationState = 3;
                        OnApiValidationComplete?.Invoke(this, state);
                    }
                }
                if (_elevenlabsVoices != null) {
                    foreach (var voice in _elevenlabsVoices) {
                        if (voice.Name.ToLower().Contains(_voiceTypeElevenlabs)) {
                            if (voice != null) {
                                _elevenLabsVoice = voice;
                            }
                            break;
                        }
                    }
                }
            }
        }
        public async void SetVoiceXTTS(string voiceType) {
            _voiceTypeXTTS = voiceType.ToLower();
            ValidationResult state = new ValidationResult();
            if (_api != null) {
                try {
                    _xttsVoices = await GetVoiceListXTTS();
                } catch (Exception e) {
                    var errorVoiceGen = e.Message.ToString();
                    OnApiValidationComplete?.Invoke(this, state);
                }
                if (_xttsVoices != null) {
                    foreach (var voice in _xttsVoices) {
                        if (voice.ToLower().Contains(_voiceTypeXTTS)) {
                            if (voice != null) {
                                _xttsVoice = voice;
                            }
                            break;
                        }
                    }
                }
            }
        }

        public async Task<string> DoVoiceElevenlabs(string sender, string text,
            bool isEmote, float volume, Vector3 position, bool aggressiveSplicing, bool useSync) {
            string clipPath = "";
            string hash = Shai1Hash(sender + text);
            if (_elevenLabsVoice == null) {
                if (_elevenlabsVoices == null) {
                    await GetVoiceListElevenlabs();
                }
                if (_elevenlabsVoices != null) {
                    foreach (var voice in _elevenlabsVoices) {
                        if (voice.Name.ToLower().Contains(_voiceTypeElevenlabs)) {
                            if (voice != null) {
                                _elevenLabsVoice = voice;
                            }
                            break;
                        }
                    }
                }
            }

            if (_elevenLabsVoice != null) {
                try {
                    if (!text.StartsWith("(") && !text.EndsWith(")") && !(isEmote && (!text.Contains(@"""") || text.Contains(@"“")))) {
                        Directory.CreateDirectory(rpVoiceCache + @"\Outgoing");
                        string stitchedPath = Path.Combine(rpVoiceCache + @"\Outgoing", _elevenLabsVoice + "-" + hash + ".mp3");
                        if (!File.Exists(stitchedPath)) {
                            string trimmedText = TrimText(text);
                            string[] audioClips = (trimmedText.Contains(@"""") || trimmedText.Contains(@"“"))
                                ? ExtractQuotationsToList(trimmedText, aggressiveSplicing) : (aggressiveSplicing ? AggressiveWordSplicing(trimmedText) : new string[] { trimmedText });
                            List<string> audioPaths = new List<string>();
                            foreach (string audioClip in audioClips) {
                                audioPaths.Add(await GetVoicePathElevenlabs(_elevenLabsVoice.Name, audioClip, _elevenLabsVoice));
                            }
                            MemoryStream playbackStream = ConcatenateAudio(audioPaths.ToArray());
                            try {
                                using (Stream stitchedStream = File.OpenWrite(stitchedPath)) {
                                    playbackStream.Position = 0;
                                    playbackStream.CopyTo(stitchedStream);
                                    stitchedStream.Flush();
                                    stitchedStream.Close();
                                }
                            } catch (Exception e) {
                                OnVoiceFailed?.Invoke(this, new VoiceFailure() { FailureMessage = "Failed", Exception = e });
                            }
                        }
                        if (useSync) {
                            Task.Run(() => _networkedClient.SendFile(hash, stitchedPath));
                        }
                        clipPath = stitchedPath;
                        VoicesUpdated?.Invoke(this, EventArgs.Empty);
                    } else {
                        return "";
                    }

                } catch {

                }
            }
            return clipPath;
        }

        public async Task<string> DoVoiceXTTS(string sender, string text,
    bool isEmote, float volume, Vector3 position, bool aggressiveSplicing, bool useSync) {
            string clipPath = "";
            string hash = Shai1Hash(sender + text);
            if (_xttsVoice == null) {
                if (_xttsVoices == null) {
                    await GetVoiceListXTTS();
                }
                if (_xttsVoices != null) {
                    foreach (var voice in _xttsVoices) {
                        if (voice.ToLower().Contains(_voiceTypeXTTS)) {
                            if (voice != null) {
                                _xttsVoice = voice;
                            }
                            break;
                        }
                    }
                }
            }

            if (_xttsVoice != null) {
                try {
                    if (!text.StartsWith("(") && !text.EndsWith(")") && !(isEmote && (!text.Contains(@"""") || text.Contains(@"“")))) {
                        Directory.CreateDirectory(rpVoiceCache + @"\Outgoing");
                        string stitchedPath = Path.Combine(rpVoiceCache + @"\Outgoing", _xttsVoice + "-" + hash + ".mp3");
                        if (!File.Exists(stitchedPath)) {
                            string trimmedText = TrimText(text);
                            string[] audioClips = (trimmedText.Contains(@"""") || trimmedText.Contains(@"“"))
                                ? ExtractQuotationsToList(trimmedText, aggressiveSplicing) : (aggressiveSplicing ? AggressiveWordSplicing(trimmedText) : new string[] { trimmedText });
                            List<string> audioPaths = new List<string>();
                            foreach (string audioClip in audioClips) {
                                audioPaths.Add(await GetVoicePathXTTS(_xttsVoice, audioClip));
                            }
                            MemoryStream playbackStream = ConcatenateAudio(audioPaths.ToArray());
                            try {
                                using (Stream stitchedStream = File.OpenWrite(stitchedPath)) {
                                    playbackStream.Position = 0;
                                    playbackStream.CopyTo(stitchedStream);
                                    stitchedStream.Flush();
                                    stitchedStream.Close();
                                }
                            } catch (Exception e) {
                                OnVoiceFailed?.Invoke(this, new VoiceFailure() { FailureMessage = "Failed", Exception = e });
                            }
                        }
                        if (useSync) {
                            Task.Run(() => _networkedClient.SendFile(hash, stitchedPath));
                        }
                        clipPath = stitchedPath;
                        VoicesUpdated?.Invoke(this, EventArgs.Empty);
                    } else {
                        return "";
                    }

                } catch {

                }
            }
            return clipPath;
        }

        public async Task<bool> SendSound(string sender, string identifier, string soundOnDisk, float volume, Vector3 position) {
            string hash = Shai1Hash(sender + identifier);
            bool sendState = false;
            await Task.Run(async () => { sendState = await _networkedClient.SendFile(hash, soundOnDisk); });
            return sendState;
        }

        public async Task<bool> SendZip(string sender, string soundOnDisk) {
            string hash = Shai1Hash(sender);
            bool sendState = false;
            await Task.Run(async () => { sendState = await _networkedClient.SendZip(hash, soundOnDisk); });
            return sendState;
        }

        public async Task<bool> SendShort(string sender, ushort shortvalue) {
            string hash = Shai1Hash(sender);
            bool sendState = false;
            await Task.Run(async () => { sendState = await _networkedClient.SendShort(hash, shortvalue); });
            return sendState;
        }
        public async Task<ushort> GetShort(string sender) {
            if (_networkedClient != null) {
                string hash = Shai1Hash(sender);
                return await _networkedClient.GetShort(hash);
            }
            return 0;
        }

        private async Task<string> GetVoicePathElevenlabs(string voiceType, string trimmedText, Voice characterVoice) {
            string audioPath = "";
            var defaultVoiceSettings = new VoiceSettings(0.3f, 1);
            try {
                if (!CharacterVoices.VoiceCatalogue.ContainsKey(voiceType)) {
                    CharacterVoices.VoiceCatalogue[voiceType] = new Dictionary<string, string>();
                }
                if (!CharacterVoices.VoiceCatalogue[(voiceType)].ContainsKey(trimmedText.ToLower())) {
                    audioPath = await GetVoiceFromElevenLabs(trimmedText, voiceType, defaultVoiceSettings, characterVoice);
                } else if (File.Exists(CharacterVoices.VoiceCatalogue[(voiceType)][trimmedText.ToLower()])) {
                    audioPath = CharacterVoices.VoiceCatalogue[(voiceType)][trimmedText.ToLower()];
                } else {
                    CharacterVoices.VoiceCatalogue[(voiceType)].Remove(trimmedText.ToLower());
                    audioPath = await GetVoiceFromElevenLabs(trimmedText, voiceType, defaultVoiceSettings, characterVoice);
                }
            } catch (Exception e) {
                OnVoiceFailed?.Invoke(this, new VoiceFailure() { FailureMessage = "Failed", Exception = e });
            }
            return audioPath;
        }
        private async Task<string> GetVoicePathXTTS(string voiceType, string trimmedText) {
            string audioPath = "";
            try {
                if (!CharacterVoices.VoiceCatalogue.ContainsKey(voiceType)) {
                    CharacterVoices.VoiceCatalogue[voiceType] = new Dictionary<string, string>();
                }
                if (!CharacterVoices.VoiceCatalogue[(voiceType)].ContainsKey(trimmedText.ToLower())) {
                    audioPath = await GetVoiceFromXTTS(trimmedText, voiceType);
                } else if (File.Exists(CharacterVoices.VoiceCatalogue[(voiceType)][trimmedText.ToLower()])) {
                    audioPath = CharacterVoices.VoiceCatalogue[(voiceType)][trimmedText.ToLower()];
                } else {
                    CharacterVoices.VoiceCatalogue[(voiceType)].Remove(trimmedText.ToLower());
                    audioPath = await GetVoiceFromXTTS(trimmedText, voiceType);
                }
            } catch (Exception e) {
                OnVoiceFailed?.Invoke(this, new VoiceFailure() { FailureMessage = "Failed", Exception = e });
            }
            return audioPath;
        }

        private async Task<string> GetVoiceFromElevenLabs(string trimmedText, string voiceType,
            VoiceSettings defaultVoiceSettings, Voice characterVoice) {
            string unquotedText = trimmedText.Replace(@"""", null);
            string numberAdjusted = char.IsDigit(unquotedText.Last()) ? unquotedText + "." : unquotedText;
            string finalText = @"""" + numberAdjusted + @"""";
            string audioPath = "";
            bool foundInHistory = false;
            try {
                if (!foundInHistory) {
                    audioPath = await _api.TextToSpeechEndpoint
                        .TextToSpeechAsync(finalText, characterVoice,
                        defaultVoiceSettings, new Model("eleven_turbo_v2"), rpVoiceCache);
                }
                CharacterVoices.VoiceCatalogue[(voiceType)].Add(trimmedText.ToLower(), audioPath);
            } catch {

            }
            return audioPath;
        }

        private async Task<string> GetVoiceFromXTTS(string trimmedText, string voiceType) {
            string unquotedText = trimmedText.Replace(@"""", null);
            string numberAdjusted = char.IsDigit(unquotedText.Last()) ? unquotedText + "." : unquotedText;
            string finalText = @"""" + numberAdjusted + @"""";
            string audioPath = "";
            bool foundInHistory = false;
            try {
                byte[] data = null;
                if (!foundInHistory) {
                    while (data == null || data.Length == 0) {
                        LameDLL.LoadNativeDLL(_basePath);
                        data = await XTTSCommunicator.GetAudioAlternate(voiceType, finalText, Path.Combine(rpVoiceCache, "speakers"));
                        Directory.CreateDirectory(Path.Combine(rpVoiceCache, "XTTS\\" + voiceType + "\\"));
                        audioPath = Path.Combine(rpVoiceCache, "XTTS\\" + voiceType + "\\" + Guid.NewGuid() + ".mp3");
                        await File.WriteAllBytesAsync(audioPath, data);
                    }
                }
                CharacterVoices.VoiceCatalogue[(voiceType)].Add(trimmedText.ToLower(), audioPath);
            } catch {

            }
            return audioPath;
        }

        private string TrimText(string text) {
            string newText = text;
            var wordReplacements = new Dictionary<string, string>
            {
                {"XD", "ahahaha" },
                {"lmao", "ahahaha" },
                {"lol", "ahahaha" },
                {"lmfao", "ahahaha" },
                {"kek", "ahahaha" },
                {"kekw", "ahahaha" },
                {"rotflmao", "ahahaha" },
                {"rotflmfao", "ahahaha" },
                {":D", "." },
                {":P", "." },
                {":3", "." },
                {"<3", "love" }
            };
            foreach (var word in wordReplacements) {
                newText = Regex.Replace(newText, $@"(?<=^|\s){word.Key}(?=\s|$)", word.Value, RegexOptions.IgnoreCase);
            }
            foreach (char character in @"@#$%^&*()_+{}\/<>|`~".ToCharArray()) {
                newText = newText.Replace(character + "", null);
            }
            return newText;
        }

        public MemoryStream ConcatenateAudio(params string[] mp3filenames) {
            MemoryStream output = new MemoryStream();
            foreach (string filename in mp3filenames) {
                if (File.Exists(filename)) {
                    using (Stream stream = File.OpenRead(filename)) {
                        stream.CopyTo(output);
                    }
                }
            }
            return output;
        }

        private string ExtractQuotations(string text) {
            string newText = "";
            string[] strings = null;
            if (text.Contains(@"""")) {
                strings = text.Split('"');
            } else {
                strings = text.Split('“');
            }
            if (strings.Length > 1) {
                for (int i = 0; i < strings.Length; i++) {
                    if ((i + 1) % 2 == 0) {
                        newText += strings[i] + (strings.Length % 2 == 0 ? " -- " : "");
                    }
                }
                return newText;
            } else {
                return text;
            }
        }

        private string[] ExtractQuotationsToList(string text, bool aggressiveSplicing) {
            string newText = "";
            string[] strings = null;
            List<string> quotes = new List<string>();
            if (text.Contains(@"""")) {
                strings = text.Split('"');
            } else {
                strings = text.Split('“');
            }
            if (strings.Length > 1) {
                for (int i = 0; i < strings.Length; i++) {
                    if ((i + 1) % 2 == 0) {
                        if (aggressiveSplicing) {
                            quotes.AddRange(AggressiveWordSplicing(strings[i].Replace("\"", null).Replace("“", null).Replace(",", " - ")));
                        } else {
                            quotes.Add(strings[i].Replace("\"", null).Replace("“", null).Replace(",", " - "));
                        }
                    }
                }
            } else {
                quotes.Add(text);
            }
            return quotes.ToArray();
        }

        private string[] AggressiveWordSplicing(string text) {
            string[] strings = null;
            List<string> quotes = new List<string>();
            strings = text.Split(' ');
            string temp = "";
            for (int i = 0; i < strings.Length; i++) {
                temp += strings[i] + " ";
                if (strings[i].Contains(",") || (strings[i].Contains(".") && !strings[i].Contains("..."))
                    || strings[i].Contains("!") || strings[i].Contains("?") || strings[i].Contains(";")
                    || strings[i].Contains(":") || strings[i].Contains("·")) {
                    quotes.Add(temp.Replace("\"", null).Replace("“", null).Replace(",", "").Trim());
                    temp = "";
                }
            }
            if (!string.IsNullOrEmpty(temp)) {
                quotes.Add(temp.Replace("\"", null).Replace("“", null).Replace(",", "").Trim());
            }
            return quotes.ToArray();
        }

        public static string CreateMD5(string input) {
            // Use input string to calculate MD5 hash
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create()) {
                byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                return Convert.ToHexString(hashBytes); // .NET 5 +
            }
        }
        public static string Shai1Hash(string input) {
            using var sha1 = SHA1.Create();
            return Convert.ToHexString(sha1.ComputeHash(Encoding.UTF8.GetBytes(input)));
        }

        public async Task<string> GetSound(string sender, string identifier, float volume,
            Vector3 centerPosition, bool isShoutYell, string? subDirectory = null, bool ignoreCache = false) {
            string path = "";
            if (_networkedClient != null) {
                KeyValuePair<Vector3, string> data = new KeyValuePair<Vector3, string>();
                Guid id = Guid.NewGuid();
                string hash = Shai1Hash(sender + identifier);
                string localPath = Path.Combine(rpVoiceCache + subDirectory, (!ignoreCache ? hash : id) + ".mp3");
                if (!File.Exists(localPath) || ignoreCache) {
                    data = await _networkedClient.GetFile(hash, rpVoiceCache + subDirectory, id.ToString());
                    path = data.Value;
                } else {
                    path = localPath;
                }
            }
            return path;
        }
        public async Task<string> GetZip(string sender, string path) {
            if (_networkedClient != null) {
                string hash = Shai1Hash(sender);
                return await _networkedClient.GetZip(hash, path);
            }
            return "";
        }
    }

    public class ValidationResult : EventArgs {
        public bool ValidationSuceeded { get; set; }
        // ValidationState 3 is meant for api calls failed when they shouldn't have
        // Meaning somehow an invalid key slipped by the validation, or it got invalidated by outside sources
        // Right now the plugin isn't set up to actually make use of it, and this needs to be thought through
        public int ValidationState { get; set; }
    }
    public class VoiceFailure : EventArgs {
        string _failureMessage;
        Exception _exception;

        public string FailureMessage { get => _failureMessage; set => _failureMessage = value; }
        public Exception Exception { get => _exception; set => _exception = value; }
    }
}