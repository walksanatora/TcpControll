#nullable enable
using System.Collections.Generic;
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
    public class Class1 : MelonMod
    {
        private static List<NetworkStream> Streams = new() { };
        private static MelonLogger.Instance? logger;
        private static MelonPreferences_Category? Config;
        private static MelonPreferences_Entry<int>? Port;
        private static TcpListener? Listener;
        private static NamedPipeClientStream? _pipe;
        private static StreamReader? _pipeReader;
        private static StreamWriter? _pipeWriter;
        
        public override void OnInitializeMelon()
        {
            Config = MelonPreferences.CreateCategory("TcpConsole");
            Port = Config.CreateEntry("Port", 8120);
            
            logger = LoggerInstance;
            logger.Msg("TcpConsole has Loaded");

            var ipAddress = IPAddress.Parse("127.0.0.1");
            Listener = new TcpListener(ipAddress, Port.Value);
        
            Listener.Start();
            
            _pipe = new NamedPipeClientStream(".","SilicaDSPipe", PipeDirection.InOut, PipeOptions.Asynchronous);
            _pipeReader = new StreamReader(_pipe);
            _pipeWriter = new StreamWriter(_pipe);
            
            Task.Factory.StartNew(HandleConnections, TaskCreationOptions.LongRunning);
            Task.Factory.StartNew(TcpToPipe, TaskCreationOptions.LongRunning);
            Task.Factory.StartNew(PipeToTcp, TaskCreationOptions.LongRunning);
            
            base.OnInitializeMelon();
        }
        
        public override void OnApplicationQuit()
        {
            Listener?.Stop();
            foreach (var stream in Streams)
            {
                stream.Close();
            }
            _pipeReader?.Close();
            _pipeWriter?.Close();
            _pipe?.Close();
            base.OnApplicationQuit();
        }
        
        public void WriteMessages(string message)
        {
            List<int> toRemove = new () {};
            var bytes = Encoding.UTF8.GetBytes(message);
            foreach (var stream in Streams)
            {
                if (stream.CanWrite)
                {
                    stream.Write(bytes,0,bytes.Length);
                    stream.Flush();
                }
                else
                {
                    toRemove.Add(Streams.IndexOf(stream));
                }
            }

            List<NetworkStream> remap = toRemove.Select(i => Streams[i]).ToList<NetworkStream>();
            foreach (var snipe in remap)
            {
                Streams.Remove(snipe);
            }
            //yknow what. screw them. we ain't waiting for them to read our messages, which we cant do on linux anyways. gotta keep that xplat
        }
        private async void HandleConnections()
        {
            while (true)
            {
                var client = Listener!.AcceptTcpClient();
                var stream = client.GetStream();
                Streams.Add(stream);
            }
        }
        private async void TcpToPipe()
        {
            while (true)
            {
                foreach (var stream in Streams)
                {
                    var reader = new StreamReader((Stream) stream);
                    var line = reader.ReadLine();
                    if (!string.IsNullOrEmpty(line))
                    {
                        _pipeWriter?.Write(line+"\n");
                    }
                }
                _pipeWriter?.Flush();
            }
        }
        private async void PipeToTcp()
        {
            while (true)
            {
                var line = _pipeReader.ReadLine();
                if (!string.IsNullOrEmpty(line))
                {
                    WriteMessages(line);
                }
            }
        }
    }
}