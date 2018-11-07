using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Net.Sockets;
using System.IO;
using System.Threading;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using Logger;
using static TheArena.ClientConnection;

namespace TheArena
{
    public partial class Runner
    {
        static void StartFTPServer(bool is_host)
        {
            try
            {
                Log.TraceMessage(Log.Nav.NavIn, "Starting FTP Server", Log.LogType.Info);
                if (!Directory.Exists(ARENA_FILES_PATH))
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Arena File Directory didn't exist.. creating now", Log.LogType.Info);
                    Directory.CreateDirectory(ARENA_FILES_PATH);
                }
                if (is_host)
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Starting FTP Server on Host Address", Log.LogType.Info);
                    server = new FtpServer(IPAddress.Parse(HOST_ADDR), HOST_PORT, ARENA_FILES_PATH);
                }
                else
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Starting FTP Server on Any Address", Log.LogType.Info);
                    server = new FtpServer(IPAddress.Any, HOST_PORT, ARENA_FILES_PATH);
                }
                server.Start();
                if (is_host)
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Started -- now waiting for commands forever on this thread", Log.LogType.Info);
                    while (true)
                    {
                        Console.WriteLine("Type T2 to start a tourney with 2 people per game. T3 to start a tourney with 3 people per game etc.");
                        string command = Console.ReadLine();
                        if (command == "T2")
                        {
                            StartTourney(2);
                        }
                    }
                }
            }
            catch (Exception er)
            {
                Log.TraceMessage(Log.Nav.NavOut, er.StackTrace + "", Log.LogType.Error);
            }
        }

    }

    public class RateLimitingStream : Stream
    {
        private Stream _baseStream;
        private System.Diagnostics.Stopwatch _watch;
        private readonly int _speedLimit;
        private long _transmitted;
        private readonly double _resolution;

        public RateLimitingStream(Stream baseStream, int speedLimit)
            : this(baseStream, speedLimit, 1)
        {
        }

        public RateLimitingStream(Stream baseStream, int speedLimit, double resolution)
        {
            _baseStream = baseStream;
            _watch = new System.Diagnostics.Stopwatch();
            _speedLimit = speedLimit;
            _resolution = resolution;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (!_watch.IsRunning)
            {
                _watch.Start();
            }

            int dataSent = 0;

            while (_speedLimit > 0 && _transmitted >= (_speedLimit * _resolution))
            {
                Thread.Sleep(10);

                if (_watch.ElapsedMilliseconds > (1000 * _resolution))
                {
                    _transmitted = 0;
                    _watch.Restart();
                }
            }

            _baseStream.Write(buffer, offset, count);
            _transmitted += count;
            dataSent += count;

            if (_watch.ElapsedMilliseconds > (1000 * _resolution))
            {
                _transmitted = 0;
                _watch.Restart();
            }
        }

        public override bool CanRead
        {
            get { return false; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return true; }
        }

        public override void Flush()
        {
            _baseStream.Flush();
        }

        public override long Length
        {
            get { return _baseStream.Length; }
        }

        public override long Position
        {
            get
            {
                return _baseStream.Position;
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        protected override void Dispose(bool disposing)
        {
            _watch.Stop();

            base.Dispose(disposing);
        }
    }

    public class ClientConnection : IDisposable
    {

        private class DataConnectionOperation
        {
            public Func<NetworkStream, string, string> Operation { get; set; }
            public string Arguments { get; set; }
        }

        private static long CopyStream(Stream input, Stream output, int bufferSize)
        {
            byte[] buffer = new byte[bufferSize];
            int count = 0;
            long total = 0;

            while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, count);
                total += count;
            }

            return total;
        }

        private static long CopyStreamAscii(Stream input, Stream output, int bufferSize)
        {
            char[] buffer = new char[bufferSize];
            int count = 0;
            long total = 0;

            using (StreamReader rdr = new StreamReader(input, Encoding.ASCII))
            {
                using (StreamWriter wtr = new StreamWriter(output, Encoding.ASCII))
                {
                    while ((count = rdr.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        wtr.Write(buffer, 0, count);
                        total += count;
                    }
                }
            }

            return total;
        }

        private long CopyStream(Stream input, Stream output)
        {
            Stream limitedStream = output; // new RateLimitingStream(output, 131072, 0.5);

            if (_connectionType == TransferType.Image)
            {
                return CopyStream(input, limitedStream, 4096);
            }
            else
            {
                return CopyStreamAscii(input, limitedStream, 4096);
            }
        }

        private enum TransferType
        {
            Ascii,
            Ebcdic,
            Image,
            Local,
        }

        private enum FormatControlType
        {
            NonPrint,
            Telnet,
            CarriageControl,
        }

        private enum DataConnectionType
        {
            Passive,
            Active,
        }

        private enum FileStructureType
        {
            File,
            Record,
            Page,
        }

        private bool _disposed = false;

        private TcpListener _passiveListener;

        private TcpClient _controlClient;
        private TcpClient _dataClient;

        private NetworkStream _controlStream;
        private StreamReader _controlReader;
        private StreamWriter _controlWriter;

        private TransferType _connectionType = TransferType.Ascii;
        private DataConnectionType _dataConnectionType = DataConnectionType.Active;


        private string _username;
        private string _root;
        private string _currentDirectory;
        private IPEndPoint _dataEndpoint;
        private IPEndPoint _remoteEndPoint;

        private X509Certificate _cert = null;
        private SslStream _sslStream;

        private string _clientIP;
        private readonly string direct;

        private List<string> _validCommands;

        public ClientConnection(TcpClient client, string directory)
        {
            _controlClient = client;
            Log.TraceMessage(Log.Nav.NavIn, "ClientConnection created with directory: " + directory, Log.LogType.Info);
            direct = directory;
            _validCommands = new List<string>();
        }

        public void TimeoutToEndPassiveListener()
        {
            Log.TraceMessage(Log.Nav.NavIn, "Started Timeout to End Passive Listener", Log.LogType.Info);
            Thread.Sleep(15000);
            if (_passiveListener != null && _passiveListener.Server.IsBound)
            {
                Log.TraceMessage(Log.Nav.NavIn, "15 Seconds up ending Passive Listener", Log.LogType.Info);
                _passiveListener.Stop();
            }
        }

        public void HandleClient(object obj)
        {
            _remoteEndPoint = (IPEndPoint)_controlClient.Client.RemoteEndPoint;

            _clientIP = _remoteEndPoint.Address.ToString();

            Log.TraceMessage(Log.Nav.NavIn, "Client connecting from: " + _clientIP, Log.LogType.Info);

            _controlStream = _controlClient.GetStream();

            _controlReader = new StreamReader(_controlStream);
            _controlWriter = new StreamWriter(_controlStream);

            _controlWriter.WriteLine("220 Service Ready.");
            _controlWriter.Flush();

            _validCommands.AddRange(new string[] { "AUTH", "USER", "PASS", "QUIT", "HELP", "NOOP" });

            string line;

            _dataClient = new TcpClient();

            string renameFrom = null;

            try
            {
                while ((line = _controlReader.ReadLine()) != null)
                {
                    string response = null;

                    string[] command = line.Split(' ');

                    string cmd = command[0].ToUpperInvariant();
                    string arguments = command.Length > 1 ? line.Substring(command[0].Length + 1) : null;

                    Log.TraceMessage(Log.Nav.NavIn, "Client sent message: " + cmd, Log.LogType.Info);

                    if (arguments != null && arguments.Trim().Length == 0)
                    {
                        arguments = null;
                    }

                    if (cmd != "RNTO")
                    {
                        renameFrom = null;
                    }

                    if (response == null)
                    {
                        switch (cmd)
                        {
                            case "USER":
                                response = User(arguments);
                                //If the QUIT Command is never reached, this will end the passiveListener after 7 seconds so the port is not blocked when we try to access it in the next transaction.
                                Thread myThread = new Thread(new ThreadStart(TimeoutToEndPassiveListener));
                                myThread.Start();
                                break;
                            case "PASS":
                                response = Password(arguments);
                                break;
                            case "CWD":
                                response = ChangeWorkingDirectory(arguments);
                                break;
                            case "CDUP":
                                response = ChangeWorkingDirectory("..");
                                break;
                            case "QUIT":
                                if (_passiveListener != null)
                                {
                                    _passiveListener.Stop();
                                }
                                response = "221 Service closing control connection";
                                break;
                            case "REIN":
                                _username = null;
                                _passiveListener = null;
                                _dataClient = null;

                                response = "220 Service ready for new user";
                                break;
                            case "PORT":
                                response = Port(arguments);
                                break;
                            case "PASV":
                                response = Passive();
                                //logEntry.SPort = ((IPEndPoint)_passiveListener.LocalEndpoint).Port.ToString();
                                break;
                            case "TYPE":
                                response = Type(command[1], command.Length == 3 ? command[2] : null);
                                break;
                            case "STRU":
                                response = Structure(arguments);
                                break;
                            case "MODE":
                                response = Mode(arguments);
                                break;
                            case "RNFR":
                                renameFrom = arguments;
                                response = "350 Requested file action pending further information";
                                break;
                            case "RNTO":
                                response = Rename(renameFrom, arguments);
                                break;
                            case "DELE":
                                response = Delete(arguments);
                                break;
                            case "RMD":
                                response = RemoveDir(arguments);
                                break;
                            case "MKD":
                                response = CreateDir(arguments);
                                break;
                            case "PWD":
                                response = PrintWorkingDirectory();
                                break;
                            case "RETR":
                                response = Retrieve(arguments);
                                break;
                            case "STOR":
                                response = Store(arguments);
                                break;
                            case "STOU":
                                response = StoreUnique();
                                break;
                            case "APPE":
                                response = Append(arguments);
                                break;
                            case "LIST":
                                response = List(arguments ?? _currentDirectory);
                                break;
                            case "SYST":
                                response = "215 UNIX Type: L8";
                                break;
                            case "NOOP":
                                response = "200 OK";
                                break;
                            case "ACCT":
                                response = "200 OK";
                                break;
                            case "ALLO":
                                response = "200 OK";
                                break;
                            case "NLST":
                                response = "502 Command not implemented";
                                break;
                            case "SITE":
                                response = "502 Command not implemented";
                                break;
                            case "STAT":
                                response = "502 Command not implemented";
                                break;
                            case "HELP":
                                response = "502 Command not implemented";
                                break;
                            case "SMNT":
                                response = "502 Command not implemented";
                                break;
                            case "REST":
                                response = "502 Command not implemented";
                                break;
                            case "ABOR":
                                response = "502 Command not implemented";
                                break;

                            // Extensions defined by rfc 2228
                            case "AUTH":
                                response = Auth(arguments);
                                break;

                            // Extensions defined by rfc 2389
                            case "FEAT":
                                response = FeatureList();
                                break;
                            case "OPTS":
                                response = Options(arguments);
                                break;

                            // Extensions defined by rfc 3659
                            case "MDTM":
                                response = FileModificationTime(arguments);
                                break;
                            case "SIZE":
                                response = FileSize(arguments);
                                break;

                            // Extensions defined by rfc 2428
                            case "EPRT":
                                response = EPort(arguments);
                                break;
                            case "EPSV":
                                Log.TraceMessage(Log.Nav.NavNone, "HereEPSV1", Log.LogType.Info);
                                response = EPassive();
                                Log.TraceMessage(Log.Nav.NavNone, "HereEPSV2", Log.LogType.Info);
                                break;

                            default:
                                response = "502 Command not implemented";
                                break;
                        }
                    }

                    if (_controlClient == null || !_controlClient.Connected)
                    {
                        break;
                    }
                    else
                    {
                        Log.TraceMessage(Log.Nav.NavOut, "Sending Response " + response, Log.LogType.Info);
                        _controlWriter.WriteLine(response);
                        _controlWriter.Flush();

                        if (response.StartsWith("221"))
                        {
                            break;
                        }

                        if (cmd == "AUTH")
                        {
                            _cert = new X509Certificate("server.cer");

                            _sslStream = new SslStream(_controlStream);

                            _sslStream.AuthenticateAsServer(_cert);

                            _controlReader = new StreamReader(_sslStream);
                            _controlWriter = new StreamWriter(_sslStream);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.TraceMessage(Log.Nav.NavIn, ex);
            }

            Dispose();
        }

        private bool IsPathValid(string path)
        {
            return path.StartsWith(_root);
        }

        private string NormalizeFilename(string path)
        {
            if (path == null)
            {
                path = string.Empty;
            }

            if (path == "/")
            {
                return _root;
            }
            else if (path.StartsWith("/"))
            {
                path = new FileInfo(Path.Combine(_root, path.Substring(1))).FullName;
            }
            else
            {
                path = new FileInfo(Path.Combine(_currentDirectory, path)).FullName;
            }

            return IsPathValid(path) ? path : null;
        }

        private string FeatureList()
        {
            _controlWriter.WriteLine("211- Extensions supported:");
            _controlWriter.WriteLine(" MDTM");
            _controlWriter.WriteLine(" SIZE");
            return "211 End";
        }

        private string Options(string arguments)
        {
            return "200 Looks good to me...";
        }

        private string Auth(string authMode)
        {
            if (authMode == "TLS")
            {
                return "234 Enabling TLS Connection";
            }
            else
            {
                return "504 Unrecognized AUTH mode";
            }
        }

        private string User(string username)
        {
            _username = username;

            return "331 Username ok, need password";
        }

        private string Password(string password)
        {

            _root = direct;
            _currentDirectory = _root;

            return "230 User logged in";
        }

        private string ChangeWorkingDirectory(string pathname)
        {
            if (pathname == "/")
            {
                _currentDirectory = _root;
            }
            else
            {
                string newDir;

                if (pathname.StartsWith("/"))
                {
                    pathname = pathname.Substring(1).Replace('/', '\\');
                    newDir = Path.Combine(_root, pathname);
                }
                else
                {
                    pathname = pathname.Replace('/', '\\');
                    newDir = Path.Combine(_currentDirectory, pathname);
                }

                if (Directory.Exists(newDir))
                {
                    _currentDirectory = new DirectoryInfo(newDir).FullName;

                    if (!IsPathValid(_currentDirectory))
                    {
                        _currentDirectory = _root;
                    }
                }
                else
                {
                    _currentDirectory = _root;
                }
            }

            return "250 Changed to new directory";
        }

        private string Port(string hostPort)
        {
            _dataConnectionType = DataConnectionType.Active;

            string[] ipAndPort = hostPort.Split(',');

            byte[] ipAddress = new byte[4];
            byte[] port = new byte[2];

            for (int i = 0; i < 4; i++)
            {
                ipAddress[i] = Convert.ToByte(ipAndPort[i]);
            }

            for (int i = 4; i < 6; i++)
            {
                port[i - 4] = Convert.ToByte(ipAndPort[i]);
            }

            if (BitConverter.IsLittleEndian)
                Array.Reverse(port);

            _dataEndpoint = new IPEndPoint(new IPAddress(ipAddress), BitConverter.ToInt16(port, 0));

            return "200 Data Connection Established";
        }

        private string EPort(string hostPort)
        {
            _dataConnectionType = DataConnectionType.Active;

            char delimiter = hostPort[0];

            string[] rawSplit = hostPort.Split(new char[] { delimiter }, StringSplitOptions.RemoveEmptyEntries);

            char ipType = rawSplit[0][0];

            string ipAddress = rawSplit[1];
            string port = rawSplit[2];

            _dataEndpoint = new IPEndPoint(IPAddress.Parse(ipAddress), int.Parse(port));

            return "200 Data Connection Established";
        }

        private string Passive()
        {
            _dataConnectionType = DataConnectionType.Passive;

            if (_passiveListener == null)
            {
                IPAddress localIp = ((IPEndPoint)_controlClient.Client.LocalEndPoint).Address;
                Log.TraceMessage(Log.Nav.NavIn, "Creating new Passive Listener at " + localIp + ":" + 65432, Log.LogType.Info);
                _passiveListener = new TcpListener(localIp, 65432);
                Log.TraceMessage(Log.Nav.NavOut, "Starting the Listener", Log.LogType.Info);
                _passiveListener.Start();
            }
            else
            {
                Log.TraceMessage(Log.Nav.NavOut, "Passive Listener already set up->Continuing", Log.LogType.Info);
            }


            IPEndPoint passiveListenerEndpoint = (IPEndPoint)_passiveListener.LocalEndpoint;

            byte[] address = passiveListenerEndpoint.Address.GetAddressBytes();
            short port = (short)passiveListenerEndpoint.Port;

            byte[] portArray = BitConverter.GetBytes(port);

            if (BitConverter.IsLittleEndian)
                Array.Reverse(portArray);

            return string.Format("227 Entering Passive Mode ({0},{1},{2},{3},{4},{5})", address[0], address[1], address[2], address[3], portArray[0], portArray[1]);

            //return "502 Not Implemented";
        }

        private string EPassive()
        {

            _dataConnectionType = DataConnectionType.Passive;

            if (_passiveListener == null)
            {
                IPAddress localIp = ((IPEndPoint)_controlClient.Client.LocalEndPoint).Address;
                Log.TraceMessage(Log.Nav.NavIn, "Creating new Passive Listener at " + localIp + ":" + 65432, Log.LogType.Info);
                _passiveListener = new TcpListener(localIp, 65432);
                Log.TraceMessage(Log.Nav.NavOut, "Starting the Listener", Log.LogType.Info);
                _passiveListener.Start();
            }
            else
            {
                Log.TraceMessage(Log.Nav.NavOut, "Passive Listener already set up->Continuing", Log.LogType.Info);
            }



            return string.Format("229 Entering Extended Passive Mode (|||{0}|)", 65432);
        }

        private string Type(string typeCode, string formatControl)
        {
            switch (typeCode.ToUpperInvariant())
            {
                case "A":
                    _connectionType = TransferType.Ascii;
                    break;
                case "I":
                    _connectionType = TransferType.Image;
                    break;
                default:
                    return "504 Command not implemented for that parameter";
            }

            if (!string.IsNullOrWhiteSpace(formatControl))
            {
                switch (formatControl.ToUpperInvariant())
                {
                    case "N":
                        break;
                    default:
                        return "504 Command not implemented for that parameter";
                }
            }

            return string.Format("200 Type set to {0}", _connectionType);
        }

        private string Delete(string pathname)
        {
            pathname = NormalizeFilename(pathname);

            if (pathname != null)
            {
                if (File.Exists(pathname))
                {
                    File.Delete(pathname);
                }
                else
                {
                    return "550 File Not Found";
                }

                return "250 Requested file action okay, completed";
            }

            return "550 File Not Found";
        }

        private string RemoveDir(string pathname)
        {
            pathname = NormalizeFilename(pathname);

            if (pathname != null)
            {
                if (Directory.Exists(pathname))
                {
                    Directory.Delete(pathname);
                }
                else
                {
                    return "550 Directory Not Found";
                }

                return "250 Requested file action okay, completed";
            }

            return "550 Directory Not Found";
        }

        private string CreateDir(string pathname)
        {
            pathname = NormalizeFilename(pathname);

            if (pathname != null)
            {
                if (!Directory.Exists(pathname))
                {
                    Directory.CreateDirectory(pathname);
                }
                else
                {
                    return "550 Directory already exists";
                }

                return "250 Requested file action okay, completed";
            }

            return "550 Directory Not Found";
        }

        private string FileModificationTime(string pathname)
        {
            pathname = NormalizeFilename(pathname);

            if (pathname != null)
            {
                if (File.Exists(pathname))
                {
                    return string.Format("213 {0}", File.GetLastWriteTime(pathname).ToString("yyyyMMddHHmmss.fff"));
                }
            }

            return "550 File Not Found";
        }

        private string FileSize(string pathname)
        {
            pathname = NormalizeFilename(pathname);

            if (pathname != null)
            {
                if (File.Exists(pathname))
                {
                    long length = 0;

                    using (FileStream fs = File.Open(pathname, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        length = fs.Length;
                    }

                    return string.Format("213 {0}", length);
                }
            }

            return "550 File Not Found";
        }

        private string Retrieve(string pathname)
        {
            pathname = NormalizeFilename(pathname);

            if (pathname != null)
            {
                if (File.Exists(pathname))
                {
                    var state = new DataConnectionOperation { Arguments = pathname, Operation = RetrieveOperation };

                    SetupDataConnectionOperation(state);

                    return string.Format("150 Opening {0} mode data transfer for RETR", _dataConnectionType);
                }
            }

            return "550 File Not Found";
        }

        private string Store(string pathname)
        {
            pathname = NormalizeFilename(pathname);

            if (pathname != null)
            {
                var state = new DataConnectionOperation { Arguments = pathname, Operation = StoreOperation };

                Log.TraceMessage(Log.Nav.NavIn, "Setting up Data Connection Operation ", Log.LogType.Info);
                SetupDataConnectionOperation(state);
                Log.TraceMessage(Log.Nav.NavNone, "HERESTOR1", Log.LogType.Info);
                return string.Format("150 Opening {0} mode data transfer for STOR", _dataConnectionType);
            }

            return "450 Requested file action not taken";
        }

        private string Append(string pathname)
        {
            pathname = NormalizeFilename(pathname);

            if (pathname != null)
            {
                var state = new DataConnectionOperation { Arguments = pathname, Operation = AppendOperation };

                SetupDataConnectionOperation(state);

                return string.Format("150 Opening {0} mode data transfer for APPE", _dataConnectionType);
            }

            return "450 Requested file action not taken";
        }

        private string StoreUnique()
        {
            string pathname = NormalizeFilename(new Guid().ToString());

            var state = new DataConnectionOperation { Arguments = pathname, Operation = StoreOperation };

            SetupDataConnectionOperation(state);

            return string.Format("150 Opening {0} mode data transfer for STOU", _dataConnectionType);
        }

        private string PrintWorkingDirectory()
        {
            string current = _currentDirectory.Replace(_root, string.Empty).Replace('\\', '/');

            if (current.Length == 0)
            {
                current = "/";
            }

            return string.Format("257 \"{0}\" is current directory.", current); ;
        }

        private string List(string pathname)
        {
            pathname = NormalizeFilename(pathname);

            if (pathname != null)
            {
                var state = new DataConnectionOperation { Arguments = pathname, Operation = ListOperation };

                SetupDataConnectionOperation(state);

                return string.Format("150 Opening {0} mode data transfer for LIST", _dataConnectionType);
            }

            return "450 Requested file action not taken";
        }

        private string Structure(string structure)
        {
            switch (structure)
            {
                case "F":
                    break;
                case "R":
                case "P":
                    return string.Format("504 STRU not implemented for \"{0}\"", structure);
                default:
                    return string.Format("501 Parameter {0} not recognized", structure);
            }

            return "200 Command OK";
        }

        private string Mode(string mode)
        {
            if (mode.ToUpperInvariant() == "S")
            {
                return "200 OK";
            }
            else
            {
                return "504 Command not implemented for that parameter";
            }
        }

        private string Rename(string renameFrom, string renameTo)
        {
            if (string.IsNullOrWhiteSpace(renameFrom) || string.IsNullOrWhiteSpace(renameTo))
            {
                return "450 Requested file action not taken";
            }

            renameFrom = NormalizeFilename(renameFrom);
            renameTo = NormalizeFilename(renameTo);

            if (renameFrom != null && renameTo != null)
            {
                if (File.Exists(renameFrom))
                {
                    File.Move(renameFrom, renameTo);
                }
                else if (Directory.Exists(renameFrom))
                {
                    Directory.Move(renameFrom, renameTo);
                }
                else
                {
                    return "450 Requested file action not taken";
                }

                return "250 Requested file action okay, completed";
            }

            return "450 Requested file action not taken";
        }

        private void HandleAsyncResult(IAsyncResult result)
        {
            if (_dataConnectionType == DataConnectionType.Active)
            {
                Log.TraceMessage(Log.Nav.NavIn, "DataConnectionType was Active (We are supposed to be the Client)-Ending the Connection ", Log.LogType.Info);
                _dataClient.EndConnect(result);
            }
            else
            {
                Log.TraceMessage(Log.Nav.NavIn, "Client Successfully Connected so we stop accepting new clients ", Log.LogType.Info);
                _dataClient = _passiveListener.EndAcceptTcpClient(result);
            }
        }

        private void SetupDataConnectionOperation(DataConnectionOperation state)
        {
            if (_dataConnectionType == DataConnectionType.Active)
            {
                Log.TraceMessage(Log.Nav.NavIn, "DataConnectionType is Active (We are the Client)-Setting up TCPClient ", Log.LogType.Info);
                _dataClient = new TcpClient(_dataEndpoint.AddressFamily);
                Log.TraceMessage(Log.Nav.NavIn, "About to Begin Connect to " + _dataEndpoint.Address.ToString(), Log.LogType.Info);
                _dataClient.BeginConnect(_dataEndpoint.Address, _dataEndpoint.Port, DoDataConnectionOperation, state);
                Log.TraceMessage(Log.Nav.NavIn, "We began connected to " + _dataEndpoint.Address.ToString(), Log.LogType.Info);
            }
            else
            {
                Log.TraceMessage(Log.Nav.NavIn, "DataConnectionType is NOT Active (We are the Host)-Setting up Host ", Log.LogType.Info);
                _passiveListener.BeginAcceptTcpClient(DoDataConnectionOperation, state);
                Log.TraceMessage(Log.Nav.NavIn, "Here2Store ", Log.LogType.Info);

            }
        }

        private void DoDataConnectionOperation(IAsyncResult result)
        {
            Log.TraceMessage(Log.Nav.NavIn, "Handling Async Result ", Log.LogType.Info);
            HandleAsyncResult(result);

            DataConnectionOperation op = result.AsyncState as DataConnectionOperation;

            string response;

            using (NetworkStream dataStream = _dataClient.GetStream())
            {
                Log.TraceMessage(Log.Nav.NavIn, "DataStream Created-Starting the Operation ", Log.LogType.Info);
                response = op.Operation(dataStream, op.Arguments);
            }

            _dataClient.Close();
            _dataClient = null;

            _controlWriter.WriteLine(response);
            _controlWriter.Flush();
        }

        private string RetrieveOperation(NetworkStream dataStream, string pathname)
        {
            long bytes = 0;
            Log.TraceMessage(Log.Nav.NavOut, "Retrieve Operation Starting ", Log.LogType.Info);
            using (FileStream fs = new FileStream(pathname, FileMode.Open, FileAccess.Read))
            {
                bytes = CopyStream(fs, dataStream);
            }
            Log.TraceMessage(Log.Nav.NavIn, "Transfer Complete: bytes= " + bytes, Log.LogType.Info);
            return "226 Closing data connection, file transfer successful";
        }

        private string StoreOperation(NetworkStream dataStream, string pathname)
        {
            long bytes = 0;
            Log.TraceMessage(Log.Nav.NavOut, "Store Operation Starting ", Log.LogType.Info);
            try
            {
                Log.TraceMessage(Log.Nav.NavOut, "Writing to " + pathname, Log.LogType.Info);
                using (FileStream fs = new FileStream(pathname, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan))
                {
                    bytes = CopyStream(dataStream, fs);
                    Log.TraceMessage(Log.Nav.NavOut, "Copied from stream " + bytes + " bytes.", Log.LogType.Info);
                }
            }
            catch (Exception er)
            {
                Log.TraceMessage(Log.Nav.NavIn, er);
            }
            Log.TraceMessage(Log.Nav.NavIn, "Store Operation Successful: bytes= " + bytes, Log.LogType.Info);

            return "226 Closing data connection, file transfer successful";
        }

        private string AppendOperation(NetworkStream dataStream, string pathname)
        {
            long bytes = 0;
            Log.TraceMessage(Log.Nav.NavOut, "Append Operation Starting ", Log.LogType.Info);
            using (FileStream fs = new FileStream(pathname, FileMode.Append, FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan))
            {
                bytes = CopyStream(dataStream, fs);
            }
            Log.TraceMessage(Log.Nav.NavIn, "Append Operation Completed: bytes= " + bytes, Log.LogType.Info);

            return "226 Closing data connection, file transfer successful";
        }

        private string ListOperation(NetworkStream dataStream, string pathname)
        {
            StreamWriter dataWriter = new StreamWriter(dataStream, Encoding.ASCII);

            IEnumerable<string> directories = Directory.EnumerateDirectories(pathname);

            foreach (string dir in directories)
            {
                DirectoryInfo d = new DirectoryInfo(dir);

                string date = d.LastWriteTime < DateTime.Now - TimeSpan.FromDays(180) ?
                    d.LastWriteTime.ToString("MMM dd  yyyy") :
                    d.LastWriteTime.ToString("MMM dd HH:mm");

                string line = string.Format("drwxr-xr-x    2 2003     2003     {0,8} {1} {2}", "4096", date, d.Name);

                dataWriter.WriteLine(line);
                dataWriter.Flush();
            }

            IEnumerable<string> files = Directory.EnumerateFiles(pathname);

            foreach (string file in files)
            {
                FileInfo f = new FileInfo(file);

                string date = f.LastWriteTime < DateTime.Now - TimeSpan.FromDays(180) ?
                    f.LastWriteTime.ToString("MMM dd  yyyy") :
                    f.LastWriteTime.ToString("MMM dd HH:mm");

                string line = string.Format("-rw-r--r--    2 2003     2003     {0,8} {1} {2}", f.Length, date, f.Name);

                dataWriter.WriteLine(line);
                dataWriter.Flush();
            }

            return "226 Transfer complete";
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_controlClient != null)
                    {
                        _controlClient.Close();
                    }

                    if (_dataClient != null)
                    {
                        _dataClient.Close();
                    }

                    if (_controlStream != null)
                    {
                        _controlStream.Close();
                    }

                    if (_controlReader != null)
                    {
                        _controlReader.Close();
                    }

                    if (_controlWriter != null)
                    {
                        _controlWriter.Close();
                    }
                }
            }

            _disposed = true;
        }

        public class FtpServer : IDisposable
        {

            private bool _disposed = false;
            private bool _listening = false;
            private string directory = "";
            private TcpListener _listener;
            private List<ClientConnection> _activeConnections;

            private readonly IPEndPoint _localEndPoint;

            public FtpServer()
                : this(IPAddress.Parse("172.22.22.104"), 3000, "C:\\")
            {
            }

            public FtpServer(IPAddress ipAddress, int port, string direct)
            {
                _localEndPoint = new IPEndPoint(ipAddress, port);
                directory = direct;
            }

            public void ChangeDirectory(string newDirectory)
            {
                directory = newDirectory;
            }

            public void Start()
            {
                _listener = new TcpListener(_localEndPoint);

                _listening = true;
                _listener.Start();

                _activeConnections = new List<ClientConnection>();

                _listener.BeginAcceptTcpClient(HandleAcceptTcpClient, _listener);
            }

            public void Stop()
            {

                _listening = false;
                _listener.Stop();

                _listener = null;
            }

            private void HandleAcceptTcpClient(IAsyncResult result)
            {
                if (_listening)
                {
                    _listener.BeginAcceptTcpClient(HandleAcceptTcpClient, _listener);

                    TcpClient client = _listener.EndAcceptTcpClient(result);

                    ClientConnection connection = new ClientConnection(client, directory);

                    _activeConnections.Add(connection);

                    ThreadPool.QueueUserWorkItem(connection.HandleClient, client);
                }
            }

            public void Dispose()
            {
                Dispose(true);
            }

            protected virtual void Dispose(bool disposing)
            {
                if (!_disposed)
                {
                    if (disposing)
                    {
                        Stop();

                        foreach (ClientConnection conn in _activeConnections)
                        {
                            conn.Dispose();
                        }
                    }
                }

                _disposed = true;
            }
        }
    }
}
