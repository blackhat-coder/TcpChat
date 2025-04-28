using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tcp.Shared.DTOs.Responses
{
    public class ViewChatRoomsResponse : Result
    {
        public List<ChatRoomDto> Rooms { get; set; }
    }
}
