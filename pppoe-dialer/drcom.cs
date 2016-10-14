using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;
using System.Security.Cryptography;

public class drcom
{
    byte PPPOE_FLAG = 0x2a;

    private Socket client;
    private IPEndPoint hostipe;

    int pppoeCount = 1;

    public delegate void labelCallback(string status, string message);
    public labelCallback lbcallback;

    public drcom()
	{
	}

    /// <summary>
    /// 初始化Socket端口
    /// </summary>
    public void initSocket()
    {
        int port = 61440;
        string localhost = "0.0.0.0";
        string host = "10.0.3.2";

        IPEndPoint ipe = new IPEndPoint(IPAddress.Parse(localhost), port);
        hostipe = new IPEndPoint(IPAddress.Parse(host), port);

        try
        {
            client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            client.ReceiveTimeout = 3000;
            client.Bind(ipe);
            Console.WriteLine("success");
        }
        catch (SocketException e)
        {
            Console.WriteLine("SocketException:{0}", e);
            lbcallback("端口绑定异常", "请关闭本客户端并10秒后重试");
        }
    }
    /// <summary>
    /// 认证过程
    /// </summary>
    /// <returns>状态</returns>
    public int auth()
    {
        while (true)
        {
            if (heartbeat() == 0)
            {
                lbcallback("异常断开", "正在重连");
                return 0;
            }
            Thread.Sleep(20000);
        }
    }
    /// <summary>
    /// 心跳
    /// </summary>
    /// <returns>结果</returns>
    private int heartbeat()
    {
        while (true)
        {
            //Step 1. 发送握手包
            byte[] packet = generateStartPacket((byte)pppoeCount);

            lbcallback("发送握手包", null);
            Console.WriteLine("pppoe: send challenge request");

            try
            {
                client.SendTo(packet, packet.Length, SocketFlags.None, hostipe);
            }
            catch (SocketException e)
            {
                Console.WriteLine("SocketException:{0}", e);
            }

            //Step 2. 接受握手包
            byte[] recvBuff = recv_packet();
            if (recvBuff == null)
            {
                lbcallback("内部错误，重新开始拨号", null);
                return 0;
            }
            Console.WriteLine("pppoe: received challenge response");
            lbcallback("收到握手包", null);


            //Step 3. 发送心跳包
            byte[] seed = new byte[4], sip = new byte[4];

            for (int i = 0; i < 4; i++)
            {
                seed[i] = recvBuff[8 + i];
                sip[i] = recvBuff[12 + i];
            }

            byte[] hbPacket;
            if (pppoeCount > 2)
                hbPacket = generateHeartbeatPacket((byte)pppoeCount, sip, seed, false);
            else
                hbPacket = generateHeartbeatPacket((byte)pppoeCount, sip, seed, true);
            try
            {
                client.SendTo(hbPacket, hbPacket.Length, SocketFlags.None, hostipe);
            }
            catch (SocketException e)
            {
                Console.WriteLine("SocketException:{0}", e.Message);
            }
            lbcallback("第" + (pppoeCount - 1) + "次发送心跳包",null);
            Console.WriteLine("pppoe: send heartbeat request");


            //Step 4. 接收心跳包
            if (recv_packet() == null)
            {
                lbcallback(null,"拨号失败，重新拨号中");

                Console.WriteLine("pppoe: heartbeat response failed, retry");
                Console.WriteLine("pppoe: reset idx to 0x01\n");
                pppoeCount = 1;
                Thread.Sleep(1000);
                return 0;
            }
            else
            {
                Console.WriteLine("pppoe: received heartbeat response");
                lbcallback("第" + (pppoeCount - 1) + "次发送心跳包成功", "拨号成功");
                break;
            }
        }
        return 1;
    }

    public void exit()
    {
        client.Close();
    }
    /// <summary>
    /// 发送包
    /// </summary>
    /// <param name="data">包数据</param>
    /// <returns>发送结果</returns>
    int sent_packet(byte[] data)
    {
        int result = client.SendTo(data, data.Length, SocketFlags.None, hostipe);
        if (result < 0)
            Console.WriteLine("Send packet error");
        return result;
    }
    /// <summary>
    /// 接收包
    /// </summary>
    /// <returns>收到的数据</returns>
    byte[] recv_packet()
    {
        byte[] recBytes = new byte[4096];
        EndPoint remote = (EndPoint)hostipe;
        while (true)
        {
            try
            {
                int bytes = client.ReceiveFrom(recBytes, 0, recBytes.Length, SocketFlags.None, ref remote);
            }
            catch (SocketException e)
            {
                Console.WriteLine("SocketException:{0}: {1}", e.ErrorCode, e.Message);
                return null;
            }

            if (recBytes[0] == 0x4d)
            {
                //cut_char(recvBuff, (unsigned char *) & realStr, 3);
                Console.WriteLine("Message: {0}", recBytes);
                continue;
            }
            else return recBytes;
        }
    }
    /// <summary>
    /// 生成握手包
    /// </summary>
    /// <param name="count">包计数</param>
    /// <returns>握手包</returns>
    byte[] generateStartPacket(byte count)
    {
        int index = 0;
        byte[] packet = new byte[8];
        packet[index++] = 0x07;
        packet[index++] = (byte)count;
        packet[index++] = 0x08;
        packet[index++] = 0x00;
        packet[index++] = 0x01;
        packet[index++] = 0x00;
        packet[index++] = 0x00;
        packet[index++] = 0x00;

        return packet;
    }

