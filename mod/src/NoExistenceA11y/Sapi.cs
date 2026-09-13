using System;
using System.Runtime.InteropServices;
using BepInEx.Logging;

namespace NoExistenceA11y
{
    /// <summary>
    /// Windows 内置语音（SAPI5）后端 —— 不依赖运行时的 COM 互操作。
    ///
    /// 为什么不用 `Type.GetTypeFromProgID("SAPI.SpVoice")` + `InvokeMember`：
    /// 在 Unity 的 Mono 里那条路**根本没实现**。玩家机器上的实测日志：
    ///
    ///     SAPI 不可用: NotImplementedException: The method or operation is not implemented.
    ///
    /// 所以 v0.5.x 的「SAPI 兜底」从来没出过声 —— 只装争渡（或什么读屏都没装）的
    /// 机器上，四级后端全灭，表现就是整局游戏一片安静。
    ///
    /// 现在的做法是纯 P/Invoke：自己 `CoCreateInstance` 拿到 `ISpVoice` 接口指针，
    /// 再从对象的 vtable 里按槽位取出函数地址调用。这条路上没有任何一步需要
    /// Mono 的 COM 支持。
    ///
    /// vtable 槽位（来自 sapi.idl：ISpVoice : ISpEventSource : ISpNotifySource : IUnknown）：
    ///     0  QueryInterface          1  AddRef               2  Release
    ///     3  SetNotifySink           4  SetNotifyWindowMessage
    ///     5  SetNotifyCallbackFunction   6  SetNotifyCallbackInterface
    ///     7  SetNotifyWin32Event     8  WaitForNotifyEvent   9  GetNotifyEventHandle
    ///     10 SetInterest             11 GetEvents            12 GetInfo
    ///     13 SetOutput               14 GetOutputObjectToken 15 GetOutputStream
    ///     16 Pause                   17 Resume               18 SetVoice
    ///     19 GetVoice                20 Speak                21 SpeakStream
    ///     22 GetStatus               23 Skip                 24 SetPriority
    ///     25 GetPriority             26 SetAlertBoundary     27 GetAlertBoundary
    ///     28 SetRate                 29 GetRate              30 SetVolume
    ///     31 GetVolume               32 WaitUntilDone        33 SetSyncSpeakTimeout
    ///     34 GetSyncSpeakTimeout     35 SpeakCompleteEvent   36 IsUISupported
    ///
    /// 这套槽位在真机上验证过：把音量设成 0 再读回来确实是 0，然后用
    /// `Speak` 念一句（音量 0，听不见）返回 S_OK —— 槽位错一个都会当场露馅。
    /// </summary>
    internal static class Sapi
    {
        // ---- ole32 ----
        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext,
                                                   ref Guid riid, out IntPtr ppv);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        private const uint COINIT_APARTMENTTHREADED = 0x2;
        private const uint CLSCTX_ALL = 0x17;

        private static readonly Guid CLSID_SpVoice = new Guid("96749377-3391-11d2-9ee3-00c04f797396");
        private static readonly Guid IID_ISpVoice = new Guid("6c44df74-72b9-4992-a1ec-ef996e0422d4");

        // ---- vtable 槽位 ----
        private const int VT_Release = 2;
        private const int VT_Speak = 20;
        private const int VT_GetRate = 29;
        private const int VT_GetVolume = 31;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint ReleaseFn(IntPtr self);

        /// <summary>pwcs 为 NULL 时表示「只做 flags 里的动作」（例如清空队列）。</summary>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SpeakFn(IntPtr self, IntPtr pwcs, uint flags, IntPtr pulStreamNumber);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetRateFn(IntPtr self, out int rate);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetVolumeFn(IntPtr self, out ushort volume);

        private static IntPtr _voice;
        private static SpeakFn _speak;
        private static ReleaseFn _release;
        private static bool _comInited;

        public static string LastError = "";

        // SPF_ASYNC 必须置位：SAPI 默认同步朗读，会阻塞 Unity 主线程导致游戏卡死
        private const uint SPF_ASYNC = 1;
        private const uint SPF_PURGEBEFORESPEAK = 2;

