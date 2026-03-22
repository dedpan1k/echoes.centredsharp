using System.Globalization;
using CentrED.Client;
using CentrED.Server;
using CentrED.Utils;

namespace CentrED;

/// <summary>
/// Provides process-wide application state and the main entry point for the editor.
/// </summary>
public class Application
{
    /// <summary>
    /// Gets the directory that contains the deployed application binaries and runtime assets.
    /// </summary>
    // Resolve assets relative to the executable so startup does not depend on
    // whichever working directory launched the process.
    static public string WorkDir { get; } = AppContext.BaseDirectory;

    /// <summary>
    /// Gets the active editor game instance for the current process.
    /// </summary>
    // These process-wide references expose the single editor, server, client,
    // and metrics instances used during a session.
    public static CentrEDGame CEDGame { get; private set; } = null!;

    /// <summary>
    /// Holds the optional in-process server instance when the editor hosts one.
    /// </summary>
    public static CEDServer? CEDServer;

    /// <summary>
    /// Gets the shared client instance used by the editor.
    /// </summary>
    public static readonly CentrEDClient CEDClient = new();

    /// <summary>
    /// Gets the metrics collector used for runtime instrumentation.
    /// </summary>
    public static readonly Metrics Metrics = new();

    [STAThread]
    /// <summary>
    /// Initializes process-wide state and starts the editor game loop.
    /// </summary>
    /// <param name="args">Command-line arguments supplied to the application.</param>
    public static void Main(string[] args)
    {
        // Force invariant formatting so config, logging, and protocol behavior
        // do not vary with the user's locale.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        
        Console.WriteLine($"Root Dir: {WorkDir}");

        // Load configuration before any game systems start so initialization
        // code can safely read settings.
        Config.Initialize();

        // CentrEDGame owns the FNA lifetime; disposing it here guarantees native
        // graphics and input resources are released during shutdown.
        using (CEDGame = new CentrEDGame())
        {
            try
            {
                CEDGame.Run();
            }
            catch (Exception e)
            {
                // Persist crash details because the console window may close
                // before the user can copy the stack trace.
                Console.WriteLine(e.ToString());
                File.WriteAllText("Crash.log", e.ToString());
            }
        }
    }
}