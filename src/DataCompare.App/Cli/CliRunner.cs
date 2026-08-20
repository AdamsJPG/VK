using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataCompare.Engine.Connections;
using DataCompare.Engine.DataComparison;
using DataCompare.Engine.Models;
using DataCompare.Engine.Reporting;
using DataCompare.Engine.Schema;

namespace DataCompare.App.Cli
{

    /// <summary>
    /// Headless command-line mode: runs the same schema/data comparisons as the GUI (via the shared
    /// <see cref="DataComparisonOrchestrator"/> for data, and the same Engine schema-reading calls the
    /// GUI makes for schema) against a JSON request file, and writes the resulting HTML report(s) next
    /// to that file.
    /// </summary>
    public static class CliRunner
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

        /// <summary>
        /// dispatches on the first command-line argument: "/?" prints help, "/stub" writes a template
        /// request JSON, anything else is treated as the path to a request JSON to run.
        /// </summary>
        /// <param name="args">a System.String array holding the command-line arguments the process was launched with</param>
        /// <returns>returns a System.Threading.Tasks.Task of System.Int32 holding the process exit code — 0 for success, non-zero for a failure</returns>
        public static async Task<int> RunAsync(string[] args)
        {
            var command = args[0];
            if (string.Equals(command, "/?", StringComparison.Ordinal))
            {
                PrintHelp();
                return 0;
            }

            if (string.Equals(command, "/stub", StringComparison.OrdinalIgnoreCase))
            {
                return WriteStub(args.Length > 1 ? args[1] : "vk-compare-stub.json");
            }

            return await RunComparisonAsync(command);
        }

        /// <summary>
        /// prints CLI usage help to standard output.
        /// </summary>
        /// <returns>returns nothing; this is a System.Void method</returns>
        private static void PrintHelp()
        {
            Console.WriteLine("""
                VK Data Compare — command-line mode

                Usage:
                  VK.exe /?                  Show this help.
                  VK.exe /stub [path]        Write a template request JSON to [path]
                                             (default: vk-compare-stub.json in the current directory).
                                             Fails if the file already exists.
                  VK.exe <path-to-json>      Run the comparison(s) described by the JSON file and write
                                             the HTML report(s) next to it.

                Request JSON shape:
                  {
                    "mode": "schema" | "data" | "both",
                    "source": {
                      "server": "...", "database": "...", "username": "...", "password": "...",
                      "encrypt": true, "trustServerCertificate": false
                    },
                    "target": { ... same fields as "source" ... }
                  }

                Passwords are stored in plaintext in this file by design — treat it accordingly.
                """);
        }

        /// <summary>
        /// writes a template request JSON, with every required field present and placeholder values,
        /// to the given path.
        /// </summary>
        /// <param name="path">a System.String holding the path to write the stub JSON to</param>
        /// <returns>returns a System.Int32 exit code — 0 on success, 1 if a file already exists at that path</returns>
        private static int WriteStub(string path)
        {
            if (File.Exists(path))
            {
                Console.Error.WriteLine($"Error: '{path}' already exists — remove it or choose a different path first.");
                return 1;
            }

            var stub = new CliComparisonRequest
            {
                Mode = CliComparisonMode.Both,
                Source = new CliConnectionSpec
                {
                    Server = "SOURCE_SERVER", Database = "SOURCE_DATABASE", Username = "SOURCE_USERNAME", Password = "SOURCE_PASSWORD",
                },
                Target = new CliConnectionSpec
                {
                    Server = "TARGET_SERVER", Database = "TARGET_DATABASE", Username = "TARGET_USERNAME", Password = "TARGET_PASSWORD",
                },
            };

            File.WriteAllText(path, JsonSerializer.Serialize(stub, JsonOptions));
            Console.WriteLine($"Wrote {path}");
            return 0;
        }

