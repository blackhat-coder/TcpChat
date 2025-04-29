using System.ComponentModel.DataAnnotations;
using TcpMessenger;

Console.WriteLine("=== Console Chat Client ===");
Console.WriteLine("Enter your name to begin chatting");
Console.WriteLine("Type :help at any time to see available commands\n");

Console.Write("Enter your name: ");
string name = Console.ReadLine()?.Trim();

while (string.IsNullOrWhiteSpace(name))
{
    Console.Write("Name cannot be empty. Please enter your name: ");
    name = Console.ReadLine()?.Trim();
}

(bool success, string? host, int port, string? errorMessage) serverValidationResult;
string? serverAddress;

while (true)
{
    Console.Write("Enter server address (e.g., localhost:6000): ");
    serverAddress = Console.ReadLine()?.Trim();
    serverValidationResult = ValidateServerAddress(serverAddress);

    if (serverValidationResult.success){
        break;
    }

    if (!string.IsNullOrEmpty(serverValidationResult.errorMessage))
    {
        Console.WriteLine(serverValidationResult.errorMessage);
    }
}

try
{
    Messenger messenger = new Messenger(serverValidationResult.host!, serverValidationResult.port, name);
    await messenger.Connect();
    messenger.SendMessages();
}
catch (Exception ex)
{
    Console.WriteLine($"Fatal error: {ex.Message}");
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey();
}

(bool success, string? host, int port, string? errorMessage) ValidateServerAddress(string? serverAddress)
{
    if (string.IsNullOrWhiteSpace(serverAddress))
    {
        return (false, null, 0, "Server address cannot be empty. Please enter a server address.");
    }

    try
    {
        string[] parts = serverAddress.Split(':');
        if (parts.Length != 2) {
            return (false, null, 0, "Please input server address in the format <address>:<port>.");
        }

        string host = parts[0];
        if (!int.TryParse(parts[1], out int port)){
            return (false, host, 0, "Invalid port number. Please enter a valid integer port.");
        }

        if (port is < 1 or > 65535){
            return (false, host, port, "Port number must be between 1 and 65535.");
        }

        return (true, host, port, null);
    }
    catch (Exception) // Catching general exceptions here is okay as the more specific IndexOutOfRangeException is handled above
    {
        return (false, null, 0, "An unexpected error occurred while validating the server address.");
    }
}