    /// <summary>
    /// 生成心跳包
    /// </summary>
    /// <param name="count">包计数</param>
    /// <param name="sip">服务器IP</param>
    /// <param name="seed">随机种子</param>
    /// <param name="isFirst">是否第一次发送</param>
    /// <returns>心跳包</returns>
    byte[] generateHeartbeatPacket(byte count, byte[] sip, byte[] seed, bool isFirst)
    {
        int index = 0;
        byte[] packet = new byte[100];
        packet[index++] = 0x07; //header
        packet[index++] = count; //id
        packet[index++] = 0x60; //length
        packet[index++] = 0x00;
        packet[index++] = 0x03; //type
        packet[index++] = 0x00; //uid length
        for (int i = 0; i < 6; i++)
        {
            packet[index++] = 0x00; //mac
        }
        for (int i = 0; i < 4; i++)
        {
            packet[index++] = sip[i]; //serverip
        }

        //Option
        packet[index++] = 0x00;

        if (isFirst)
            packet[index++] = 0x62;
        else
            packet[index++] = 0x63;

        packet[index++] = 0x00;
        packet[index++] = PPPOE_FLAG;

        //ChallengeSeed
        for (int i = 0; i < 4; i++)
        {
            packet[index++] = seed[i];
        }

        ///计算CRC
        byte[] crc = new byte[8];

        int encryptMode = seed[0] % 4;
        crc = genCRC(seed, encryptMode);

        ///填充CRC
        for (int i = 0; i < 8; i++)
        {
            packet[index++] = crc[i];
        }
        ///填充IP
        for (int i = 0; i < 68; i++)
        {
            //0x00 * 4 CRC
            //0x00 * 16 ip * 4
            packet[index++] = 0x00;
        }
        return packet;
    }

    /// <summary>
    /// 生成校验
    /// </summary>
    /// <param name="data">服务器发来的种子</param>
    /// <param name="type">校验类型</param>
    /// <returns></returns>
    byte[] genCRC(byte[] data, int type)
    {
        int index = 0;
        byte[] result = new byte[8];
        //byte[] mdResult = new byte[20];

        switch (type)
        {
            case 0: //default
                    //20000711
                result[index++] = 0xC7;
                result[index++] = 0x2F;
                result[index++] = 0x31;
                result[index++] = 0x01;
                //126
                result[index++] = 0x7E;
                result[index++] = 0x00;
                result[index++] = 0x00;
                result[index++] = 0x00;
                break;
            case 1: //md5
                MD5 md5 = new MD5CryptoServiceProvider();
                byte[] mdResult = md5.ComputeHash(data);

                result[index++] = mdResult[2];
                result[index++] = mdResult[3];
                result[index++] = mdResult[8];
                result[index++] = mdResult[9];
                result[index++] = mdResult[5];
                result[index++] = mdResult[6];
                result[index++] = mdResult[13];
                result[index++] = mdResult[14];
                break;
            case 2: //md4
                MD4 md4 = new MD4();
                byte[] md4Result = md4.ComputeHash(data);

                result[index++] = md4Result[1];
                result[index++] = md4Result[2];
                result[index++] = md4Result[8];
                result[index++] = md4Result[9];
                result[index++] = md4Result[4];
                result[index++] = md4Result[5];
                result[index++] = md4Result[11];
                result[index++] = md4Result[12];
                break;
            case 3: //sha1
                SHA1 SHA1 = new SHA1CryptoServiceProvider();
                byte[] shaResult = SHA1.ComputeHash(data);

                result[index++] = shaResult[2];
                result[index++] = shaResult[3];
                result[index++] = shaResult[9];
                result[index++] = shaResult[10];
                result[index++] = shaResult[5];
                result[index++] = shaResult[6];
                result[index++] = shaResult[15];
                result[index++] = shaResult[16];
                break;
            default:
                //printf("Invalid CRC type %d\n", type);
                break;
        }
        return result;
    }

}