        /// <summary>
        /// reads and parses the request JSON at the given path, runs the comparison(s) its mode calls
        /// for, and writes the resulting HTML report(s) next to it.
        /// </summary>
        /// <param name="path">a System.String holding the path to the request JSON</param>
        /// <returns>returns a System.Threading.Tasks.Task of System.Int32 holding the process exit code — 0 for success, non-zero for a failure</returns>
        private static async Task<int> RunComparisonAsync(string path)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"Error: '{path}' does not exist.");
                return 1;
            }

            CliComparisonRequest request;
            try
            {
                var json = await File.ReadAllTextAsync(path);
                request = JsonSerializer.Deserialize<CliComparisonRequest>(json, JsonOptions)
                    ?? throw new InvalidOperationException("the request JSON deserialized to nothing.");
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                Console.Error.WriteLine($"Error: could not parse '{path}': {ex.Message}");
                return 1;
            }

            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory();
            var sourceProfile = ToProfile(request.Source, "Source");
            var targetProfile = ToProfile(request.Target, "Target");

            var hasChanges = false;
            try
            {
                if (request.Mode is CliComparisonMode.Schema or CliComparisonMode.Both)
                {
                    hasChanges |= await RunSchemaComparisonAsync(sourceProfile, request.Source.Password, targetProfile, request.Target.Password, outputDirectory);
                }

                if (request.Mode is CliComparisonMode.Data or CliComparisonMode.Both)
                {
                    hasChanges |= await RunDataComparisonAsync(sourceProfile, request.Source.Password, targetProfile, request.Target.Password, outputDirectory);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: comparison failed: {ex.Message}");
                return 1;
            }

            if (hasChanges)
            {
                await FlashChangesWarningAsync();
            }

            return 0;
        }

        /// <summary>
        /// hand-rolls a flash effect (console blink codes are unreliable across terminals) by rewriting
        /// the same console line in red on and off a few times, leaving it visible red at the end.
        /// </summary>
        /// <returns>returns a System.Threading.Tasks.Task representing the asynchronous flash animation</returns>
        private static async Task FlashChangesWarningAsync()
        {
            const string message = "*** Changes detected, please investigate ***";

            Console.WriteLine();
            var (left, top) = Console.GetCursorPosition();

            for (var i = 0; i < 6; i++)
            {
                Console.SetCursorPosition(left, top);
                if (i % 2 == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write(message);
                    Console.ResetColor();
                }
                else
                {
                    Console.Write(new string(' ', message.Length));
                }

                await Task.Delay(300);
            }

            Console.SetCursorPosition(left, top);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write(message);
            Console.ResetColor();
            Console.WriteLine();
        }

        /// <summary>
        /// converts one side of a parsed request into the Engine-layer connection profile used to open connections.
        /// </summary>
        /// <param name="spec">a DataCompare.App.Cli.CliConnectionSpec holding the parsed connection details</param>
        /// <param name="name">a System.String holding the display name to give the resulting profile</param>
        /// <returns>returns a DataCompare.Engine.Models.ConnectionProfile built from the given spec</returns>
        private static ConnectionProfile ToProfile(CliConnectionSpec spec, string name) => new()
        {
            Name = name,
            ServerName = spec.Server,
            DatabaseName = spec.Database,
            UserId = spec.Username,
            Encrypt = spec.Encrypt,
            TrustServerCertificate = spec.TrustServerCertificate,
        };

        /// <summary>
        /// connects to both sides, compares their schemas, and writes the HTML schema report.
        /// </summary>
        /// <param name="sourceProfile">a DataCompare.Engine.Models.ConnectionProfile describing the source side of the comparison</param>
        /// <param name="sourcePassword">a System.String holding the source connection's password</param>
        /// <param name="targetProfile">a DataCompare.Engine.Models.ConnectionProfile describing the target side of the comparison</param>
        /// <param name="targetPassword">a System.String holding the target connection's password</param>
        /// <param name="outputDirectory">a System.String holding the directory to write the report into</param>
        /// <returns>returns a System.Threading.Tasks.Task of System.Boolean holding true if the schemas differ</returns>
        private static async Task<bool> RunSchemaComparisonAsync(
            ConnectionProfile sourceProfile, string sourcePassword, ConnectionProfile targetProfile, string targetPassword, string outputDirectory)
        {
            Console.WriteLine("Reading schema...");
            var connectionFactory = new SqlConnectionFactory();
            var schemaReader = new SchemaReader();

            await using var sourceConnection = connectionFactory.CreateConnection(sourceProfile, sourcePassword);
            await using var targetConnection = connectionFactory.CreateConnection(targetProfile, targetPassword);
            await sourceConnection.OpenAsync();
            await targetConnection.OpenAsync();

            var sourceSchema = await schemaReader.ReadSchemaAsync(sourceConnection);
            var targetSchema = await schemaReader.ReadSchemaAsync(targetConnection);
            var result = new SchemaComparer().Compare(sourceSchema, targetSchema);

            var html = SchemaHtmlReportWriter.Generate(
                sourceProfile.ServerName, sourceProfile.DatabaseName ?? string.Empty,
                targetProfile.ServerName, targetProfile.DatabaseName ?? string.Empty, result, sourceSchema, targetSchema);

            var outputPath = Path.Combine(outputDirectory, $"VK-Schema-Compare-{DateTime.Now:yyyyMMdd-HHmmss}.html");
            await File.WriteAllTextAsync(outputPath, html);
            if (result.IsIdentical)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Schemas are identical.");
                Console.ResetColor();
                Console.WriteLine($"Schema report written to {outputPath}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Schema changes found");
                Console.ResetColor();
                Console.WriteLine(
                    $"{result.TablesOnlyInSource.Count} table(s) only in source, {result.TablesOnlyInTarget.Count} only in target, " +
                    $"{result.TableDiffs.Count} table(s) with column differences.{Environment.NewLine}Schema report written to {outputPath}");
            }

            return !result.IsIdentical;
        }

        /// <summary>
        /// connects to both sides, compares their data via the shared <see
        /// cref="DataComparisonOrchestrator"/>, and writes the HTML data comparison report.
        /// </summary>
        /// <param name="sourceProfile">a DataCompare.Engine.Models.ConnectionProfile describing the source side of the comparison</param>
        /// <param name="sourcePassword">a System.String holding the source connection's password</param>
        /// <param name="targetProfile">a DataCompare.Engine.Models.ConnectionProfile describing the target side of the comparison</param>
        /// <param name="targetPassword">a System.String holding the target connection's password</param>
        /// <param name="outputDirectory">a System.String holding the directory to write the report into</param>
        /// <returns>returns a System.Threading.Tasks.Task of System.Boolean holding true if any table's data differs</returns>
        private static async Task<bool> RunDataComparisonAsync(
            ConnectionProfile sourceProfile, string sourcePassword, ConnectionProfile targetProfile, string targetPassword, string outputDirectory)
        {
            Console.WriteLine("Comparing data...");
            var orchestrator = new DataComparisonOrchestrator();
            var tableProgress = new Progress<DataComparisonTableProgress>(p =>
                Console.WriteLine($"Compared {p.CompletedTableCount} of {p.TotalTableCount} table(s)..."));

            var result = await orchestrator.RunAsync(
                sourceProfile, sourcePassword, targetProfile, targetPassword,
                [], null, null, null, tableProgress, CancellationToken.None);

            var html = DataComparisonHtmlReportWriter.Generate(
                sourceProfile.ServerName, sourceProfile.DatabaseName ?? string.Empty,
                targetProfile.ServerName, targetProfile.DatabaseName ?? string.Empty, result.Rows);

            var outputPath = Path.Combine(outputDirectory, $"VK-Data-Compare-{DateTime.Now:yyyyMMdd-HHmmss}.html");
            await File.WriteAllTextAsync(outputPath, html);
            Console.ForegroundColor = result.DifferingTableCount == 0 ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine($"Compared {result.ComparedTableCount} table(s) — {result.DifferingTableCount} with data differences.");
            Console.ResetColor();
            Console.WriteLine($"Data comparison report written to {outputPath}");

            return result.DifferingTableCount > 0;
        }
    }
}
