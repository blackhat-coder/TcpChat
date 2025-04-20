using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tcp.Shared.DTOs.Responses;

public class RegisterNodeResponse : Result
{
    public string ErrorMessage { get; set; }
}
