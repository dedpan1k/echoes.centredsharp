using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using CentrED.Network;
using CentrED.Server.Config;
using CentrED.Server.Map;
using CentrED.Utility;

namespace CentrED.Server;

/// <summary>
/// Hosts the collaborative editing server, including networking, persistence, subscriptions,
/// background maintenance, and command processing.
/// </summary>
public class CEDServer : ILogging, IDisposable
{
    /// <summary>
    /// Defines the maximum number of simultaneous client sessions the server will accept.
    /// </summary>
    public const int MaxConnections = 1024;

    /// <summary>
    /// Defines the send pipe size allocated for each connected client session.
    /// </summary>
    private const int SendPipeSize = 1024 * 256;
    
    private readonly Logger _logger = new();
    private ProtocolVersion ProtocolVersion;
    private Socket Listener { get; } = null!;

    /// <summary>
    /// Gets the loaded server configuration, including accounts, regions, and backup settings.
    /// </summary>
    public ConfigRoot Config { get; }

    /// <summary>
    /// Gets the map landscape state served to connected editors.
    /// </summary>
    public ServerLandscape Landscape { get; }

    /// <summary>
    /// Gets the currently connected client sessions.
    /// </summary>
    public HashSet<NetState<CEDServer>> Clients { get; } = new(8);

    // Subscriptions are keyed by block number so map change broadcasts only reach
    // clients that are actively viewing the affected area.
    private readonly Dictionary<long, HashSet<NetState<CEDServer>>> _blockSubscriptions = new();

    private readonly ConcurrentQueue<NetState<CEDServer>> _connectedQueue = new();
    private readonly Queue<NetState<CEDServer>> _toDispose = new();
    
    private readonly ConcurrentQueue<string> _consoleCommandQueue = new();

    /// <summary>
    /// Records when the current server process started.
    /// </summary>
    public DateTime StartTime = DateTime.Now;
    private DateTime _lastFlush = DateTime.Now;
    private DateTime _lastBackup = DateTime.Now;

    /// <summary>
    /// Gets or sets a value indicating whether the main loop should terminate.
    /// </summary>
    public bool Quit { get; set; }

    /// <summary>
    /// Gets a value indicating whether the server accept loop and main loop are active.
    /// </summary>
    public bool Running { get; private set; }
    
    /// <summary>
    /// Gets a value indicating whether the main loop is allowed to sleep briefly between iterations.
    /// </summary>
    public bool CPUIdle { get; private set; } = true;
    private NetState<CEDServer>? _CPUIdleOwner;

    /// <summary>
    /// Initializes a new server instance from the supplied configuration.
    /// </summary>
    /// <param name="config">The loaded server configuration and persisted state.</param>
    /// <param name="logOutput">An optional writer used for structured server logging.</param>
    public CEDServer(ConfigRoot config, TextWriter? logOutput = default)
    {
        if (logOutput == null)
            logOutput = Console.Out;
        _logger.Out = logOutput;
        
        LogInfo("Initialization started");
        Config = config;
        ProtocolVersion = Config.CentrEdPlus ? ProtocolVersion.CentrEDPlus : ProtocolVersion.CentrED;
        LogInfo("Running as " + (Config.CentrEdPlus ? "CentrED+ 0.7.9" : "CentrED 0.6.3"));
        Console.CancelKeyPress += ConsoleOnCancelKeyPress;
        Landscape = new ServerLandscape(config, _logger);
        Listener = Bind(new IPEndPoint(IPAddress.Any, Config.Port));
        LogInfo("Initialization done");
    }

    /// <summary>
    /// Finds a connected client session by username.
    /// </summary>
    /// <param name="name">The username to look up.</param>
    /// <returns>The matching live session, or <see langword="null"/> when the user is offline.</returns>
    public NetState<CEDServer>? GetClient(string name)
    {
        return Clients.FirstOrDefault(ns => ns.Username == name);
    }

    /// <summary>
    /// Finds an account definition by username.
    /// </summary>
    /// <param name="name">The account name to look up.</param>
    /// <returns>The configured account, or <see langword="null"/> when no account matches.</returns>
    public Account? GetAccount(string name)
    {
        return Config.Accounts.Find(a => a.Name == name);
    }

