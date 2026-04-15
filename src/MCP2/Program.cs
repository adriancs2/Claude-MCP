using MCP2.Core;
using System;
using System.Text;

namespace MCP2
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                // Set UTF-8 encoding for stdin/stdout
                Console.InputEncoding = Encoding.UTF8;
                Console.OutputEncoding = Encoding.UTF8;

                // Ensure config file exists (writes default sample if missing)
                McpConfig.EnsureConfigExists();

                // Load configuration
                McpConfig.Load();

                // Validate caller (if enabled in config)
                if (McpConfig.CallerValidation)
                {
                    CallerValidator.Validate();
                }

                // Start the MCP server
                var server = new McpServer();
                server.Start();
            }
            catch (Exception ex)
            {
                // Write fatal error to stderr
                Console.Error.WriteLine($"Fatal error: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                Environment.Exit(1);
            }
        }
    }
}
