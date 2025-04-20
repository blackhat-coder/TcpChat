using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tcp.Shared.DTOs.Responses;
using Tcp.Shared.Enums;

namespace Tcp.Shared.DTOs.Requests;

public class MessageRequest
{
    public MessageType Type { get; set; }
    public MessageData? Data { get; set; }
    public CommandRequest? CommandRequest { get; set; }
    public CommandResponse? CommandResponse { get; set; }
    public Result? NameNegotiationResponse { get; set; }
}

public class MessageData
{
    public string? Message { get; set; }
    public string? SentBy { get; set; }
}