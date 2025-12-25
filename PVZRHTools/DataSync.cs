using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Windows;
using ToolModData;

namespace PVZRHTools;

public class DataSync
{
    public byte[] buffer;
    public bool closed;
    public Socket modifierSocket;

    public DataSync(int port)
    {
        buffer = new byte[1024 * 64];
        modifierSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        modifierSocket.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), port));
        modifierSocket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, Receive, modifierSocket);
    }

    public static bool Enabled { get; set; } = true;
    public static Lazy<DataSync> Instance { get; } = new();

    ~DataSync()
    {
        if (!modifierSocket.Poll(100, SelectMode.SelectRead))
        {
            modifierSocket.Shutdown(SocketShutdown.Both);
            modifierSocket.Close();
        }
    }

    public void ProcessData(string data)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(data) || string.IsNullOrEmpty(data)) return;
            var json = JsonNode.Parse(data)!.AsObject();
            switch ((int)json["ID"]!)
            {
                case 3:
                {
                    var igh = json.Deserialize(InGameHotkeysSGC.Default.InGameHotkeys);
                    Application.Current.Dispatcher.Invoke(() =>
                        MainWindow.Instance!.ViewModel.InitInGameHotkeys(igh.KeyCodes));
                    break;
                }
                case 4:
                {
                    var s = json.Deserialize(SyncTravelBuffSGC.Default.SyncTravelBuff);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (s.AdvInGame is not null && s.UltiInGame is not null)
                        {
                            Enabled = false;
                            for (var i = 0; i < s.AdvInGame.Count; i++)
                                MainWindow.Instance!.ViewModel.InGameBuffs[i].Enabled = s.AdvInGame[i];
                            for (var i = 0; i < s.UltiInGame.Count; i++)
                                MainWindow.Instance!.ViewModel.InGameBuffs[i + s.AdvInGame.Count].Enabled =
                                    s.UltiInGame[i];
                            Enabled = true;
                        }

                        if (s.DebuffsInGame is not null)
                        {
                            Enabled = false;
                            for (var i = 0; i < s.DebuffsInGame.Count; i++)
                                MainWindow.Instance!.ViewModel.InGameDebuffs[i].Enabled = s.DebuffsInGame[i];

                            Enabled = true;
                        }
                    });
                    break;
                }
                case 6:
                {
                    var iga = json.Deserialize(InGameActionsSGC.Default.InGameActions);
                    if (iga.WriteField is not null)
                        Application.Current.Dispatcher.Invoke(() =>
                            MainWindow.Instance!.ViewModel.FieldString = iga.WriteField);
                    if (iga.WriteZombies is not null)
                        Application.Current.Dispatcher.Invoke(() =>
                            MainWindow.Instance!.ViewModel.ZombieFieldString = iga.WriteZombies);
                    if (iga.WriteVases is not null)
                        Application.Current.Dispatcher.Invoke(() =>
                            MainWindow.Instance!.ViewModel.VasesFieldString = iga.WriteVases);
                    if (iga.WriteMix is not null)
                        Application.Current.Dispatcher.Invoke(() =>
                            MainWindow.Instance!.ViewModel.MixFieldString = iga.WriteMix);

                    break;
                }
                case 15:
                {
                    Application.Current.Dispatcher.Invoke(() => MainWindow.Instance!.ViewModel.SyncAll());
                    break;
                }
                case 16:
                {
                    closed = true;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MainWindow.Instance!.ViewModel.Save();
                        Environment.Exit(0);
                    });
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            File.WriteAllText("./ModifierError.txt", ex.Message + ex.StackTrace);
            MessageBox.Show(ex.ToString());
            Application.Current.Dispatcher.Invoke(Application.Current.Shutdown);
        }
    }

    public void Receive(IAsyncResult ar)
    {
        try
        {
            if (closed) return;
            var socket = ar.AsyncState as Socket;
            if (socket is not null)
            {
                var bytes = socket.EndReceive(ar);
                ar.AsyncWaitHandle.Close();
                ProcessData(Encoding.UTF8.GetString(buffer, 0, bytes));
                buffer = new byte[1024 * 64];
                socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, Receive, socket);
            }
        }
        catch (InvalidOperationException)
        {
            MainWindow.Instance?.ViewModel.Save();
            Environment.Exit(0);
        }
        catch (SocketException)
        {
            MainWindow.Instance?.ViewModel.Save();
            Environment.Exit(0);
        }
        catch (NullReferenceException)
        {
            MainWindow.Instance?.ViewModel.Save();
            Environment.Exit(0);
        }
    }

    public void SendData<T>(T data) where T : ISyncData
    {
        if (!App.inited) return;
        if (!Enabled) return;
        JsonTypeInfo jti = data.ID switch
        {
            1 => ValuePropertiesSGC.Default.ValueProperties,
            2 => BasicPropertiesSGC.Default.BasicProperties,
            3 => InGameHotkeysSGC.Default.InGameHotkeys,
            4 => SyncTravelBuffSGC.Default.SyncTravelBuff,
            6 => InGameActionsSGC.Default.InGameActions,
            7 => GameModesSGC.Default.GameModes,
            15 => SyncAllSGC.Default.SyncAll,
            16 => ExitSGC.Default.Exit,
            _ => throw new InvalidOperationException()
        };
        modifierSocket.Send(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data, jti)));
        Thread.Sleep(5);
    }
}