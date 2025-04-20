using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Tcp.Shared.Utilities;

public static class StreamUtils
{
    const int LengthPrefixSize = 4;

    /// <summary>
    /// Handles reading messages from a stream
    /// </summary>
    /// <param name="stream"></param>
    /// <returns></returns>
    public static async Task<string> ReadMessageAsync(NetworkStream stream)
    {
        var lengthPrefixByteArr = new byte[LengthPrefixSize];

        var bytesRead = await ReadBytesAsync(stream, lengthPrefixByteArr, 0, LengthPrefixSize);

        if (bytesRead < LengthPrefixSize) { return string.Empty; } // Consider throwing Exceptions here

        var messageLength = BitConverter.ToInt32(lengthPrefixByteArr, 0);
        var messageBytes = new byte[messageLength];

        bytesRead = await ReadBytesAsync(stream, messageBytes, 0, messageLength);

        if (bytesRead < messageLength) { return string.Empty; } // Consider throwing Exceptions here

        string message = Encoding.UTF8.GetString(messageBytes);
        return message;
    }

    /// <summary>
    /// Reads the exact amount of bytes from a stream into the buffer
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="buffer"></param>
    /// <param name="offset"></param>
    /// <param name="count"></param>
    /// <returns></returns>
    public static async Task<int> ReadBytesAsync(NetworkStream stream, byte[] buffer, int offset, int count)
    {
        var totalBytesRead = 0;
        while (totalBytesRead < count)
        {
            var bytesRead = await stream.ReadAsync(buffer, offset + totalBytesRead, count - totalBytesRead);
            if (bytesRead == 0)
            { break; } // EOS - End of Stream

            totalBytesRead += bytesRead;
        }

        return totalBytesRead;
    }

    /// <summary>
    /// Writes a message to the stream in a specified format, length of the message first, then the message
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="stream"></param>
    /// <param name="message"></param>
    /// <returns></returns>
    public static async Task WriteMessageAsync<T>(NetworkStream stream, T message)
    {
        var requestMsg = JsonSerializer.Serialize(message);
        var byteString = Encoding.UTF8.GetBytes(requestMsg);

        var byteLen = BitConverter.GetBytes(byteString.Length);

        await stream.WriteAsync(byteLen);
        await stream.WriteAsync(byteString);
    }
}