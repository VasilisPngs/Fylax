using System.Text;

namespace Fylax.Services;

public static class DnsMessage
{
    private const ushort TypeA = 1;
    private const ushort TypeAaaa = 28;

    public readonly record struct Question(string Name, ushort Type);

    public static string TypeName(ushort type)
    {
        return type switch
        {
            1 => "A",
            28 => "AAAA",
            5 => "CNAME",
            15 => "MX",
            16 => "TXT",
            2 => "NS",
            6 => "SOA",
            12 => "PTR",
            33 => "SRV",
            64 => "SVCB",
            65 => "HTTPS",
            257 => "CAA",
            _ => $"TYPE{type}"
        };
    }

    public static byte[] CreateQuery(string domain)
    {
        var query = new List<byte>();
        var id = (ushort)Random.Shared.Next(1, ushort.MaxValue);
        AddUInt16(query, id);
        AddUInt16(query, 0x0100);
        AddUInt16(query, 1);
        AddUInt16(query, 0);
        AddUInt16(query, 0);
        AddUInt16(query, 0);
        foreach (var label in domain.Trim('.').Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            query.Add((byte)bytes.Length);
            query.AddRange(bytes);
        }
        query.Add(0);
        AddUInt16(query, TypeA);
        AddUInt16(query, 1);
        return query.ToArray();
    }

