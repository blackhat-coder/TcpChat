using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tcp.Shared.Enums;

namespace Tcp.Shared.DTOs.Responses;

public class CommandResponse
{
    public CommandType Command { get; set; }
    public string? Value { get; set; }
}
