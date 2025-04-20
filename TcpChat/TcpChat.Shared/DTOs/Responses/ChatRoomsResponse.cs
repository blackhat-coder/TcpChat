using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tcp.Shared.DTOs.Responses;

public class ChatRoomsResponse
{
    public string Message { get; set; }
    public List<string> ChatRooms { get; set; }
}
