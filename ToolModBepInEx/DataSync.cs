using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using UnityEngine;
using static ToolModData.Modifier;

namespace ToolModBepInEx;

public class DataSync
{
    public byte[] buffer;
    public Socket gameSocket;
    public Socket modifierSocket;

    public DataSync()
    {
        buffer = new byte[1024 * 64];
        gameSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        gameSocket.Bind(new IPEndPoint(IPAddress.Parse("127.0.0.1"), Core.Port.Value.Value));
        Process modifier = new();
        ProcessStartInfo info = new()
        {
            FileName = "PVZRHTools/PVZRHTools.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true
        };
        info.ArgumentList.Add(CommandLineToken);
        info.ArgumentList.Add(Core.Port.Value.Value.ToString());
        modifier.StartInfo = info;
        gameSocket.Listen(1);
        modifier.Start();
        modifierSocket = gameSocket.Accept();
        modifierSocket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, Receive, modifierSocket);
    }

    public static Lazy<DataSync> Instance { get; } = new();

    ~DataSync()
    {
        gameSocket.Close();
        modifierSocket.Close();
    }

    public void Receive(IAsyncResult ar)
    {
        try
        {
            var socket = ar.AsyncState as Socket;
            if (socket is not null)
            {
                var bytes = socket.EndReceive(ar);
                ar.AsyncWaitHandle.Close();
                DataProcessor.AddData(Encoding.UTF8.GetString(buffer, 0, bytes));
                Array.Clear(buffer);
                buffer = new byte[1024 * 64];
                socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, Receive, socket);
            }
        }
        catch (SocketException)
        {
            Application.Quit();
        }
        catch (ObjectDisposedException)
        {
            Application.Quit();
        }
        catch (NullReferenceException)
        {
            Application.Quit();
        }
        catch (Exception e)
        {
            Core.Instance.Value.LoggerInstance.LogError(e);
            Application.Quit();
        }
    }

    public void SendData<T>(T data)
    {
        if (Dev) Core.Instance.Value.LoggerInstance.LogInfo("Send:" + JsonSerializer.Serialize(data));
        modifierSocket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data)), SocketFlags.None);
        Thread.Sleep(5);
    }
}