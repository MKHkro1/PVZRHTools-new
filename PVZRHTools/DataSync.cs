using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Windows;
using ToolModData;
using static ToolModData.Modifier;

namespace PVZRHTools;

public class DataSync : IDisposable
{
    private readonly NamedPipeClientStream _pipeStream;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _receiveTask;
    public bool closed;

    public DataSync()
    {
        _pipeStream = new NamedPipeClientStream(
            ".",
            PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        _pipeStream.Connect(30000);
        _reader = new StreamReader(_pipeStream, Encoding.UTF8);
        _writer = new StreamWriter(_pipeStream, Encoding.UTF8) { AutoFlush = true };
        _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token));
    }

    public static bool Enabled { get; set; } = true;

    private async Task ReceiveLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !closed)
            {
                string? line = await _reader.ReadLineAsync(cancellationToken);
                if (line == null)
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    Application.Current.Dispatcher.Invoke(() => ProcessData(line));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            if (!closed)
            {
                closed = true;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MainWindow.Instance?.ViewModel.Save();
                    Environment.Exit(0);
                });
            }
        }
    }

    public void ProcessData(string data)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(data))
            {
                return;
            }

            JsonObject? json = JsonNode.Parse(data)?.AsObject();
            if (json == null)
            {
                return;
            }

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
                    try
                    {
                        var initData = json.Deserialize(InitDataSGC.Default.InitData);
                        if (initData.AdvBuffs != null)
                        {
                            App.InitData = initData;
                            if (MainWindow.Instance?.ViewModel != null)
                            {
                                MainWindow.Instance.ViewModel.ReloadBuffsFromInitData();
                            }
                        }
                    }
                    catch
                    {
                    }

                    break;
                }
                case 3:
                {
                    var igh = json.Deserialize(InGameHotkeysSGC.Default.InGameHotkeys);
                    if (igh.KeyCodes != null && MainWindow.Instance?.ViewModel != null)
                    {
                        MainWindow.Instance.ViewModel.InitInGameHotkeys(igh.KeyCodes);
                    }

                    break;
                }
                case 4:
                {
                    var s = json.Deserialize(SyncTravelBuffSGC.Default.SyncTravelBuff);
                    if (s.AdvInGame is not null && s.UltiInGame is not null)
                    {
                        Enabled = false;
                        var inGameBuffsCount = MainWindow.Instance!.ViewModel.InGameBuffs.Count;
                        for (var i = 0; i < s.AdvInGame.Count && i < inGameBuffsCount; i++)
                        {
                            MainWindow.Instance.ViewModel.InGameBuffs[i].Enabled = s.AdvInGame[i];
                        }

                        for (var i = 0; i < s.UltiInGame.Count && i + s.AdvInGame.Count < inGameBuffsCount; i++)
                        {
                            MainWindow.Instance.ViewModel.InGameBuffs[i + s.AdvInGame.Count].Enabled = s.UltiInGame[i];
                        }

                        Enabled = true;
                    }

                    if (s.InvestInGame is not null)
                    {
                        Enabled = false;
                        var inGameInvestCount = MainWindow.Instance!.ViewModel.InGameInvestBuffs.Count;
                        for (var i = 0; i < s.InvestInGame.Count && i < inGameInvestCount; i++)
                        {
                            MainWindow.Instance.ViewModel.InGameInvestBuffs[i].Enabled = s.InvestInGame[i];
                        }

                        Enabled = true;
                    }

                    if (s.DebuffsInGame is not null)
                    {
                        Enabled = false;
                        var inGameDebuffsCount = MainWindow.Instance!.ViewModel.InGameDebuffs.Count;
                        for (var i = 0; i < s.DebuffsInGame.Count && i < inGameDebuffsCount; i++)
                        {
                            MainWindow.Instance.ViewModel.InGameDebuffs[i].Enabled = s.DebuffsInGame[i];
                        }

                        Enabled = true;
                    }

                    break;
                }
                case 6:
                {
                    var iga = json.Deserialize(InGameActionsSGC.Default.InGameActions);
                    if (iga.WriteField is not null)
                    {
                        MainWindow.Instance!.ViewModel.FieldString = iga.WriteField;
                    }

                    if (iga.WriteZombies is not null)
                    {
                        MainWindow.Instance!.ViewModel.ZombieFieldString = iga.WriteZombies;
                    }

                    if (iga.WriteVases is not null)
                    {
                        MainWindow.Instance!.ViewModel.VasesFieldString = iga.WriteVases;
                    }

                    if (iga.WriteMix is not null)
                    {
                        MainWindow.Instance!.ViewModel.MixFieldString = iga.WriteMix;
                    }

                    break;
                }
                case 15:
                {
                    MainWindow.Instance?.ViewModel.SyncAll();
                    break;
                }
                case 16:
                {
                    closed = true;
                    MainWindow.Instance?.ViewModel.Save();
                    Environment.Exit(0);
                    break;
                }
                case 17:
                {
                    try
                    {
                        var zombieListData = json.Deserialize(ZombieListDataSGC.Default.ZombieListData);
                        if (zombieListData.ZombieListByWave != null && MainWindow.Instance != null)
                        {
                            MainWindow.Instance.SetZombieListData(
                                zombieListData.ZombieListByWave,
                                zombieListData.CurrentWave);
                        }
                    }
                    catch
                    {
                    }

                    break;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString());
            Application.Current.Shutdown();
        }
    }

    public void SendData<T>(T data) where T : ISyncData
    {
        if (!App.inited || !Enabled || closed)
        {
            return;
        }

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

        try
        {
            _writer.WriteLine(JsonSerializer.Serialize(data, jti));
            _writer.Flush();
        }
        catch
        {
            closed = true;
            MainWindow.Instance?.ViewModel.Save();
            Environment.Exit(0);
        }

        Thread.Sleep(5);
    }

    public void Dispose()
    {
        closed = true;
        _cts.Cancel();
        try
        {
            _receiveTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
        }

        try { _reader.Dispose(); } catch { }
        try { _writer.Dispose(); } catch { }
        try { _pipeStream.Dispose(); } catch { }
        _cts.Dispose();
    }
}
