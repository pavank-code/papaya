using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FlowVault.BackendHost.Services;
using FlowVault.BackendHost.IPC;
using FlowVault.BackendHost.Persistence;

namespace FlowVault.BackendHost;

/// <summary>
/// Flow Vault Backend Host - Handles heavy processing, LLM calls, indexing, and persistence
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("Flow Vault Backend Host starting...");
        
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // Database
                services.AddSingleton<DatabaseService>();
                
                // Core services
                services.AddSingleton<IndexerService>();
                services.AddSingleton<TaskService>();
                services.AddSingleton<SchedulerService>();
                services.AddSingleton<GraphService>();
                services.AddSingleton<ApiKeyService>();
                
                // LLM adapters
                services.AddSingleton<MockLlmAdapter>();
                services.AddSingleton<GeminiLlmAdapter>();
                services.AddSingleton<OpenAiLlmAdapter>();
                services.AddSingleton<LlmService>();
                
                // IPC server
                services.AddSingleton<BackendApiHandler>();
                services.AddHostedService<IpcServer>();
            })
            .ConfigureLogging(logging =>
            {
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .Build();

        await host.RunAsync();
    }
}
