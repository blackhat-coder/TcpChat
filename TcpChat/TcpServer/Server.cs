using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Tcp.Shared.Models;
using Tcp.Shared.DTOs.Requests;
using Tcp.Shared.Enums;
using Tcp.Shared.Utilities;
using Tcp.Shared.DTOs.Responses;

namespace TcpServer;

public class ChatRoom
{
    public string Id { get; } = Guid.NewGuid().ToString();
    public string Name { get; }
    public ConcurrentDictionary<string, bool> Members { get; } = new();
    public ConcurrentQueue<Message> Messages { get; } = new();

    public ChatRoom(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public async Task PublishMessage(List<Node> nodes, Message message)
    {
        Messages.Enqueue(message);
        foreach (var node in nodes.Where(n => n.Id != message.CreatedBy && n.ChatRoomId == Id).ToList())
        {
            var client = node.Client;
            try
            {
                if (client.Connected)
                {
                    await StreamUtils.WriteMessageAsync<MessageRequest>(client.GetStream(), new MessageRequest
                    {
                        Type = MessageType.Message,
                        Data = new MessageData
                        {
                            Message = message.Value,
                            SentBy = nodes.FirstOrDefault(x => x.Id == message.CreatedBy)?.Name ?? "Guest"
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error publishing message to nodes, {node.Name}, {ex.Message}");
            }
        }
    }
}

public class Server
{
    private readonly TcpListener _listener;
    private readonly ConcurrentDictionary<string, Node> _nodes = new();
    private readonly ConcurrentDictionary<string, ChatRoom> _chatRooms = new();
    private readonly ConcurrentDictionary<string, bool> _nameSet = new();

    private int _port;
    public readonly int BufferSize = 1024 * 2;
    public readonly int LengthPrefixSize = 4;

    public bool Running { get; private set; }
    public int ActiveConnections => _nodes.Count;
    public int ActiveRooms => _chatRooms.Count;

    public Server(int port)
    {
        _port = port;
        Running = false;
        _listener = new TcpListener(IPAddress.Any, _port);
        _nameSet = new ConcurrentDictionary<string, bool>();
    }

    public async Task Run()
    {
        Console.WriteLine($"Starting TCP Chat Server on port {_port}");
        _listener.Start();
        Running = true;

        try
        {
            while (Running)
            {
                if (_listener.Pending())
                {
                    _ = Task.Run(() => HandleNewConnection());
                }
            }
        }
        finally
        {
            Shutdown();
        }
    }

    public async Task HandleNewConnection()
    {
        TcpClient client = new();
        try
        {
            client = _listener.AcceptTcpClient();
            NetworkStream netStream = client.GetStream();

            client.SendBufferSize = BufferSize;
            client.ReceiveBufferSize = BufferSize;

            EndPoint endpoint = client.Client.RemoteEndPoint;
            Console.WriteLine($"New connection from {client.Client.RemoteEndPoint}");
            bool nameNegotiated = false;

            while (!nameNegotiated)
            {
                var negotiateNodeName = await StreamUtils.ReadMessageAsync(netStream);
                nameNegotiated = await NegotiateNodeName(netStream, negotiateNodeName);

                if (nameNegotiated) // Successfull name negotiation
                    await StreamUtils.WriteMessageAsync<MessageRequest>(netStream, new MessageRequest
                    {
                        Type = MessageType.UserNameNegotiation,
                        NameNegotiationResponse = new Result { Success = true }
                    });
            }

            // Registration Message
            var nodeRegMessage = await StreamUtils.ReadMessageAsync(netStream);

            if (!string.IsNullOrEmpty(nodeRegMessage))
            {

                var regNode = JsonSerializer.Deserialize<RegisterNode>(nodeRegMessage);
                Node node;
                if (regNode != null)
                {
                    node = new Node { Client = client, Id = regNode.Id, Name = regNode.Name };
                    _nodes.TryAdd(node.Id, node);
                    _nameSet.Remove(regNode.Name, out bool val);
                }
                else
                {
                    Console.WriteLine($"Error registering Node: {endpoint}");
                    _cleanUpClient(client);
                    return;
                }

                var response = new ChatRoomsResponse
                {
                    Message = $"Welcome to the server {node.Name}, see available Chat Rooms",
                    ChatRooms = _chatRooms.Select(x => x.Value.Name).ToList()
                };

                Console.WriteLine("Writing chat rooms");
                await StreamUtils.WriteMessageAsync<ChatRoomsResponse>(netStream, response);

                _ = Task.Run(() => ProcessNodeMessages(node));
            }
            else
            {
                Console.WriteLine($"Error registering Node, couldn't understand node message");
                _cleanUpClient(client);
                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Connection error: {ex.Message}");
            _cleanUpClient(client);
        }
    }

    private async Task<bool> NegotiateNodeName(NetworkStream stream, string nameRequest)
    {
        Console.WriteLine($"Name REq: {nameRequest}");
        var msg = JsonSerializer.Deserialize<MessageRequest>(nameRequest);
        if (msg?.Type == MessageType.UserNameNegotiation)
        {
            var userName = msg?.Data?.Message;

            if (string.IsNullOrEmpty(userName))
            {
                await StreamUtils.WriteMessageAsync<MessageRequest>(stream, new MessageRequest
                {
                    NameNegotiationResponse = new Result { Success = false },
                    Type = MessageType.UserNameNegotiation,
                });

                return false;
            }

            if (_nodes.Any(node => node.Value.Name == userName) || _nameSet.ContainsKey(msg?.Data?.Message!))
            {
                Console.WriteLine($"Hash set contains: {msg.Data.Message}");
                await StreamUtils.WriteMessageAsync<MessageRequest>(stream, new MessageRequest
                {
                    NameNegotiationResponse = new Result { Success = false },
                    Type = MessageType.UserNameNegotiation,
                });
                return false;
            }

            return _nameSet.TryAdd(msg?.Data?.Message!, true);
        }

        return false;
    }

    private async Task ProcessNodeMessages(Node node)
    {
        TcpClient client = node.Client;
        NetworkStream netStream = client.GetStream();

        try
        {
            while (client.Connected)
            {
                var nodeMessage = await StreamUtils.ReadMessageAsync(netStream);

                if (!string.IsNullOrEmpty(nodeMessage))
                {
                    var message = JsonSerializer.Deserialize<MessageRequest>(nodeMessage);
                    if (message == null)
                    {
                        Console.WriteLine($"Error parsing message from {node.Name}");
                        continue;
                    }

                    switch (message.Type)
                    {
                        case MessageType.Message:
                            await HandleChatMessage(node, message);
                            break;
                        case MessageType.Command:
                            await HandleCommand(netStream, node, message.CommandRequest!);
                            break;
                    }
                }
            }

            // Client isn't connected anymore
            _removeNode(node);
            _cleanUpClient(client);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    private async Task HandleChatMessage(Node node, MessageRequest request)
    {
        if (!node.InChatRoom)
        {
            Console.WriteLine($"Node:{node.Name} ({node.Id}) is not in an active chat room. Please join a chat room to send a message");
            return; // return error object 
        }

        var chatRoom = (_chatRooms.FirstOrDefault(x => x.Key == node.ChatRoomId)).Value;
        if (chatRoom != null)
        {
            await chatRoom.PublishMessage(_nodes.Where(n => n.Value.ChatRoomId == node.ChatRoomId).Select(x => x.Value).ToList(), new Message
            { Id = Guid.NewGuid().ToString(), Value = request?.Data?.Message ?? "", CreatedOn = DateTime.UtcNow, CreatedBy = node.Id });
        }
    }

    private async Task HandleCommand(NetworkStream stream, Node node, CommandRequest command)
    {
        switch (command?.Command)
        {
            case CommandType.CreateChatRoom:
                await CreateChatRoomCommandHandler(stream, node, command);
                break;
            case CommandType.ViewChatRooms:
                await ViewChatRoomsCommandHandler(stream);
                break;
            case CommandType.JoinChatRoom:
                await JoinChatRoomCommandHandler(stream, node, command);
                break;
            case CommandType.LeaveChatRoom:
                await LeaveChatRoomCommandHandler(stream, node);
                break;
        }
    }

    private async Task CreateChatRoomCommandHandler(NetworkStream stream, Node node, CommandRequest command)
    {
        if (string.IsNullOrEmpty(command.Value))
        {
            await SendCommandResponse<CreateChatRoomResponse>(stream, new CreateChatRoomResponse
            {
                Success = false,
                ErrorMessage = "Invalid Chat room name"
            }, CommandType.CreateChatRoom);
        }

        if (_chatRooms.Any(x => x.Value.Name == command.Value))
        {
            await SendCommandResponse<CreateChatRoomResponse>(stream, new CreateChatRoomResponse
            {
                Success = false,
                ErrorMessage = "Chat room already exists"
            }, CommandType.CreateChatRoom);
        }
        else
        {
            var chatRoom = new ChatRoom(command.Value);
            _chatRooms.TryAdd(chatRoom.Id, chatRoom);

            node.InChatRoom = true;
            node.ChatRoomId = chatRoom.Id;

            await SendCommandResponse<CreateChatRoomResponse>(stream, new CreateChatRoomResponse
            {
                Success = true,
                ChatRoomName = command.Value
            }, CommandType.CreateChatRoom);
        }
    }

    private async Task ViewChatRoomsCommandHandler(NetworkStream stream)
    {
        var response = new ViewChatRoomsResponse
        {
            Success = true,
            Rooms = _chatRooms.Select(x => x.Value.Name).ToList()
        };

        await SendCommandResponse<ViewChatRoomsResponse>(stream, response, CommandType.ViewChatRooms);
    }

    private async Task LeaveChatRoomCommandHandler(NetworkStream stream, Node node)
    {
        var chatRoom = _chatRooms.FirstOrDefault(x => x.Key == node.ChatRoomId);
        if (chatRoom.Value != null)
        {
            var member = chatRoom.Value.Members.FirstOrDefault(x => x.Key == node.Id);
            chatRoom.Value.Members.TryRemove(member);
            node.InChatRoom = false;
            node.ChatRoomId = string.Empty;

            await SendCommandResponse<Result>(stream, new Result { Success = true }, CommandType.LeaveChatRoom);
        }
    }

    private async Task JoinChatRoomCommandHandler(NetworkStream stream, Node node, CommandRequest command)
    {
        if (string.IsNullOrEmpty(command.Value) || !_chatRooms.Any(room => room.Value.Name == command.Value))
        {
            await SendCommandResponse<JoinChatRoomResponse>(stream, new JoinChatRoomResponse
            {
                Success = false,
                ChatRoomName = string.Empty,
                ErrorMessage = $"Error finding chatroom with name: {command.Value}"
            }, CommandType.JoinChatRoom);
        }

        var chatRoom = _chatRooms.FirstOrDefault(room => room.Value.Name == command.Value);
        if (chatRoom.Value != null)
        {
            chatRoom.Value.Members.TryAdd(node.Id, true);
            node.InChatRoom = true;
            node.ChatRoomId = chatRoom.Key;

            await SendCommandResponse<JoinChatRoomResponse>(stream, new JoinChatRoomResponse
            {
                Success = true,
                ChatRoomName = chatRoom.Value.Name
            }, CommandType.JoinChatRoom);

            return;
        }

        await SendCommandResponse<JoinChatRoomResponse>(stream, new JoinChatRoomResponse
        {
            Success = false,
            ChatRoomName = string.Empty
        }, CommandType.JoinChatRoom);
    }

    private async Task SendCommandResponse<T>(NetworkStream stream, T commandResponse, CommandType commandType)
    {
        await StreamUtils.WriteMessageAsync<MessageRequest>(stream, new MessageRequest
        {
            Type = MessageType.CommandResponse,
            CommandResponse = new CommandResponse
            {
                Command = commandType,
                Value = JsonSerializer.Serialize(commandResponse)
            }
        });
    }

    private void _removeNode(Node node)
    {
        _nodes.Remove(node.Id, out Node? val);
        Console.WriteLine($"{node.Name} ({node.Id}) has disconnected");
    }

    private static void _cleanUpClient(TcpClient client)
    {
        client.GetStream().Close();
        client.Close();
    }

    public void Shutdown()
    {
        Running = false;
        Console.WriteLine("Shutting down server");
    }
}