public class MD4 : HashAlgorithm
{
    private uint _a;
    private uint _b;
    private uint _c;
    private uint _d;
    private uint[] _x;
    private int _bytesProcessed;

    public MD4()
    {
        _x = new uint[16];

        Initialize();
    }

    public override void Initialize()
    {
        _a = 0x67452301;
        _b = 0xefcdab89;
        _c = 0x98badcfe;
        _d = 0x10325476;

        _bytesProcessed = 0;
    }

    protected override void HashCore(byte[] array, int offset, int length)
    {
        ProcessMessage(Bytes(array, offset, length));
    }

    protected override byte[] HashFinal()
    {
        try
        {
            ProcessMessage(Padding());

            return new[] { _a, _b, _c, _d }.SelectMany(word => Bytes(word)).ToArray();
        }
        finally
        {
            Initialize();
        }
    }

    private void ProcessMessage(IEnumerable<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            int c = _bytesProcessed & 63;
            int i = c >> 2;
            int s = (c & 3) << 3;

            _x[i] = (_x[i] & ~((uint)255 << s)) | ((uint)b << s);

            if (c == 63)
            {
                Process16WordBlock();
            }

            _bytesProcessed++;
        }
    }

    private static IEnumerable<byte> Bytes(byte[] bytes, int offset, int length)
    {
        for (int i = offset; i < length; i++)
        {
            yield return bytes[i];
        }
    }

    private IEnumerable<byte> Bytes(uint word)
    {
        yield return (byte)(word & 255);
        yield return (byte)((word >> 8) & 255);
        yield return (byte)((word >> 16) & 255);
        yield return (byte)((word >> 24) & 255);
    }

    private IEnumerable<byte> Repeat(byte value, int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return value;
        }
    }

    private IEnumerable<byte> Padding()
    {
        return Repeat(128, 1)
           .Concat(Repeat(0, ((_bytesProcessed + 8) & 0x7fffffc0) + 55 - _bytesProcessed))
           .Concat(Bytes((uint)_bytesProcessed << 3))
           .Concat(Repeat(0, 4));
    }

    private void Process16WordBlock()
    {
        uint aa = _a;
        uint bb = _b;
        uint cc = _c;
        uint dd = _d;

        foreach (int k in new[] { 0, 4, 8, 12 })
        {
            aa = Round1Operation(aa, bb, cc, dd, _x[k], 3);
            dd = Round1Operation(dd, aa, bb, cc, _x[k + 1], 7);
            cc = Round1Operation(cc, dd, aa, bb, _x[k + 2], 11);
            bb = Round1Operation(bb, cc, dd, aa, _x[k + 3], 19);
        }

        foreach (int k in new[] { 0, 1, 2, 3 })
        {
            aa = Round2Operation(aa, bb, cc, dd, _x[k], 3);
            dd = Round2Operation(dd, aa, bb, cc, _x[k + 4], 5);
            cc = Round2Operation(cc, dd, aa, bb, _x[k + 8], 9);
            bb = Round2Operation(bb, cc, dd, aa, _x[k + 12], 13);
        }

        foreach (int k in new[] { 0, 2, 1, 3 })
        {
            aa = Round3Operation(aa, bb, cc, dd, _x[k], 3);
            dd = Round3Operation(dd, aa, bb, cc, _x[k + 8], 9);
            cc = Round3Operation(cc, dd, aa, bb, _x[k + 4], 11);
            bb = Round3Operation(bb, cc, dd, aa, _x[k + 12], 15);
        }

        unchecked
        {
            _a += aa;
            _b += bb;
            _c += cc;
            _d += dd;
        }
    }

    private static uint ROL(uint value, int numberOfBits)
    {
        return (value << numberOfBits) | (value >> (32 - numberOfBits));
    }

    private static uint Round1Operation(uint a, uint b, uint c, uint d, uint xk, int s)
    {
        unchecked
        {
            return ROL(a + ((b & c) | (~b & d)) + xk, s);
        }
    }

    private static uint Round2Operation(uint a, uint b, uint c, uint d, uint xk, int s)
    {
        unchecked
        {
            return ROL(a + ((b & c) | (b & d) | (c & d)) + xk + 0x5a827999, s);
        }
    }

    private static uint Round3Operation(uint a, uint b, uint c, uint d, uint xk, int s)
    {
        unchecked
        {
            return ROL(a + (b ^ c ^ d) + xk + 0x6ed9eba1, s);
        }
    }
}