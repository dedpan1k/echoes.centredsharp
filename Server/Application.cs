using System.Reflection;
using CentrED.Server.Config;

namespace CentrED.Server;

/// <summary>
/// Provides the standalone server entry point and hosts console-driven process control.
/// </summary>
public class Application
{
    // The entry point owns a single in-process server instance for the lifetime of
    // the process so shutdown hooks and the console thread can share it.
    private static CEDServer _cedServer = null!;

    /// <summary>
    /// Initializes configuration, starts the server, and wires process-level shutdown behavior.
    /// </summary>
    /// <param name="args">Command-line arguments used to locate and override server configuration.</param>
    public static void Main(string[] args)
    {
        var assemblyName = Assembly.GetExecutingAssembly().GetName();
        var title = $"{assemblyName.Name} {assemblyName.Version}";
        Console.Title = title;
        Console.WriteLine(title);
        Console.WriteLine("Copyright 2024 Kaczy" );
        Console.WriteLine("Credits to Andreas Schneider, StaticZ");
        try
        {
            var config = ConfigRoot.Init(args);
            _cedServer = new CEDServer(config);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => _cedServer.Save();
            if (Environment.UserInteractive && !Console.IsInputRedirected)
            {
                // Read console commands on a background thread so the main loop can
                // keep servicing network traffic and autosave work.
                new Thread(HandleConsoleInput)
                {
                    IsBackground = true,
                    Name = "Console Input"
                }.Start();
            }
            _cedServer.Run();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            Console.Write("Press any key to exit...");
            Console.ReadKey();
        }
        finally
        {
            Console.WriteLine("Shutting down");
        }
    }

    /// <summary>
    /// Gets the friendly process name reported by the current application domain.
    /// </summary>
    /// <returns>The executable name used to launch the current server process.</returns>
    public static string GetCurrentExecutable()
    {
        return AppDomain.CurrentDomain.FriendlyName;
    }

    /// <summary>
    /// Continuously reads console commands and forwards them into the server command queue.
    /// </summary>
    public static async void HandleConsoleInput()
    {
        while (true)
        {
            string? input;
            try
            {
                input = Console.ReadLine()?.Trim();
            }
            catch(Exception e)
            {
                Console.WriteLine("Console input error!");
                Console.WriteLine(e);
                return;
            }
            if (string.IsNullOrEmpty(input))
            {
                continue;
            }
            _cedServer.PushCommand(input);
        }
    }
}