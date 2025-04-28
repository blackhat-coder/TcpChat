using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Tcp.Shared.DTOs.Requests;
using Tcp.Shared.DTOs.Responses;
using Tcp.Shared.Enums;
using Tcp.Shared.Utilities;

namespace TcpMessenger;

public class Messenger
{
    public readonly string ServerAddress;
    public readonly int Port;
    private TcpClient _client;
    private object _lock = new object();

    private object _running = new object();
    public bool Running
    {
        get
        {
            object val = Interlocked.CompareExchange(ref _running, false, null);
            return val != null && (bool)val;
        }
        set
        {
            Interlocked.Exchange(ref _running, value);
        }
    }

    // Buffer & messaging
    public readonly int BufferSize = 2 * 1024;  // 2KB
    private NetworkStream _msgStream = null;

    private string _name;
    public string Name
    {
        get { return _name; }
        set
        {
            lock (_lock)
            {
                _name = value;
            }
        }
    }

    public readonly int LengthPrefixSize = 4;
    private bool _inChatRoom = false;
    private string? _chatRoomName;

    const int MAX_CHATROOM_NAME_LENGTH = 24;

    /// <summary>
    /// Turn on/off collecting input from users
    /// </summary>
    private object _collectInput = new object();
    public bool CollectInput
    {
        get
        {
            object val = Interlocked.CompareExchange(ref _collectInput, false, null);
            return val != null && (bool)val;
        }
        set
        {
            Interlocked.Exchange(ref _collectInput, value);
        }
    }

    private List<string> _commandsList = new List<string> { Commands.CreateChatRoom, Commands.ViewChatRooms, Commands.JoinChatRoom, Commands.LeaveChatRoom, Commands.Help };
    private Dictionary<string, CommandType> commandMap = new Dictionary<string, CommandType>
    {
        {Commands.CreateChatRoom, CommandType.CreateChatRoom },
        {Commands.ViewChatRooms, CommandType.ViewChatRooms },
        {Commands.JoinChatRoom, CommandType.JoinChatRoom },
        {Commands.LeaveChatRoom, CommandType.LeaveChatRoom },
        {Commands.Help, CommandType.Help }
    };

    public Messenger(string serverAddress, int port, string name)
    {
        _client = new TcpClient();
        Running = false;
        CollectInput = true;

        // Set the other things
        ServerAddress = serverAddress;
        Port = port;
        Name = name;
        _name = name;
    }

    private bool _nameAvailable = false;

    public async Task Connect()
    {
        Console.WriteLine("Connecting to chat server...");

        try
        {
            _client.Connect(ServerAddress, Port);
            EndPoint endPoint = _client.Client.RemoteEndPoint;
            _msgStream = _client.GetStream();

            if (_client.Connected)
            {
                Console.WriteLine($"Connected to server at {endPoint}");
                Console.WriteLine("\nType :help to see available commands\n");

                Running = true;
                _ = Task.Run(() => ReceiveMessagesAsync());

                string prevName = string.Empty;
                while (!_nameAvailable)
                {
                    if (prevName != Name)
                        await NegotiateUserName(Name);
                    prevName = Name;
                }

                await StreamUtils.WriteMessageAsync<RegisterNode>(_msgStream,
                    new RegisterNode { Id = Guid.NewGuid().ToString(), Name = Name });

                if (!_isDisconnected(_client))
                {
                    string chatRoomsResponse = await StreamUtils.ReadMessageAsync(_msgStream);
                    if (!string.IsNullOrEmpty(chatRoomsResponse))
                    {
                        var chatRooms = JsonSerializer.Deserialize<ChatRoomsResponse>(chatRoomsResponse);
                        Console.WriteLine("\nAvailable chat rooms:");
                        ConsoleExt.WriteChatRooms(chatRooms?.ChatRooms);
                    }

                    Running = true;
                }
                else
                {
                    _cleanupNetworkResources();
                    Console.WriteLine("The server rejected the connection.");
                }
            }
            else
            {
                _cleanupNetworkResources();
                Console.WriteLine("Failed to connect to server.");
            }
        }
        catch (Exception ex)
        {
            _cleanupNetworkResources();
            Console.WriteLine($"Connection error: {ex.Message}");
        }
    }

    private async Task NegotiateUserName(string userName)
    {
        await StreamUtils.WriteMessageAsync<MessageRequest>(_msgStream, new MessageRequest
        {
            Type = MessageType.UserNameNegotiation,
            Data = new MessageData { Message = userName }
        });
    }

