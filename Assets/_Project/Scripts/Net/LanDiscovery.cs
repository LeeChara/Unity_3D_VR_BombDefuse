using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

/// <summary>
/// 같은 네트워크 안에서 호스트를 찾는다. 호스트는 UDP 브로드캐스트로 게임 포트를 알리고, 클라이언트는 그 알림을 듣는다.
/// 스레드를 쓰지 않는다. 부르는 쪽이 매 프레임 <see cref="Broadcast"/>나 <see cref="TryReceive"/>를 부른다.
/// </summary>
public sealed class LanDiscovery : IDisposable
{
    const string k_Header = "BOMBDEFUSE_LAN|";

    UdpClient m_Udp;
    IPEndPoint m_BroadcastEndPoint;

    /// <summary>호스트 쪽. 브로드캐스트를 보낼 준비를 한다.</summary>
    public void StartAnnouncing(ushort discoveryPort)
    {
        Dispose();
        m_Udp = new UdpClient { EnableBroadcast = true };
        m_BroadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, discoveryPort);
    }

    /// <summary>호스트 쪽. 게임 포트를 한 번 알린다.</summary>
    public void Broadcast(ushort gamePort)
    {
        if (m_Udp == null || m_BroadcastEndPoint == null)
            return;

        var bytes = Encoding.UTF8.GetBytes(k_Header + gamePort);
        try
        {
            m_Udp.Send(bytes, bytes.Length, m_BroadcastEndPoint);
        }
        catch (SocketException)
        {
            // 네트워크가 잠깐 없을 때는 다음 알림에서 다시 보낸다
        }
    }

    /// <summary>클라이언트 쪽. 알림을 받을 포트를 연다. 한 컴퓨터에서 여러 개가 열 수 있다.</summary>
    public void StartListening(ushort discoveryPort)
    {
        Dispose();
        m_Udp = new UdpClient();
        m_Udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        m_Udp.Client.Bind(new IPEndPoint(IPAddress.Any, discoveryPort));
    }

    /// <summary>클라이언트 쪽. 받은 알림이 있으면 호스트 주소와 게임 포트를 돌려준다.</summary>
    public bool TryReceive(out string address, out ushort gamePort)
    {
        address = null;
        gamePort = 0;
        if (m_Udp == null || m_BroadcastEndPoint != null)
            return false;

        try
        {
            while (m_Udp.Available > 0)
            {
                var sender = new IPEndPoint(IPAddress.Any, 0);
                var message = Encoding.UTF8.GetString(m_Udp.Receive(ref sender));
                if (message.StartsWith(k_Header) && ushort.TryParse(message.Substring(k_Header.Length), out gamePort))
                {
                    address = sender.Address.ToString();
                    return true;
                }
            }
        }
        catch (SocketException)
        {
        }

        return false;
    }

    public void Dispose()
    {
        m_Udp?.Close();
        m_Udp = null;
        m_BroadcastEndPoint = null;
    }

    /// <summary>이 기기의 LAN IPv4 주소. 못 찾으면 127.0.0.1.</summary>
    public static string GetLocalAddress()
    {
        string fallback = null;
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var properties = adapter.GetIPProperties();
                foreach (var unicast in properties.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    // 게이트웨이가 있는 어댑터(실제 공유기에 붙은 쪽)를 먼저 고른다
                    if (properties.GatewayAddresses.Count > 0)
                        return unicast.Address.ToString();
                    fallback ??= unicast.Address.ToString();
                }
            }
        }
        catch (NetworkInformationException)
        {
        }

        return fallback ?? "127.0.0.1";
    }
}
