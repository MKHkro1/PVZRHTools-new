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
    private StringBuilder dataBuffer = new StringBuilder(); // 累积接收的数据

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

    private void ProcessBufferedData()
    {
        // 尝试从缓冲区中提取完整的 JSON 对象
        var data = dataBuffer.ToString();
        if (string.IsNullOrWhiteSpace(data)) return;
        
        // 通过计算大括号的匹配来找到完整的 JSON 对象
        int braceCount = 0;
        int startIndex = -1;
        int processedLength = 0;
        
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] == '{')
            {
                if (startIndex == -1)
                {
                    startIndex = i;
                }
                braceCount++;
            }
            else if (data[i] == '}')
            {
                braceCount--;
                if (braceCount == 0 && startIndex != -1)
                {
                    // 找到了一个完整的 JSON 对象
                    try
                    {
                        var jsonData = data.Substring(startIndex, i - startIndex + 1);
                        ProcessData(jsonData);
                        
                        // 移除已处理的数据
                        processedLength = i + 1;
                        break;
                    }
                    catch (JsonException)
                    {
                        // 如果解析失败，可能是数据还不完整，继续等待更多数据
                        return;
                    }
                }
            }
        }
        
        // 移除已处理的数据
        if (processedLength > 0)
        {
            dataBuffer.Remove(0, processedLength);
            // 如果还有剩余数据，递归处理
            if (dataBuffer.Length > 0)
            {
                ProcessBufferedData();
            }
        }
    }

    public void ProcessData(string data)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(data) || string.IsNullOrEmpty(data)) return;
            
            // 尝试解析 JSON，如果失败则尝试分割多个 JSON 对象
            JsonObject? json = null;
            try
            {
                json = JsonNode.Parse(data)!.AsObject();
            }
            catch (JsonException)
            {
                // 如果解析失败，尝试查找第一个完整的 JSON 对象
                // 通过计算大括号的匹配来找到 JSON 边界
                int braceCount = 0;
                int startIndex = 0;
                bool foundStart = false;
                
                for (int i = 0; i < data.Length; i++)
                {
                    if (data[i] == '{')
                    {
                        if (!foundStart)
                        {
                            startIndex = i;
                            foundStart = true;
                        }
                        braceCount++;
                    }
                    else if (data[i] == '}')
                    {
                        braceCount--;
                        if (braceCount == 0 && foundStart)
                        {
                            // 找到了一个完整的 JSON 对象
                            try
                            {
                                var partialData = data.Substring(startIndex, i - startIndex + 1);
                                json = JsonNode.Parse(partialData)!.AsObject();
                                
                                // 如果还有剩余数据，递归处理
                                if (i + 1 < data.Length)
                                {
                                    var remainingData = data.Substring(i + 1).TrimStart();
                                    if (!string.IsNullOrEmpty(remainingData))
                                    {
                                        ProcessData(remainingData);
                                    }
                                }
                                break;
                            }
                            catch
                            {
                                // 如果这个片段也解析失败，记录错误并继续
                                foundStart = false;
                                braceCount = 0;
                            }
                        }
                    }
                }
                
                // 如果仍然无法解析，重新抛出原始异常
                if (json == null)
                {
                    throw; // 重新抛出原始异常
                }
            }
            
            if (json == null) return;
            
            // 检查 ID 字段是否存在
            var idNode = json["ID"];
            if (idNode == null)
            {
                return;
            }
            
            int id;
            try
            {
                id = (int)idNode;
            }
            catch
            {
                return;
            }
            
            switch (id)
            {
                case 0:
                {
                    // 接收更新后的InitData（包含MOD添加的词条）
                    try
                    {
                        var initData = json.Deserialize(InitDataSGC.Default.InitData);
                        
                        if (initData.AdvBuffs != null)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                App.InitData = initData;
                                
                                // 重新加载词条列表
                                if (MainWindow.Instance != null && MainWindow.Instance.ViewModel != null)
                                {
                                    MainWindow.Instance.ViewModel.ReloadBuffsFromInitData();
                                }
                            });
                        }
                    }
                    catch
                    {
                        // 静默处理错误，避免影响程序运行
                    }
                    break;
                }
                case 3:
                {
                    var igh = json.Deserialize(InGameHotkeysSGC.Default.InGameHotkeys);
                    if (igh.KeyCodes != null)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var vm = MainWindow.Instance?.ViewModel;
                            if (vm != null && vm.ShouldAcceptGameInGameHotkeys())
                            {
                                vm.InitInGameHotkeys(igh.KeyCodes);
                            }
                            else if (vm == null)
                            {
                                App.PendingGameInGameHotkeys = igh.KeyCodes;
                            }
                        });
                    }
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
                            var inGameBuffsCount = MainWindow.Instance!.ViewModel.InGameBuffs.Count;
                            for (var i = 0; i < s.AdvInGame.Count && i < inGameBuffsCount; i++)
                                MainWindow.Instance!.ViewModel.InGameBuffs[i].Enabled = s.AdvInGame[i];
                            for (var i = 0; i < s.UltiInGame.Count && i + s.AdvInGame.Count < inGameBuffsCount; i++)
                                MainWindow.Instance!.ViewModel.InGameBuffs[i + s.AdvInGame.Count].Enabled =
                                    s.UltiInGame[i];
                            Enabled = true;
                        }

                        // 投资词条局内同步
                        if (s.InvestInGame is not null)
                        {
                            Enabled = false;
                            var inGameInvestCount = MainWindow.Instance!.ViewModel.InGameInvestBuffs.Count;
                            for (var i = 0; i < s.InvestInGame.Count && i < inGameInvestCount; i++)
                                MainWindow.Instance!.ViewModel.InGameInvestBuffs[i].Enabled = s.InvestInGame[i];
                            Enabled = true;
                        }

                        if (s.DebuffsInGame is not null)
                        {
                            Enabled = false;
                            var inGameDebuffsCount = MainWindow.Instance!.ViewModel.InGameDebuffs.Count;
                            for (var i = 0; i < s.DebuffsInGame.Count && i < inGameDebuffsCount; i++)
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
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (MainWindow.Instance != null && MainWindow.Instance.ViewModel != null)
                        {
                            MainWindow.Instance.ViewModel.SyncAll();
                        }
                    });
                    break;
                }
                case 16:
                {
                    closed = true;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MainWindow.Instance?.ViewModel?.Save();
                        Environment.Exit(0);
                    });
                    break;
                }
                case 17:
                {
                    // 接收出怪列表数据
                    try
                    {
                        var zombieListData = json.Deserialize(ZombieListDataSGC.Default.ZombieListData);
                        
                        // ZombieListData是struct，不能与null比较，检查ZombieListByWave是否为null
                        if (zombieListData.ZombieListByWave != null)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                try
                                {
                                    if (MainWindow.Instance != null)
                                    {
                                        MainWindow.Instance.SetZombieListData(
                                            zombieListData.ZombieListByWave, 
                                            zombieListData.CurrentWave);
                                    }
                                }
                                catch
                                {
                                    // 静默处理错误
                                }
                            });
                        }
                    }
                    catch
                    {
                        // 静默处理错误
                    }
                    break;
                }
            }
        }
        catch (Exception ex)
        {
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
                
                // 累积接收的数据
                dataBuffer.Append(Encoding.UTF8.GetString(buffer, 0, bytes));
                
                // 尝试处理累积的数据
                ProcessBufferedData();
                
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
            17 => ZombieListDataSGC.Default.ZombieListData,
            18 => GodEvolutionPropertiesSGC.Default.GodEvolutionProperties,
            _ => throw new InvalidOperationException()
        };
        modifierSocket.Send(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data, jti)));
        Thread.Sleep(5);
    }
}