    public async void SendMessages()
    {
        bool wasRunning = Running;
        _ = Task.Run(() => ReceiveMessagesAsync());

        while (Running)
        {
            if (CollectInput)
            {
                Console.Write($"{ChatDisplay()}");
                string? msg = Console.ReadLine();

                if (string.IsNullOrEmpty(msg))
                    continue;

                var msgType = GetInputType(msg);

                if (msgType == MessageType.Command)
                {
                    var commandResult = GetCommandType(msg);

                    if (!commandResult.Item1)
                    {
                        Console.WriteLine("Error: Unknown command. Type :help for available commands.");
                        continue;
                    }

                    CollectInput = false;
                    await CommandHandler(commandResult.Item2!.Value, msg);
                }

                if (msgType == MessageType.Message)
                {
                    if (!_inChatRoom)
                    {
                        Console.WriteLine("Error: You must join a chat room first. Use :create-chatroom or :join-chatroom");
                        continue;
                    }

                    await StreamUtils.WriteMessageAsync<MessageRequest>(_msgStream, new MessageRequest
                    {
                        Type = MessageType.Message,
                        Data = new MessageData
                        {
                            Message = msg,
                            SentBy = Name
                        }
                    });
                }

                if (_isDisconnected(_client))
                {
                    Running = false;
                    Console.WriteLine("Server has disconnected.");
                }
            }
        }

        _cleanupNetworkResources();
        if (wasRunning)
            Console.WriteLine("Disconnected from server.");
    }

