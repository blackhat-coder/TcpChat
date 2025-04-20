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

string host = "localhost";
int port = 6000;

try
{
    Messenger messenger = new Messenger(host, port, name);
    await messenger.Connect();
    messenger.SendMessages();
}
catch (Exception ex)
{
    Console.WriteLine($"Fatal error: {ex.Message}");
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey();
}