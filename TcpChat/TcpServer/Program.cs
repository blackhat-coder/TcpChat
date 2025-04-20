using System;
using TcpServer;

Server server;

int port = 6000;
server = new Server(port);

Console.CancelKeyPress += InterruptHandler;

await server.Run();

void InterruptHandler(object sender, ConsoleCancelEventArgs args)
{
    server.Shutdown();
    args.Cancel = true;
}