    public async Task CommandHandler(CommandType commandType, string userInput)
    {
        try
        {
            if (commandType == CommandType.JoinChatRoom)
            {
                var roomName = string.Join(' ', userInput.Split(' ')[1..]);
                if (string.IsNullOrWhiteSpace(roomName))
                {
                    Console.WriteLine("Error: Please specify a chat room name");
                    CollectInput = true;
                    return;
                }

                await StreamUtils.WriteMessageAsync<MessageRequest>(_msgStream, new MessageRequest
                {
                    Type = MessageType.Command,
                    CommandRequest = new CommandRequest
                    {
                        Command = CommandType.JoinChatRoom,
                        Value = roomName
                    }
                });
            }

            if (commandType == CommandType.CreateChatRoom)
            {
                if (_inChatRoom)
                {
                    Console.WriteLine("Error: Please leave your current chat room first");
                    CollectInput = true;
                    return;
                }

                var nameParts = userInput.Split(' ')[1..];
                if (nameParts.Length == 0)
                {
                    Console.WriteLine("Error: Please specify a chat room name");
                    CollectInput = true;
                    return;
                }

                var roomName = string.Join(' ', nameParts);
                if (roomName.Length > MAX_CHATROOM_NAME_LENGTH)
                {
                    Console.WriteLine($"Error: Chat room name too long (max {MAX_CHATROOM_NAME_LENGTH} characters)");
                    CollectInput = true;
                    return;
                }

                await StreamUtils.WriteMessageAsync<MessageRequest>(_msgStream, new MessageRequest
                {
                    Type = MessageType.Command,
                    CommandRequest = new CommandRequest
                    {
                        Command = CommandType.CreateChatRoom,
                        Value = roomName
                    }
                });
            }

            if (commandType == CommandType.LeaveChatRoom)
            {
                if (!_inChatRoom)
                {
                    Console.WriteLine("Error: You're not in any chat room");
                    CollectInput = true;
                    return;
                }

                await StreamUtils.WriteMessageAsync<MessageRequest>(_msgStream, new MessageRequest
                {
                    Type = MessageType.Command,
                    CommandRequest = new CommandRequest
                    {
                        Command = CommandType.LeaveChatRoom
                    }
                });
            }

            if (commandType == CommandType.ViewChatRooms)
            {
                await StreamUtils.WriteMessageAsync<MessageRequest>(_msgStream, new MessageRequest
                {
                    Type = MessageType.Command,
                    CommandRequest = new CommandRequest
                    {
                        Command = CommandType.ViewChatRooms
                    }
                });
            }

            if (commandType == CommandType.Help)
            {
                ShowHelp();
                CollectInput = true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing command: {ex.Message}");
            CollectInput = true;
        }
    }

    private void ShowHelp()
    {
        Console.WriteLine("\nAvailable Commands:");
        Console.WriteLine("------------------");
        Console.WriteLine(":create-chatroom <name> - Create a new chat room with the given name");
        Console.WriteLine(":join-chatroom <name>   - Join an existing chat room");
        Console.WriteLine(":leave-chatroom         - Leave your current chat room");
        Console.WriteLine(":view-chatrooms         - List all available chat rooms");
        Console.WriteLine(":help                   - Show this help message");
        Console.WriteLine("\nTo send a message, just type and press Enter (must be in a chat room)");
    }

    private MessageType GetInputType(string userInput)
    {
        var stringSplit = userInput.Split(' ');
        if (_commandsList.Contains(stringSplit[0]))
        {
            return MessageType.Command;
        }

        return MessageType.Message;
    }

    private (bool, CommandType?) GetCommandType(string userInput)
    {
        var cmd = GetCommand(userInput);

        if (string.IsNullOrEmpty(cmd))
        {
            return (false, null);
        }

        if (commandMap.ContainsKey(cmd))
        {
            return (true, commandMap[cmd]);
        }

        return (false, null);
    }

    private string? GetCommand(string userInput)
    {
        if (GetInputType(userInput) != MessageType.Command)
        {
            return string.Empty;
        }
        return userInput.Split(' ')[0];
    }

    private async Task ReceiveMessagesAsync()
    {
        while (Running)
        {
            try
            {
                var message = await StreamUtils.ReadMessageAsync(_msgStream);

                if (!string.IsNullOrEmpty(message))
                {
                    var msg = JsonSerializer.Deserialize<MessageRequest>(message);

                    if (msg?.Type == MessageType.Message)
                    {
                        CollectInput = false;
                        Console.WriteLine($"\n{msg?.Data?.SentBy}@{_chatRoomName}> {msg?.Data?.Message}");
                        CollectInput = true;
                    }

                    if (msg?.Type == MessageType.CommandResponse)
                    {
                        await CmdResponseHandler(msg?.CommandResponse);
                    }

                    if (msg?.Type == MessageType.UserNameNegotiation)
                    {
                        if (msg.NameNegotiationResponse.Success)
                        {
                            _nameAvailable = true;
                            Running = false;
                            continue;
                        }

                        Console.Write($"Username '{Name}' is taken. Please enter a new one: ");
                        string? name = Console.ReadLine();

                        Name = !string.IsNullOrEmpty(name) ? name : $"User-{Guid.NewGuid().ToString().Substring(0, 3)}";
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error receiving message: {ex.Message}");
                Running = false;
            }
        }
    }

    private async Task CmdResponseHandler(CommandResponse response)
    {
        try
        {
            if (response.Command == CommandType.JoinChatRoom)
            {
                var chatRoomResponse = JsonSerializer.Deserialize<JoinChatRoomResponse>(response.Value);

                if (chatRoomResponse != null)
                {
                    if (chatRoomResponse.Success)
                    {
                        _inChatRoom = true;
                        _chatRoomName = chatRoomResponse.ChatRoomName;
                        Console.WriteLine($"\nJoined chat room: {_chatRoomName}");
                    }
                    else
                    {
                        Console.WriteLine($"\nError: {chatRoomResponse.ErrorMessage}");
                    }
                }
            }

            if (response.Command == CommandType.LeaveChatRoom)
            {
                var leaveRoomResponse = JsonSerializer.Deserialize<Result>(response.Value);

                if (leaveRoomResponse != null)
                {
                    if (leaveRoomResponse.Success)
                    {
                        Console.WriteLine($"\nLeft chat room: {_chatRoomName}");
                        _inChatRoom = false;
                        _chatRoomName = null;
                    }
                    else
                    {
                        Console.WriteLine($"\nError leaving chat room");
                    }
                }
            }

            if (response.Command == CommandType.ViewChatRooms)
            {
                var viewChatRooms = JsonSerializer.Deserialize<ViewChatRoomsResponse>(response.Value);

                if (viewChatRooms != null)
                {
                    Console.WriteLine("\nAvailable Chat Rooms:");
                    ConsoleExt.WriteChatRooms(viewChatRooms.Rooms);
                }
            }

            if (response.Command == CommandType.CreateChatRoom)
            {
                var createRoom = JsonSerializer.Deserialize<CreateChatRoomResponse>(response.Value);

                if (createRoom != null)
                {
                    if (createRoom.Success)
                    {
                        _inChatRoom = true;
                        _chatRoomName = createRoom.ChatRoomName;
                        Console.WriteLine($"\nCreated and joined chat room: {_chatRoomName}");
                    }
                    else
                    {
                        Console.WriteLine($"\nError: {createRoom.ErrorMessage}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing command response: {ex.Message}");
        }
        finally
        {
            CollectInput = true;
        }
    }

    private string ChatDisplay()
    {
        if (_inChatRoom)
            return $"{Name}@{_chatRoomName}> ";
        return $"{Name}> ";
    }

    private void _cleanupNetworkResources()
    {
        try
        {
            _msgStream?.Close();
            _msgStream = null;
            _client.Close();
        }
        catch { }
    }

    private static bool _isDisconnected(TcpClient client)
    {
        try
        {
            Socket s = client.Client;
            return s.Poll(10 * 1000, SelectMode.SelectRead) && (s.Available == 0);
        }
        catch (SocketException)
        {
            return true;
        }
    }
}

public static class ConsoleExt
{
    public static void WriteChatRooms(List<ChatRoomDto> chatRooms)
    {
        if (chatRooms == null || chatRooms.Count == 0)
        {
            Console.WriteLine("No chat rooms available");
            return;
        }

        for (int i = 0; i < chatRooms.Count; i++)
        {
            Console.WriteLine($"{i + 1}. {chatRooms[i].Name} ({chatRooms[i].MemCount} Online)");
        }
    }
}

public class Commands
{
    public const string CreateChatRoom = ":create-chatroom";
    public const string LeaveChatRoom = ":leave-chatroom";
    public const string ViewChatRooms = ":view-chatrooms";
    public const string JoinChatRoom = ":join-chatroom";
    public const string Help = ":help";
}