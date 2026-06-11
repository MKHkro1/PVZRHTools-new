using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ToolModData;
using UnityEngine;
using static ToolModData.Modifier;

namespace ToolModBepInEx;

public class DataSync : IDisposable
{
    private readonly string _pipeName;
    private NamedPipeServerStream? _pipeStream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;
    private bool _isRunning;
    private bool _connected;
    private readonly object _lock = new();
    private bool _disposed;

    private DataSync()
    {
        _pipeName = PipeName;
    }

    private static DataSync? _instance;
    private static readonly object InstanceLock = new();

    public static DataSync Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (InstanceLock)
                {
                    if (_instance == null)
                    {
                        while (!Core.inited)
                        {
                            Thread.Sleep(100);
                        }

                        _instance = new DataSync();
                    }
                }
            }

            return _instance;
        }
    }

    public static void Initialize()
    {
        if (_instance != null)
        {
            return;
        }

        lock (InstanceLock)
        {
            if (_instance != null)
            {
                return;
            }

            _instance = new DataSync();
            _instance.Start();
            _instance.LaunchModifier();
        }
    }

    public void Start()
    {
        if (_isRunning)
        {
            return;
        }

        lock (_lock)
        {
            if (_isRunning)
            {
                return;
            }

            _isRunning = true;
            _cts = new CancellationTokenSource();
            _serverTask = Task.Run(() => RunServerAsync(_cts.Token));
        }
    }

    private void LaunchModifier()
    {
        string modifierPath = ModifierPaths.ResolveModifierExe();
        if (!File.Exists(modifierPath))
        {
            throw new FileNotFoundException($"修改器不存在: {modifierPath}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = modifierPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Directory.GetCurrentDirectory()
        };
        startInfo.ArgumentList.Add(RunModifierArgument);
        startInfo.ArgumentList.Add(Directory.GetCurrentDirectory());

        Core.Instance.Value.LoggerInstance.LogInfo($"[PVZRHTools] DataSync: 启动修改器 {modifierPath}");
        Process.Start(startInfo);
    }

    public void SendData<T>(T data)
    {
        if (Dev)
        {
            Core.Instance.Value.LoggerInstance.LogInfo("Send:" + JsonSerializer.Serialize(data));
        }

        lock (_lock)
        {
            if (!_connected || _writer == null)
            {
                return;
            }

            try
            {
                string json = JsonSerializer.Serialize(data);
                _writer.WriteLine(json);
                _writer.Flush();
            }
            catch (Exception ex)
            {
                Core.Instance.Value.LoggerInstance.LogWarning($"[PVZRHTools] DataSync.SendData 失败: {ex.Message}");
            }
        }

        Thread.Sleep(5);
    }

    private async Task RunServerAsync(CancellationToken cancellationToken)
    {
        try
        {
            _pipeStream = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            using (cancellationToken.Register(() => _pipeStream?.Dispose()))
            {
                await _pipeStream.WaitForConnectionAsync(cancellationToken);
            }

            lock (_lock)
            {
                _reader = new StreamReader(_pipeStream, Encoding.UTF8);
                _writer = new StreamWriter(_pipeStream, Encoding.UTF8) { AutoFlush = true };
                _connected = true;
            }

            Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync: 修改器已连接");

            string? message;
            while (!cancellationToken.IsCancellationRequested &&
                   _reader != null &&
                   (message = await _reader.ReadLineAsync()) != null)
            {
                DataProcessor.AddData(message);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Core.Instance.Value.LoggerInstance.LogError($"[PVZRHTools] DataSync 服务异常: {ex.Message}");
        }
        finally
        {
            Cleanup();
            lock (_lock)
            {
                _isRunning = false;
                _connected = false;
            }

            try
            {
                Application.Quit();
            }
            catch
            {
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
            _connected = false;
            _cts?.Cancel();
            Cleanup();
        }

        try
        {
            _serverTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
    }

    private void Cleanup()
    {
        lock (_lock)
        {
            try { _reader?.Dispose(); } catch { }
            try { _writer?.Dispose(); } catch { }
            try { _pipeStream?.Dispose(); } catch { }
            _reader = null;
            _writer = null;
            _pipeStream = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _cts?.Dispose();
    }
}
