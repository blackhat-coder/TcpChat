using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Tcp.Shared.Models;

public class Node : BaseModel
{
    public TcpClient Client { get; set; }
    public string Name { get; set; }
    public string Id { get; set; }
    public string ChatRoomId { get; set; }
    public bool InChatRoom { get; set; } = false;
}
