using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace ToolModBepInEx;

/// <summary>
/// LateInit 后延迟刷新词条，捕获晚于启动时注册的二创插件词条。
/// </summary>
public class BuffReloadScheduler : MonoBehaviour
{
    public BuffReloadScheduler() : base(ClassInjector.DerivedConstructorPointer<BuffReloadScheduler>())
    {
        ClassInjector.DerivedConstructorBody(this);
    }

    public BuffReloadScheduler(IntPtr i) : base(i) { }

    private System.Collections.IEnumerator Start()
    {
        yield return new WaitForSeconds(5f);
        PatchMgr.ReloadAndSendBuffsData();

        yield return new WaitForSeconds(5f);
        PatchMgr.ReloadAndSendBuffsData();

        yield return new WaitForSeconds(10f);
        PatchMgr.ReloadAndSendBuffsData();

        Destroy(gameObject);
    }
}