    public static Question ReadQuestion(byte[] buffer, int length)
    {
        if (length < 13)
        {
            return new Question("", 0);
        }
        var offset = 12;
        var labels = new List<string>();
        while (offset < length)
        {
            var labelLength = buffer[offset++];
            if (labelLength == 0)
            {
                break;
            }
            if ((labelLength & 0xC0) != 0 || offset + labelLength > length)
            {
                return new Question("", 0);
            }
            labels.Add(Encoding.ASCII.GetString(buffer, offset, labelLength));
            offset += labelLength;
        }
        ushort type = 0;
        if (offset + 2 <= length)
        {
            type = (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
        }
        return new Question(string.Join('.', labels).ToLowerInvariant(), type);
    }

    public static ushort GetId(byte[] message)
    {
        return message.Length >= 2 ? (ushort)((message[0] << 8) | message[1]) : (ushort)0;
    }

    public static void SetId(byte[] message, ushort id)
    {
        if (message.Length < 2)
        {
            return;
        }
        message[0] = (byte)(id >> 8);
        message[1] = (byte)(id & 0xFF);
    }

    public static bool IsTruncated(byte[] message, int length)
    {
        return length >= 3 && (message[2] & 0x02) != 0;
    }

    public static byte[] CreateSinkholeResponse(byte[] query, int length)
    {
        var questionEnd = FindQuestionEnd(query, length);
        if (questionEnd < 0)
        {
            return CreateFailureResponse(query, length);
        }
        var type = ReadQuestion(query, length).Type;
        var answer = type switch
        {
            TypeA => BuildAnswer(TypeA, new byte[] { 0, 0, 0, 0 }),
            TypeAaaa => BuildAnswer(TypeAaaa, new byte[16]),
            _ => Array.Empty<byte>()
        };
        var response = new byte[questionEnd + answer.Length];
        Buffer.BlockCopy(query, 0, response, 0, questionEnd);
        response[2] = (byte)(0x80 | (query[2] & 0x01));
        response[3] = 0x80;
        WriteUInt16(response, 4, 1);
        WriteUInt16(response, 6, answer.Length > 0 ? (ushort)1 : (ushort)0);
        WriteUInt16(response, 8, 0);
        WriteUInt16(response, 10, 0);
        if (answer.Length > 0)
        {
            Buffer.BlockCopy(answer, 0, response, questionEnd, answer.Length);
        }
        return response;
    }

    public static byte[] CreateTruncatedResponse(byte[] query, int length)
    {
        var questionEnd = FindQuestionEnd(query, length);
        if (questionEnd < 0)
        {
            questionEnd = Math.Min(length, 12);
        }
        var response = new byte[questionEnd];
        Buffer.BlockCopy(query, 0, response, 0, questionEnd);
        response[2] = (byte)(0x82 | (query[2] & 0x01));
        response[3] = 0x80;
        WriteUInt16(response, 4, questionEnd > 12 ? (ushort)1 : (ushort)0);
        WriteUInt16(response, 6, 0);
        WriteUInt16(response, 8, 0);
        WriteUInt16(response, 10, 0);
        return response;
    }

    public static byte[] CreateFailureResponse(byte[] query, int length)
    {
        var size = Math.Max(length, 12);
        var response = new byte[size];
        Buffer.BlockCopy(query, 0, response, 0, Math.Min(length, size));
        response[2] = (byte)(0x80 | (query.Length > 2 ? query[2] & 0x01 : 0));
        response[3] = 0x82;
        WriteUInt16(response, 6, 0);
        WriteUInt16(response, 8, 0);
        WriteUInt16(response, 10, 0);
        return response;
    }

    public static int? ClampAnswerTtls(byte[] response, int length, int floor, int cap)
    {
        try
        {
            if (length < 12)
            {
                return null;
            }
            var ancount = (response[6] << 8) | response[7];
            if (ancount <= 0)
            {
                return null;
            }
            var offset = FindQuestionEnd(response, length);
            if (offset < 0)
            {
                return null;
            }
            var min = int.MaxValue;
            for (var i = 0; i < ancount; i++)
            {
                offset = SkipName(response, length, offset);
                if (offset < 0 || offset + 10 > length)
                {
                    return null;
                }
                var ttl = (response[offset + 4] << 24) | (response[offset + 5] << 16) | (response[offset + 6] << 8) | response[offset + 7];
                var clamped = ttl < floor ? floor : ttl > cap ? cap : ttl;
                response[offset + 4] = (byte)(clamped >> 24);
                response[offset + 5] = (byte)(clamped >> 16);
                response[offset + 6] = (byte)(clamped >> 8);
                response[offset + 7] = (byte)clamped;
                var rdlength = (response[offset + 8] << 8) | response[offset + 9];
                offset += 10 + rdlength;
                if (offset > length)
                {
                    return null;
                }
                if (clamped < min)
                {
                    min = clamped;
                }
            }
            return min == int.MaxValue ? null : min;
        }
        catch
        {
            return null;
        }
    }

    public static byte[]? TryGetFirstAddress(byte[] response, int length, ushort wantType)
    {
        try
        {
            if (length < 12)
            {
                return null;
            }
            var ancount = (response[6] << 8) | response[7];
            if (ancount <= 0)
            {
                return null;
            }
            var offset = FindQuestionEnd(response, length);
            if (offset < 0)
            {
                return null;
            }
            for (var i = 0; i < ancount; i++)
            {
                offset = SkipName(response, length, offset);
                if (offset < 0 || offset + 10 > length)
                {
                    return null;
                }
                var type = (response[offset] << 8) | response[offset + 1];
                var rdlength = (response[offset + 8] << 8) | response[offset + 9];
                var rdata = offset + 10;
                if (rdata + rdlength > length)
                {
                    return null;
                }
                if (type == wantType)
                {
                    var result = new byte[rdlength];
                    Buffer.BlockCopy(response, rdata, result, 0, rdlength);
                    return result;
                }
                offset = rdata + rdlength;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public static bool ContainsBlockedCname(byte[] response, int length, Func<string, bool> isBlocked)
    {
        try
        {
            if (length < 12)
            {
                return false;
            }
            var ancount = (response[6] << 8) | response[7];
            if (ancount <= 0)
            {
                return false;
            }
            var offset = FindQuestionEnd(response, length);
            if (offset < 0)
            {
                return false;
            }
            for (var i = 0; i < ancount; i++)
            {
                offset = SkipName(response, length, offset);
                if (offset < 0 || offset + 10 > length)
                {
                    return false;
                }
                var type = (response[offset] << 8) | response[offset + 1];
                var rdlength = (response[offset + 8] << 8) | response[offset + 9];
                var rdata = offset + 10;
                if (rdata + rdlength > length)
                {
                    return false;
                }
                if (type == 5)
                {
                    var target = ReadName(response, length, rdata);
                    if (!string.IsNullOrEmpty(target) && isBlocked(target))
                    {
                        return true;
                    }
                }
                offset = rdata + rdlength;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string ReadName(byte[] buffer, int length, int offset)
    {
        var labels = new List<string>();
        var pos = offset;
        var jumps = 0;
        while (pos >= 0 && pos < length && jumps < 64)
        {
            var labelLength = buffer[pos];
            if (labelLength == 0)
            {
                break;
            }
            if ((labelLength & 0xC0) == 0xC0)
            {
                if (pos + 1 >= length)
                {
                    break;
                }
                pos = ((labelLength & 0x3F) << 8) | buffer[pos + 1];
                jumps++;
                continue;
            }
            pos++;
            if (pos + labelLength > length)
            {
                break;
            }
            labels.Add(Encoding.ASCII.GetString(buffer, pos, labelLength));
            pos += labelLength;
        }
        return string.Join('.', labels).ToLowerInvariant();
    }

    private static byte[] BuildAnswer(ushort type, byte[] rdata)
    {
        var answer = new byte[12 + rdata.Length];
        answer[0] = 0xC0;
        answer[1] = 0x0C;
        WriteUInt16(answer, 2, type);
        WriteUInt16(answer, 4, 1);
        answer[6] = 0;
        answer[7] = 0;
        answer[8] = 0;
        answer[9] = 0;
        WriteUInt16(answer, 10, (ushort)rdata.Length);
        Buffer.BlockCopy(rdata, 0, answer, 12, rdata.Length);
        return answer;
    }

    private static int FindQuestionEnd(byte[] buffer, int length)
    {
        if (length < 12)
        {
            return -1;
        }
        var offset = 12;
        while (offset < length)
        {
            var labelLength = buffer[offset];
            if (labelLength == 0)
            {
                offset += 1;
                return offset + 4 <= length ? offset + 4 : -1;
            }
            if ((labelLength & 0xC0) != 0)
            {
                return -1;
            }
            offset += labelLength + 1;
        }
        return -1;
    }

    private static int SkipName(byte[] buffer, int length, int offset)
    {
        while (offset < length)
        {
            var labelLength = buffer[offset];
            if ((labelLength & 0xC0) == 0xC0)
            {
                return offset + 2;
            }
            if (labelLength == 0)
            {
                return offset + 1;
            }
            offset += labelLength + 1;
        }
        return -1;
    }

    private static void AddUInt16(List<byte> buffer, ushort value)
    {
        buffer.Add((byte)(value >> 8));
        buffer.Add((byte)(value & 0xFF));
    }

    private static void WriteUInt16(byte[] buffer, int offset, ushort value)
    {
        if (offset + 1 >= buffer.Length)
        {
            return;
        }
        buffer[offset] = (byte)(value >> 8);
        buffer[offset + 1] = (byte)(value & 0xFF);
    }
}
