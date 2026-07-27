using System.Text;
using Sanduhr.Mcp;

// sanduhr-mcp: read-only stdio MCP server over the Sanduhr snapshot seam.
// On-demand child of the user's own CLI. No service, no autostart, no network
// listener, no credential access (structurally - see the csproj trust boundary).

var stdin = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };

var config = McpConfig.Resolve();
var server = new McpServer(stdin, stdout, new ToolLogic(config));
server.Run();