        /// <summary>创建 SpVoice 并取好要用的函数地址。</summary>
        public static bool TryInit(ManualLogSource log)
        {
            try
            {
                if (_voice != IntPtr.Zero) return true;

                int hr = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
                // S_OK / S_FALSE 都算成功。RPC_E_CHANGED_MODE（线程已经是别的套间模型）
                // 也不影响：SpVoice 是 threading(both)，照样能建。
                _comInited = hr >= 0;

                Guid clsid = CLSID_SpVoice, iid = IID_ISpVoice;
                IntPtr p;
                hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_ALL, ref iid, out p);
                if (hr != 0 || p == IntPtr.Zero)
                {
                    LastError = "SAPI: CoCreateInstance(SpVoice) 返回 0x" + hr.ToString("X8");
                    return false;
                }

                IntPtr vtbl = Marshal.ReadIntPtr(p);
                _speak = (SpeakFn)Marshal.GetDelegateForFunctionPointer(
                    Marshal.ReadIntPtr(vtbl, VT_Speak * IntPtr.Size), typeof(SpeakFn));
                _release = (ReleaseFn)Marshal.GetDelegateForFunctionPointer(
                    Marshal.ReadIntPtr(vtbl, VT_Release * IntPtr.Size), typeof(ReleaseFn));
                var getRate = (GetRateFn)Marshal.GetDelegateForFunctionPointer(
                    Marshal.ReadIntPtr(vtbl, VT_GetRate * IntPtr.Size), typeof(GetRateFn));
                var getVolume = (GetVolumeFn)Marshal.GetDelegateForFunctionPointer(
                    Marshal.ReadIntPtr(vtbl, VT_GetVolume * IntPtr.Size), typeof(GetVolumeFn));

                // 取好函数后先读两个属性：既验证 vtable 槽位没取错
                // （错了会在这一行露馅，而不是等玩家需要朗读时静默失败），
                // 也把结果写进日志，方便对着系统设置核对。
                int rate = 0;
                ushort vol = 0;
                int hrRate = getRate(p, out rate);
                int hrVol = getVolume(p, out vol);

                _voice = p;
                log.LogInfo("语音后端: SAPI（没检测到读屏软件，用系统语音朗读；语速="
                    + (hrRate == 0 ? rate.ToString() : "读取失败 0x" + hrRate.ToString("X8"))
                    + "，音量=" + (hrVol == 0 ? vol.ToString() : "读取失败 0x" + hrVol.ToString("X8"))
                    + "）");
                return true;
            }
            catch (Exception e)
            {
                LastError = "SAPI 不可用: " + e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        /// <summary>朗读一段文本。interrupt=true 时先清空队列。</summary>
        public static void Speak(string text, bool interrupt)
        {
            if (_voice == IntPtr.Zero || _speak == null || string.IsNullOrEmpty(text)) return;

            IntPtr p = IntPtr.Zero;
            try
            {
                p = Marshal.StringToHGlobalUni(text);
                uint flags = SPF_ASYNC | (interrupt ? SPF_PURGEBEFORESPEAK : 0);
                int hr = _speak(_voice, p, flags, IntPtr.Zero);
                if (hr != 0) throw new COMException("ISpVoice::Speak 返回 0x" + hr.ToString("X8"), hr);
            }
            finally
            {
                if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
            }
        }

        /// <summary>停止朗读：pwcs 传 NULL + SPF_PURGEBEFORESPEAK 就是清空队列。</summary>
        public static void Stop()
        {
            if (_voice == IntPtr.Zero || _speak == null) return;
            try { _speak(_voice, IntPtr.Zero, SPF_ASYNC | SPF_PURGEBEFORESPEAK, IntPtr.Zero); }
            catch { }
        }

        public static void Shutdown()
        {
            try
            {
                if (_voice != IntPtr.Zero && _release != null) _release(_voice);
            }
            catch { }
            _voice = IntPtr.Zero;
            _speak = null;
            _release = null;

            try { if (_comInited) CoUninitialize(); } catch { }
            _comInited = false;
        }
    }
}