    /// <summary>
    /// Resolves the persisted account associated with an active network session.
    /// </summary>
    /// <param name="ns">The client session whose account should be returned.</param>
    /// <returns>The matching account, or <see langword="null"/> when the session is not authenticated.</returns>
    public Account? GetAccount(NetState<CEDServer> ns)
    {
        return Config.Accounts.Find(a => a.Name == ns.Username);
    }

    /// <summary>
    /// Finds a configured region by name.
    /// </summary>
    /// <param name="name">The region name to look up.</param>
    /// <returns>The configured region, or <see langword="null"/> when no region matches.</returns>
    public Region? GetRegion(string name)
    {
        return Config.Regions.Find(a => a.Name == name);
    }

    /// <summary>
    /// Creates and binds the listening socket used for incoming client connections.
    /// </summary>
    /// <param name="endPoint">The address and port the server should listen on.</param>
    /// <returns>The bound listening socket.</returns>
    private Socket Bind(IPEndPoint endPoint)
    {
        var s = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            LingerState = new LingerOption(false, 0),
            ExclusiveAddressUse = false,
            NoDelay = true,
            SendBufferSize = SendPipeSize,
            ReceiveBufferSize = 64 * 1024,
        };

        try
        {
            s.Bind(endPoint);
            s.Listen(32);
            LogInfo($"Listening on {s.LocalEndPoint}");
            return s;
        }
        catch (Exception e)
        {
            if (e is SocketException se)
            {
                // WSAEADDRINUSE
                if (se.ErrorCode == 10048)
                {
                    LogError($"Listener Failed: {endPoint.Address}:{endPoint.Port} (In Use)");
                }
                // WSAEADDRNOTAVAIL
                else if (se.ErrorCode == 10049)
                {
                    LogError($"Listener Failed: {endPoint.Address}:{endPoint.Port} (Unavailable)");
                }
                else
                {
                    LogError("Listener Exception:");
                    Console.WriteLine(e);
                }
            }

            return null!;
        }
    }

    /// <summary>
    /// Registers the top-level packet routing table for a newly connected session.
    /// </summary>
    /// <param name="ns">The session that should receive the shared packet handlers.</param>
    private void RegisterPacketHandlers(NetState<CEDServer> ns)
    {
        ns.RegisterPacketHandler(0x01, 0, Zlib.OnCompressedPacket);
        ns.RegisterPacketHandler(0x02, 0, ConnectionHandling.OnConnectionHandlerPacket);
        ns.RegisterPacketHandler(0x03, 0, AdminHandling.OnAdminHandlerPacket);
        ns.RegisterPacketHandler(0x0C, 0, ClientHandling.OnClientHandlerPacket);
        ns.RegisterPacketHandler(0xFF, 1, ConnectionHandling.OnNoOpPacket);
    }

    /// <summary>
    /// Accepts inbound sockets and turns them into queued client sessions.
    /// </summary>
    private async void Listen()
    {
        try
        {
            while (Running)
            {
                var socket = await Listener.AcceptAsync();
                if (Clients.Count >= MaxConnections)
                {
                    LogError("Too many connections");
                    socket.Shutdown(SocketShutdown.Both);
                    socket.Close();
                    continue;
                }
                
                var ns = new NetState<CEDServer>(this, socket, sendPipeSize: SendPipeSize)
                {
                    ProtocolVersion = ProtocolVersion
                };
                RegisterPacketHandlers(ns);
                Landscape.RegisterPacketHandlers(ns);
                _connectedQueue.Enqueue(ns);
            }
        }
        catch (Exception e)
        {
            LogError("Server stopped");
            LogError(e.ToString());
        }
        finally
        {
            Quit = true;
        }
    }

    /// <summary>
    /// Requests a graceful shutdown when the hosting process receives Ctrl+C.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The cancellation event arguments.</param>
    private void ConsoleOnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        LogInfo("Killed");
        Quit = true;
        e.Cancel = true;
    }

    /// <summary>
    /// Starts the accept loop and enters the main server processing loop.
    /// </summary>
    public void Run()
    {
        Running = true;
        new Task(Listen).Start();
        try
        {
            do
            {
                // Keep the main loop ordered so newly connected clients are announced
                // before packet processing and maintenance work begins.
                ProcessConnectedQueue();
                ProcessNetStates();

                AutoSave();
                AutoBackup();
                ProcessCommands();

                if(CPUIdle)
                    Thread.Sleep(1);
            } while (!Quit);
        }
        finally
        {
            Listener.Close();
            foreach (var ns in Clients)
            {
                ns.Dispose();
            }
            Running = false;
        }
    }

    /// <summary>
    /// Promotes newly accepted sockets into active sessions and sends their initial handshake.
    /// </summary>
    private void ProcessConnectedQueue()
    {
        while (_connectedQueue.TryDequeue(out var ns))
        {
            Clients.Add(ns);
            ns.LogInfo($"Connected. [{Clients.Count} Online]");
            ns.Send(new ProtocolVersionPacket((uint)ProtocolVersion));
            ns.Flush();
        }
    }

    /// <summary>
    /// Services network input and output for all active sessions, disposing disconnected clients.
    /// </summary>
    private void ProcessNetStates()
    {
        foreach (var ns in Clients)
        {
            if (!ns.Receive() || !ns.Active)
            {
                _toDispose.Enqueue(ns);
            }
            if (!ns.Flush())
            {
                _toDispose.Enqueue(ns);
            }
        }
        
        while (_toDispose.TryDequeue(out var ns))
        {
            Clients.Remove(ns);
            if (ns.Username != "")
            {
                Broadcast(new ClientDisconnectedPacket(ns));
            }
            if (CPUIdle && _CPUIdleOwner == ns)
            {
                SetCPUIdle(null, true);
            }
            ns.Dispose();
        }
    }

    /// <summary>
    /// Persists map and configuration state on the autosave interval.
    /// </summary>
    private void AutoSave()
    {
        if (DateTime.Now - TimeSpan.FromMinutes(1) > _lastFlush)
        {
            Save();
        }
    }

    /// <summary>
    /// Flushes the landscape and configuration state to disk immediately.
    /// </summary>
    public void Save()
    {
        Landscape.Flush();
        Config.Flush();
        _lastFlush = DateTime.Now;
    }

    /// <summary>
    /// Triggers automatic backups when the configured interval has elapsed.
    /// </summary>
    private void AutoBackup()
    {
        if (Config.AutoBackup.Enabled && DateTime.Now - Config.AutoBackup.Interval > _lastBackup)
        {
            Backup();
            _lastBackup = DateTime.Now;
        }
    }

    /// <summary>
    /// Flushes pending outbound packets for clients that still have buffered data.
    /// </summary>
    public void Flush()
    {
        foreach (var ns in Clients)
        {
            if (!ns.FlushPending) continue;
            
            if (!ns.Flush())
            {
                _toDispose.Enqueue(ns);
            }
        }
    }

    /// <summary>
    /// Queues a packet for every connected client session.
    /// </summary>
    /// <param name="packet">The packet to broadcast.</param>
    public void Broadcast(Packet packet)
    {
        foreach (var ns in Clients)
        {
            ns.Send(packet);
        }
    }
    
    /// <summary>
    /// Gets the live client subscriptions for a map block, removing dead sessions as needed.
    /// </summary>
    /// <param name="x">The block X coordinate.</param>
    /// <param name="y">The block Y coordinate.</param>
    /// <returns>The mutable subscription set for the requested block.</returns>
    public HashSet<NetState<CEDServer>> GetBlockSubscriptions(ushort x, ushort y)
    {
        Landscape.AssertBlockCoords(x, y);
        var key = Landscape.GetBlockNumber(x, y);

        if (!_blockSubscriptions.TryGetValue(key, out var subscriptions))
        {
            subscriptions = [];
            _blockSubscriptions.Add(key, subscriptions);
        }

        subscriptions.RemoveWhere(ns => !ns.Running);
        return subscriptions;
    }

    /// <summary>
    /// Rotates automatic backup directories and writes a fresh landscape snapshot.
    /// </summary>
    private void Backup()
    {
        Landscape.Flush();
        var logMsg = "Automatic backup in progress";
        LogInfo(logMsg);
        Broadcast(new ServerStatePacket(ServerState.Other, logMsg));
        String backupDir;
        for (var i = Config.AutoBackup.MaxBackups; i > 0; i--)
        {
            backupDir = $"{Config.AutoBackup.Directory}/Backup{i}";
            if (Directory.Exists(backupDir))
                if (i == Config.AutoBackup.MaxBackups)
                    Directory.Delete(backupDir, true);
                else
                    Directory.Move(backupDir, $"{Config.AutoBackup.Directory}/Backup{i + 1}");
        }
        backupDir = $"{Config.AutoBackup.Directory}/Backup1";

        Landscape.Backup(backupDir);

        Broadcast(new ServerStatePacket(ServerState.Running));
        LogInfo("Automatic backup finished.");
    }

    /// <summary>
    /// Enables or disables idle sleeping for the main loop.
    /// </summary>
    /// <param name="ns">The client that requested the change, when applicable.</param>
    /// <param name="enabled"><see langword="true"/> to allow idle sleeps; otherwise, <see langword="false"/>.</param>
    public void SetCPUIdle(NetState<CEDServer>? ns, bool enabled)
    {
        if (CPUIdle == enabled)
            return;
        
        Console.WriteLine($"CPU Idle: {enabled}");
        CPUIdle = enabled;
        if (enabled)
            _CPUIdleOwner = ns;
        else
        {
            _CPUIdleOwner = null;
        }
    }

    /// <summary>
    /// Queues a console command for execution on the main server loop.
    /// </summary>
    /// <param name="command">The raw console command text.</param>
    public void PushCommand(string command)
    {
        _consoleCommandQueue.Enqueue(command);
    }

    /// <summary>
    /// Executes queued console commands on the main loop thread.
    /// </summary>
    private void ProcessCommands()
    {
        while (_consoleCommandQueue.TryDequeue(out var command))
        {
            try
            {
                // Keep command parsing minimal and deterministic because these commands
                // are primarily used for emergency saves and manual maintenance.
                var parts = command.Split(' ', 2);
                switch (parts)
                {
                    case ["save"]:
                        Console.Write("Saving...");
                        Landscape.Flush();
                        Console.WriteLine("Done");
                        break;
                    case ["save", string dir]:
                        Console.Write($"Saving to {dir}...");
                        Landscape.Backup(dir);
                        Console.WriteLine("Done");
                        break;
                    case ["supersave"]:
                        Console.Write("Supersaving...");
                        Landscape.SuperSave();
                        Console.WriteLine("Done");
                        break;
                    default: PrintHelp(); break;
                }
                ;
            }
            catch (Exception e)
            {
                LogError($"Error processing command: {command}");
                LogError(e.ToString());
            }
        }
    }

    /// <summary>
    /// Prints the supported console commands.
    /// </summary>
    private void PrintHelp()
    {
        Console.WriteLine("Supported commands:");
        Console.WriteLine("save");
        Console.WriteLine("save <dir>");
        Console.WriteLine("supersave");
    }

    /// <summary>
    /// Releases the listening socket and landscape resources owned by the server.
    /// </summary>
    public void Dispose()
    {
        Listener.Dispose();
        Landscape.Dispose();
    }
    
    /// <summary>
    /// Writes an informational message to the configured logger.
    /// </summary>
    /// <param name="message">The message to write.</param>
    public void LogInfo(string message)
    {
        _logger.LogInfo(message);
    }

    /// <summary>
    /// Writes a warning message to the configured logger.
    /// </summary>
    /// <param name="message">The message to write.</param>
    public void LogWarn(string message)
    {
       _logger.LogWarn(message);
    }

    /// <summary>
    /// Writes an error message to the configured logger.
    /// </summary>
    /// <param name="message">The message to write.</param>
    public void LogError(string message)
    {
        _logger.LogError(message);
    }

    /// <summary>
    /// Writes a debug message when the server is built in debug mode.
    /// </summary>
    /// <param name="message">The message to write.</param>
    public void LogDebug(string message)
    {
#if DEBUG
        _logger.LogDebug(message);
#endif
    }
}