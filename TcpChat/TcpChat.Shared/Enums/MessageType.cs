using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tcp.Shared.Enums;

public enum MessageType
{
    Message,
    Command,
    CommandResponse,
    UserNameNegotiation
}
