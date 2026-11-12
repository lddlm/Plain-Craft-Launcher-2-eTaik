Imports System.Runtime.InteropServices

''' <summary>
''' Ultra：WASAPI 回环采集所需的最小 COM 声明。
''' 只用于「录屏时同时录系统声音」：抓默认播放设备的混音输出，不碰麦克风。
''' vtable 顺序按 Windows SDK 的 mmdeviceapi.h / audioclient.h，改的时候不要重排。
''' 全部接口与调用顺序已用 pcl-build/_probe_wasapi.py 在本机逐项验证通过。
''' </summary>
Friend Module UltraWasapi

    Public ReadOnly ClsidDeviceEnumerator As New Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")
    Public ReadOnly IidDeviceEnumerator As New Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")
    Public ReadOnly IidAudioClient As New Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")
    Public ReadOnly IidAudioCaptureClient As New Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")

    Public ReadOnly SubtypeFloat As New Guid("00000003-0000-0010-8000-00AA00389B71")
    Public ReadOnly SubtypePcm As New Guid("00000001-0000-0010-8000-00AA00389B71")

    Public Const ClsCtxAll As Integer = 23
    Public Const RenderEndpoint As Integer = 0       'eRender
    Public Const ConsoleRole As Integer = 0          'eConsole
    Public Const ShareModeShared As Integer = 0
    Public Const StreamFlagsLoopback As Integer = &H20000
    Public Const BufferSilent As Integer = 2         'AUDCLNT_BUFFERFLAGS_SILENT

    Public Const WaveFormatPcm As UShort = 1US
    Public Const WaveFormatIeeeFloat As UShort = 3US
    Public Const WaveFormatExtensible As UShort = 65534US

    <StructLayout(LayoutKind.Sequential, Pack:=2)>
    Friend Structure WaveFormatEx
        Public FormatTag As UShort
        Public Channels As UShort
        Public SamplesPerSec As UInteger
        Public AvgBytesPerSec As UInteger
        Public BlockAlign As UShort
        Public BitsPerSample As UShort
        Public ExtraSize As UShort
    End Structure

    <DllImport("ole32.dll", ExactSpelling:=True)>
    Friend Function CoInitializeEx(pvReserved As IntPtr, dwCoInit As Integer) As Integer
    End Function

    <DllImport("ole32.dll", ExactSpelling:=True)>
    Friend Function CoCreateInstance(ByRef rclsid As Guid, pUnkOuter As IntPtr, dwClsContext As Integer,
                                     ByRef riid As Guid,
                                     <MarshalAs(UnmanagedType.Interface)> ByRef ppv As IMMDeviceEnumerator) As Integer
    End Function

    <DllImport("ole32.dll", ExactSpelling:=True)>
    Friend Sub CoTaskMemFree(pv As IntPtr)
    End Sub

    '--------------------------------------------------------------- IMMDeviceEnumerator

    <ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Friend Interface IMMDeviceEnumerator
        <PreserveSig> Function EnumAudioEndpoints(dataFlow As Integer, stateMask As Integer,
            <MarshalAs(UnmanagedType.IUnknown)> ByRef devices As Object) As Integer
        <PreserveSig> Function GetDefaultAudioEndpoint(dataFlow As Integer, role As Integer,
            <MarshalAs(UnmanagedType.Interface)> ByRef endpoint As IMMDevice) As Integer
        <PreserveSig> Function GetDevice(<MarshalAs(UnmanagedType.LPWStr)> id As String,
            <MarshalAs(UnmanagedType.IUnknown)> ByRef device As Object) As Integer
        <PreserveSig> Function RegisterEndpointNotificationCallback(client As IntPtr) As Integer
        <PreserveSig> Function UnregisterEndpointNotificationCallback(client As IntPtr) As Integer
    End Interface

    '--------------------------------------------------------------- IMMDevice

    <ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Friend Interface IMMDevice
        <PreserveSig> Function Activate(ByRef iid As Guid, clsCtx As Integer, activationParams As IntPtr,
            <MarshalAs(UnmanagedType.Interface)> ByRef ppInterface As IAudioClient) As Integer
        <PreserveSig> Function OpenPropertyStore(stgmAccess As Integer,
            <MarshalAs(UnmanagedType.IUnknown)> ByRef properties As Object) As Integer
        <PreserveSig> Function GetId(ByRef id As IntPtr) As Integer
        <PreserveSig> Function GetState(ByRef state As Integer) As Integer
    End Interface

    '--------------------------------------------------------------- IAudioClient

    <ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Friend Interface IAudioClient
        <PreserveSig> Function Initialize(shareMode As Integer, streamFlags As Integer,
                                          hnsBufferDuration As Long, hnsPeriodicity As Long,
                                          pFormat As IntPtr, ByRef sessionGuid As Guid) As Integer
        <PreserveSig> Function GetBufferSize(ByRef numBufferFrames As Integer) As Integer
        <PreserveSig> Function GetStreamLatency(ByRef latency As Long) As Integer
        <PreserveSig> Function GetCurrentPadding(ByRef numPaddingFrames As Integer) As Integer
        <PreserveSig> Function IsFormatSupported(shareMode As Integer, pFormat As IntPtr,
                                                  ByRef closestMatch As IntPtr) As Integer
        <PreserveSig> Function GetMixFormat(ByRef deviceFormat As IntPtr) As Integer
        <PreserveSig> Function GetDevicePeriod(ByRef defaultPeriod As Long, ByRef minimumPeriod As Long) As Integer
        <PreserveSig> Function Start() As Integer
        <PreserveSig> Function StopStream() As Integer   'SDK 里叫 Stop，VB 关键字，改名不影响 vtable 顺序
        <PreserveSig> Function Reset() As Integer
        <PreserveSig> Function SetEventHandle(eventHandle As IntPtr) As Integer
        <PreserveSig> Function GetService(ByRef riid As Guid,
            <MarshalAs(UnmanagedType.Interface)> ByRef ppv As IAudioCaptureClient) As Integer
    End Interface

    '--------------------------------------------------------------- IAudioCaptureClient

    <ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Friend Interface IAudioCaptureClient
        <PreserveSig> Function GetBuffer(ByRef data As IntPtr, ByRef framesToRead As Integer,
                                         ByRef flags As Integer, ByRef devicePosition As Long,
                                         ByRef qpcPosition As Long) As Integer
        <PreserveSig> Function ReleaseBuffer(framesRead As Integer) As Integer
        <PreserveSig> Function GetNextPacketSize(ByRef framesInNextPacket As Integer) As Integer
    End Interface

End Module
