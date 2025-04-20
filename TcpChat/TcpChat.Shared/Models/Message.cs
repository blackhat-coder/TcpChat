using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tcp.Shared.Models;

public class Message
{
    public string Id { get; set; }
    public string Value { get; set; }
    /// <summary>
    /// Contains Id of the Node/Client that wrote the message
    /// </summary>
    public string CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
}
