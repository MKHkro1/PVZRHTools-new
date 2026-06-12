using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
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
        try
        {
            Core.Instance.Value.LoggerInstance.LogInfo($"[PVZRHTools] DataSync: 开始初始化，端口={Core.Port.Value.Value}");
        buffer = new byte[1024 * 64];
        gameSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        gameSocket.Bind(new IPEndPoint(IPAddress.Parse("127.0.0.1"), Core.Port.Value.Value));
            Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync: Socket 已绑定");
            
        Process modifier = new();
        ProcessStartInfo info = new()
        {
            FileName = "PVZRHTools/PVZRHTools.exe",
            UseShellExecute = false,
                RedirectStandardOutput = true,
                WorkingDirectory = System.IO.Directory.GetCurrentDirectory()
        };
        info.ArgumentList.Add(CommandLineToken);
        info.ArgumentList.Add(Core.Port.Value.Value.ToString());
        modifier.StartInfo = info;
            
        gameSocket.Listen(1);
            Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync: Socket 开始监听，准备启动修改器");
            
            // 检查修改器文件是否存在
            var modifierPath = "PVZRHTools/PVZRHTools.exe";
            var fullPath = System.IO.Path.GetFullPath(modifierPath);
            Core.Instance.Value.LoggerInstance.LogInfo($"[PVZRHTools] DataSync: 检查修改器文件路径: {fullPath}");
            if (!System.IO.File.Exists(modifierPath))
            {
                Core.Instance.Value.LoggerInstance.LogError($"[PVZRHTools] DataSync: 修改器文件不存在: {modifierPath} (完整路径: {fullPath})");
                throw new System.Exception($"修改器文件不存在: {modifierPath}");
            }
            Core.Instance.Value.LoggerInstance.LogInfo($"[PVZRHTools] DataSync: 修改器文件存在，准备启动");
            
            Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync: 准备启动修改器进程...");
        modifier.Start();
            Core.Instance.Value.LoggerInstance.LogInfo($"[PVZRHTools] DataSync: 修改器进程已启动 (PID: {modifier.Id})，等待连接...");
            
            // 设置接收超时（30秒）
            gameSocket.ReceiveTimeout = 30000;
            
            // 使用异步 Accept，但用同步方式等待结果（带超时）
            Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync: 开始等待修改器连接（最多30秒）...");
            var acceptResult = gameSocket.BeginAccept(null, null);
            Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync: BeginAccept 已调用，等待连接...");
            
            if (acceptResult.AsyncWaitHandle.WaitOne(30000)) // 等待30秒
            {
                Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync: 检测到连接，正在接受...");
                modifierSocket = gameSocket.EndAccept(acceptResult);
                Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync: 修改器已连接成功");
        modifierSocket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, Receive, modifierSocket);
                Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync: 已开始接收数据");
            }
            else
            {
                Core.Instance.Value.LoggerInstance.LogError("[PVZRHTools] DataSync: 等待修改器连接超时（30秒）");
                Core.Instance.Value.LoggerInstance.LogInfo($"[PVZRHTools] DataSync: 修改器进程状态 - HasExited: {modifier.HasExited}");
                if (modifier.HasExited)
                {
                    Core.Instance.Value.LoggerInstance.LogError($"[PVZRHTools] DataSync: 修改器进程已退出，退出代码: {modifier.ExitCode}");
                }
                gameSocket.Close();
                throw new System.Exception("等待修改器连接超时");
            }
        }
        catch (System.Exception ex)
        {
            Core.Instance.Value.LoggerInstance.LogError($"[PVZRHTools] DataSync 初始化失败: {ex.Message}\n{ex.StackTrace}");
            throw;
        }
    }

    // 延迟初始化：只有在 LateInit 完成后才创建实例（启动修改器）
    private static DataSync? _instance;
    private static readonly object _lock = new();
    
    public static DataSync Instance
    {
        get
        {
            Core.Instance.Value.LoggerInstance.LogInfo($"[PVZRHTools] DataSync.Instance.get: 被调用，线程 ID: {Thread.CurrentThread.ManagedThreadId}");
            if (_instance == null)
            {
                Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync.Instance.get: _instance 为 null，准备获取锁");
                lock (_lock)
                {
                    Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync.Instance.get: 已获取锁");
                    if (_instance == null)
                    {
                        Core.Instance.Value.LoggerInstance.LogInfo($"[PVZRHTools] DataSync.Instance.get: Core.inited = {Core.inited}，等待 LateInit 完成...");
                        // 如果 LateInit 还没完成，等待
                        while (!Core.inited)
                        {
                            Thread.Sleep(100);
                        }
                        Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync.Instance.get: LateInit 已完成，开始创建实例");
                        _instance = new DataSync();
                        Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync.Instance.get: 实例创建完成");
                    }
                    else
                    {
                        Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync.Instance.get: _instance 已被其他线程创建");
                    }
                }
                Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync.Instance.get: 已释放锁");
            }
            else
            {
                Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync.Instance.get: _instance 已存在，直接返回");
            }
            return _instance;
        }
    }
    
    // 用于在 LateInit 完成后显式初始化
    public static void Initialize()
    {
        Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync.Initialize: 开始执行");
        Core.Instance.Value.LoggerInstance.LogInfo($"[PVZRHTools] DataSync.Initialize: 当前线程 ID: {Thread.CurrentThread.ManagedThreadId}");
        Core.Instance.Value.LoggerInstance.LogInfo($"[PVZRHTools] DataSync.Initialize: _instance 当前状态: {(_instance == null ? "null" : "已存在")}");
        
        if (_instance == null)
        {
            Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync.Initialize: _instance 为 null，准备创建新实例");
            Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync.Initialize: 准备获取锁...");
            
            bool lockAcquired = false;
            try
            {
                // 使用 Monitor.TryEnter 来避免无限等待，并添加超时
                if (Monitor.TryEnter(_lock, 5000)) // 5秒超时
                {
                    lockAcquired = true;
                    Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync.Initialize: 已获取锁");
                    
                    if (_instance == null)
                    {
                        Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync.Initialize: 开始创建 DataSync 实例（这将启动修改器）");
                        try
                        {
                            _instance = new DataSync();
                            Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync.Initialize: DataSync 实例创建成功");
                        }
                        catch (System.Exception ex)
                        {
                            Core.Instance.Value.LoggerInstance.LogError($"[PVZRHTools] DataSync.Initialize: 创建 DataSync 实例失败: {ex.Message}\n{ex.StackTrace}");
                            throw;
                        }
                    }
                    else
                    {
                        Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync.Initialize: _instance 已被其他线程创建");
                    }
                }
                else
                {
                    // 获取锁超时，静默处理（不记录错误日志）
                    throw new System.Exception("获取锁超时，可能有死锁");
                }
            }
            finally
            {
                if (lockAcquired)
                {
                    Monitor.Exit(_lock);
                    Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync.Initialize: 已释放锁");
                }
            }
        }
        else
        {
            Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync.Initialize: _instance 已存在，跳过创建");
        }
        Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] DataSync.Initialize: 执行完成");
    }

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