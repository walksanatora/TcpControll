#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using MelonLoader;
using TcpControl;
[assembly: MelonInfo(typeof(Class1), "TcpControl", "1.0.0", "walksanator")]
[assembly: MelonGame("Bohemia Interactive", "Silica")]

namespace TcpControl {
    [SuppressMessage("ReSharper", "FunctionNeverReturns")]
    public class Class1 : MelonMod
    {
        private static readonly List<TcpClient> Clients = [];
        private static MelonLogger.Instance? _logger;
        private static MelonPreferences_Category? _config;
        private static MelonPreferences_Entry<int>? _port;
        private static TcpListener? _listener;
        private static NamedPipeClientStream? _pipe;
        private static StreamReader? _pipeReader;
        private static StreamWriter? _pipeWriter;
        
        public override void OnInitializeMelon()
        {
            _logger = LoggerInstance;
            _logger.Msg("TcpControl is loading");
            
            _config = MelonPreferences.CreateCategory("TcpConsole");
            _port = _config.CreateEntry("Port", 8120);

            _logger.Msg("Created Configs");
            
            var ipAddress = IPAddress.Parse("127.0.0.1");
            _listener = new TcpListener(ipAddress, _port.Value);
            _listener.Start();
            
            _logger.Msg("started TCP listener");
            _logger.Msg(_listener);

            
            _pipe = new NamedPipeClientStream(".","SilicaDSPipe", PipeDirection.InOut, PipeOptions.Asynchronous);
            _pipeReader = new StreamReader(_pipe);
            _pipeWriter = new StreamWriter(_pipe);

            _logger.Msg("created named pipe stream");
            _logger.Msg(_pipeReader);
            _logger.Msg(_pipeWriter);
            
            Task.Factory.StartNew(HandleConnections, TaskCreationOptions.LongRunning);
            Task.Factory.StartNew(TcpToPipe, TaskCreationOptions.LongRunning);
            Task.Factory.StartNew(PipeToTcp, TaskCreationOptions.LongRunning);

            _logger.Msg("Spawned threads");
            _logger.Msg("TcpConsole init finished");
        }
        
        public override void OnApplicationQuit()
        {
            _logger?.Msg("stopping TcpControl");
            _listener?.Stop();
            foreach (var stream in Clients)
            {
                stream.Close();
                stream.Dispose();
            }
            _pipeReader?.Close();
            _pipeReader?.Dispose();
            _pipeWriter?.Close();
            _pipeReader?.Dispose();
            _pipe?.Close();
            _pipe?.Dispose();
            _logger?.Msg("Stopped TcpControl");
        }

        private static void WriteMessages(string message)
        {
            _logger?.Msg("Clearing dead connections");
            Clients.RemoveAll(client => !client.Connected);
            _logger?.Msg($"transmitting message over tcp: {message}");
            List<int> toRemove = [];
            var bytes = Encoding.UTF8.GetBytes(message);
            foreach (var stream in from client in Clients where client.Connected select client.GetStream())
            {
                stream.Write(bytes,0,bytes.Length);
                stream.Flush();
            } 
        }
        
        private static async void HandleConnections()
        {
            _logger?.Msg("Started TCP connection acceptor thread");
            while (true)
            {
                _logger?.Msg("Ready to accept tcp connections");
                var client = await _listener!.AcceptTcpClientAsync();
                _logger?.Msg("New TCP client connected");
                Clients.Add(client);
            }
        }
        private static async void TcpToPipe()
        {
            _logger?.Msg("started TCP to Pipe proxy");
            while (true)
            {
                var writeCount = 0;
                foreach (var reader in Clients.Select(client => new StreamReader(client.GetStream())))
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(line)) continue;
                    _logger?.Msg($"received message from Tcp: {line}");
                    await _pipeWriter!.WriteAsync(line+"\n");
                    writeCount += 1;
                }
                if (writeCount <= 0) continue;
                await _pipeWriter!.FlushAsync();
                _logger?.Msg($"written from {writeCount} pipes");
            }
        }
        private static async void PipeToTcp()
        {
            _logger?.Msg("started Pipe to TCP");
            while (true)
            {
                var line = await _pipeReader!.ReadLineAsync();
                if (string.IsNullOrEmpty(line)) continue;
                WriteMessages(line);
                _logger?.Msg("finished dispatching messages to TCP connections");
            }
        }
    }
}