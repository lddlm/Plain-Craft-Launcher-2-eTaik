Imports System.Runtime.InteropServices

''' <summary>
''' Ultra：Media Foundation 的最小互操作声明（只包含录制需要的部分）。
''' 全部按 Windows SDK 头文件里的 vtable 顺序声明，顺序错一个就会崩，改的时候不要重排。
''' </summary>
Friend Module UltraMf

    '属性键
    Public ReadOnly KeyMajorType As New Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f")
    Public ReadOnly KeySubType As New Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5")
    Public ReadOnly KeyAvgBitrate As New Guid("20332624-fb0d-4d9e-bd0d-cbf6786c102e")
    Public ReadOnly KeyInterlaceMode As New Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd")
    Public ReadOnly KeyFrameSize As New Guid("1652c33d-d6b2-4012-b834-72030849a37d")
    Public ReadOnly KeyFrameRate As New Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0")
    Public ReadOnly KeyPixelAspect As New Guid("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6")
    Public ReadOnly KeyEnableHardwareTransforms As New Guid("a634a91c-822b-41b9-a494-4de4643612b0")
    Public ReadOnly KeySinkWriterD3DManager As New Guid("ec822da2-e1e9-4b29-a0d8-563c719f5269")
    Public ReadOnly KeyLowLatency As New Guid("9c27891a-ed7a-40e1-88e8-b22727a024ee")

    '音频属性键（录屏同时录系统声音用，来自 mfapi.h）
    Public ReadOnly KeyAudioNumChannels As New Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a")
    Public ReadOnly KeyAudioSamplesPerSecond As New Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba")
    Public ReadOnly KeyAudioAvgBytesPerSecond As New Guid("1aab75c8-cfef-451c-ab95-ac034b8e1731")
    Public ReadOnly KeyAudioBlockAlignment As New Guid("322de230-9eeb-43bd-ab7a-ff412251541d")
    Public ReadOnly KeyAudioBitsPerSample As New Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669")

    '媒体类型
    Public ReadOnly GuidVideo As New Guid("73646976-0000-0010-8000-00AA00389B71")
    Public ReadOnly GuidH264 As New Guid("34363248-0000-0010-8000-00AA00389B71")
    Public ReadOnly GuidNv12 As New Guid("3231564E-0000-0010-8000-00AA00389B71")
    Public ReadOnly GuidHevc As New Guid("43564548-0000-0010-8000-00AA00389B71")
    Public ReadOnly GuidRgb32 As New Guid("00000016-0000-0010-8000-00AA00389B71")
    Public ReadOnly GuidAudio As New Guid("73647561-0000-0010-8000-00AA00389B71")
    Public ReadOnly GuidPcm As New Guid("00000001-0000-0010-8000-00AA00389B71")
    Public ReadOnly GuidAac As New Guid("00001610-0000-0010-8000-00AA00389B71")
    Public ReadOnly IidTexture2D As New Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c")

    Public Const MfVersion As Integer = &H20070

    <DllImport("mfplat.dll", ExactSpelling:=True)>
    Public Function MFStartup(Version As Integer, dwFlags As Integer) As Integer
    End Function

    <DllImport("mfplat.dll", ExactSpelling:=True)>
    Public Function MFShutdown() As Integer
    End Function

    <DllImport("mfplat.dll", ExactSpelling:=True)>
    Public Function MFCreateAttributes(ByRef ppMFAttributes As IMFAttributes, cInitialSize As Integer) As Integer
    End Function

    <DllImport("mfplat.dll", ExactSpelling:=True)>
    Public Function MFCreateMediaType(ByRef ppMFType As IMFMediaType) As Integer
    End Function

    <DllImport("mfplat.dll", ExactSpelling:=True)>
    Public Function MFCreateDXGIDeviceManager(ByRef resetToken As Integer, ByRef ppDeviceManager As IMFDXGIDeviceManager) As Integer
    End Function

    <DllImport("mfplat.dll", ExactSpelling:=True)>
    Public Function MFCreateVideoSampleFromSurface(pUnkSurface As IntPtr, ByRef ppSample As IMFSample) As Integer
    End Function

    <DllImport("mfplat.dll", ExactSpelling:=True)>
    Public Function MFCreateMemoryBuffer(cbMaxLength As Integer, ByRef ppBuffer As IMFMediaBuffer) As Integer
    End Function

    <DllImport("mfplat.dll", ExactSpelling:=True)>
    Public Function MFCreateSample(ByRef ppIMFSample As IMFSample) As Integer
    End Function

    <DllImport("mfplat.dll", ExactSpelling:=True)>
    Public Function MFCreateDXGISurfaceBuffer(ByRef riid As Guid, punkSurface As IntPtr, uSubresourceIndex As Integer,
                                              fBottomUpWhenLinear As Boolean, ByRef ppBuffer As IMFMediaBuffer) As Integer
    End Function

    <DllImport("mfreadwrite.dll", ExactSpelling:=True, CharSet:=CharSet.Unicode)>
    Public Function MFCreateSinkWriterFromURL(pwszOutputURL As String, pByteStream As IntPtr,
                                              pAttributes As IMFAttributes, ByRef ppSinkWriter As IMFSinkWriter) As Integer
    End Function

    '--------------------------------------------------------------- IMFAttributes

    <ComImport, Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Friend Interface IMFAttributes
        <PreserveSig> Function GetItem(ByRef guidKey As Guid, ByRef pValue As IntPtr) As Integer
        <PreserveSig> Function GetItemType(ByRef guidKey As Guid, ByRef pType As Integer) As Integer
        <PreserveSig> Function CompareItem(ByRef guidKey As Guid, pValue As IntPtr, ByRef pbResult As Integer) As Integer
        <PreserveSig> Function Compare(pTheirs As IMFAttributes, matchType As Integer, ByRef pbResult As Integer) As Integer
        <PreserveSig> Function GetUINT32(ByRef guidKey As Guid, ByRef punValue As Integer) As Integer
        <PreserveSig> Function GetUINT64(ByRef guidKey As Guid, ByRef punValue As Long) As Integer
        <PreserveSig> Function GetDouble(ByRef guidKey As Guid, ByRef pfValue As Double) As Integer
        <PreserveSig> Function GetGUID(ByRef guidKey As Guid, ByRef pguidValue As Guid) As Integer
        <PreserveSig> Function GetStringLength(ByRef guidKey As Guid, ByRef pcchLength As Integer) As Integer
        <PreserveSig> Function GetString(ByRef guidKey As Guid, pwszValue As Char(), cchBufSize As Integer, ByRef pcchLength As Integer) As Integer
        <PreserveSig> Function GetAllocatedString(ByRef guidKey As Guid, ByRef ppwszValue As IntPtr, ByRef pcchLength As Integer) As Integer
        <PreserveSig> Function GetBlobSize(ByRef guidKey As Guid, ByRef pcbBlobSize As Integer) As Integer
        <PreserveSig> Function GetBlob(ByRef guidKey As Guid, pBuf As Byte(), cbBufSize As Integer, ByRef pcbBlobSize As Integer) As Integer
        <PreserveSig> Function GetAllocatedBlob(ByRef guidKey As Guid, ByRef ppBuf As IntPtr, ByRef pcbSize As Integer) As Integer
        <PreserveSig> Function GetUnknown(ByRef guidKey As Guid, ByRef riid As Guid, ByRef ppv As IntPtr) As Integer
        <PreserveSig> Function SetItem(ByRef guidKey As Guid, pValue As IntPtr) As Integer
        <PreserveSig> Function DeleteItem(ByRef guidKey As Guid) As Integer
        <PreserveSig> Function DeleteAllItems() As Integer
        <PreserveSig> Function SetUINT32(ByRef guidKey As Guid, unValue As Integer) As Integer
        <PreserveSig> Function SetUINT64(ByRef guidKey As Guid, unValue As Long) As Integer
        <PreserveSig> Function SetDouble(ByRef guidKey As Guid, fValue As Double) As Integer
        <PreserveSig> Function SetGUID(ByRef guidKey As Guid, ByRef guidValue As Guid) As Integer
        <PreserveSig> Function SetString(ByRef guidKey As Guid, wszValue As String) As Integer
        <PreserveSig> Function SetBlob(ByRef guidKey As Guid, pBuf As Byte(), cbBufSize As Integer) As Integer
        <PreserveSig> Function SetUnknown(ByRef guidKey As Guid, pUnknown As Object) As Integer
        <PreserveSig> Function LockStore() As Integer
        <PreserveSig> Function UnlockStore() As Integer
        <PreserveSig> Function GetCount(ByRef pcItems As Integer) As Integer
        <PreserveSig> Function GetItemByIndex(index As Integer, ByRef pguidKey As Guid, ByRef pValue As IntPtr) As Integer
        <PreserveSig> Function CopyAllItems(pDest As IMFAttributes) As Integer
    End Interface

    '--------------------------------------------------------------- IMFMediaType

    <ComImport, Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Friend Interface IMFMediaType
        'IMFAttributes 的 30 个方法，顺序不能动
        <PreserveSig> Function GetItem(ByRef guidKey As Guid, ByRef pValue As IntPtr) As Integer
        <PreserveSig> Function GetItemType(ByRef guidKey As Guid, ByRef pType As Integer) As Integer
        <PreserveSig> Function CompareItem(ByRef guidKey As Guid, pValue As IntPtr, ByRef pbResult As Integer) As Integer
        <PreserveSig> Function Compare(pTheirs As IMFAttributes, matchType As Integer, ByRef pbResult As Integer) As Integer
        <PreserveSig> Function GetUINT32(ByRef guidKey As Guid, ByRef punValue As Integer) As Integer
        <PreserveSig> Function GetUINT64(ByRef guidKey As Guid, ByRef punValue As Long) As Integer
        <PreserveSig> Function GetDouble(ByRef guidKey As Guid, ByRef pfValue As Double) As Integer
        <PreserveSig> Function GetGUID(ByRef guidKey As Guid, ByRef pguidValue As Guid) As Integer
        <PreserveSig> Function GetStringLength(ByRef guidKey As Guid, ByRef pcchLength As Integer) As Integer
        <PreserveSig> Function GetString(ByRef guidKey As Guid, pwszValue As Char(), cchBufSize As Integer, ByRef pcchLength As Integer) As Integer
        <PreserveSig> Function GetAllocatedString(ByRef guidKey As Guid, ByRef ppwszValue As IntPtr, ByRef pcchLength As Integer) As Integer
        <PreserveSig> Function GetBlobSize(ByRef guidKey As Guid, ByRef pcbBlobSize As Integer) As Integer
        <PreserveSig> Function GetBlob(ByRef guidKey As Guid, pBuf As Byte(), cbBufSize As Integer, ByRef pcbBlobSize As Integer) As Integer
        <PreserveSig> Function GetAllocatedBlob(ByRef guidKey As Guid, ByRef ppBuf As IntPtr, ByRef pcbSize As Integer) As Integer
        <PreserveSig> Function GetUnknown(ByRef guidKey As Guid, ByRef riid As Guid, ByRef ppv As IntPtr) As Integer
        <PreserveSig> Function SetItem(ByRef guidKey As Guid, pValue As IntPtr) As Integer
        <PreserveSig> Function DeleteItem(ByRef guidKey As Guid) As Integer
        <PreserveSig> Function DeleteAllItems() As Integer
        <PreserveSig> Function SetUINT32(ByRef guidKey As Guid, unValue As Integer) As Integer
        <PreserveSig> Function SetUINT64(ByRef guidKey As Guid, unValue As Long) As Integer
        <PreserveSig> Function SetDouble(ByRef guidKey As Guid, fValue As Double) As Integer
        <PreserveSig> Function SetGUID(ByRef guidKey As Guid, ByRef guidValue As Guid) As Integer
        <PreserveSig> Function SetString(ByRef guidKey As Guid, wszValue As String) As Integer
        <PreserveSig> Function SetBlob(ByRef guidKey As Guid, pBuf As Byte(), cbBufSize As Integer) As Integer
        <PreserveSig> Function SetUnknown(ByRef guidKey As Guid, pUnknown As Object) As Integer
        <PreserveSig> Function LockStore() As Integer
        <PreserveSig> Function UnlockStore() As Integer
        <PreserveSig> Function GetCount(ByRef pcItems As Integer) As Integer
        <PreserveSig> Function GetItemByIndex(index As Integer, ByRef pguidKey As Guid, ByRef pValue As IntPtr) As Integer
        <PreserveSig> Function CopyAllItems(pDest As IMFAttributes) As Integer
        'IMFMediaType 自己的 5 个方法
        <PreserveSig> Function GetMajorType(ByRef pguidMajorType As Guid) As Integer
        <PreserveSig> Function IsCompressedFormat(ByRef pfCompressed As Integer) As Integer
        <PreserveSig> Function IsEqual(pIMediaType As IMFMediaType, ByRef pdwFlags As Integer) As Integer
        <PreserveSig> Function GetRepresentation(guidRepresentation As Guid, ByRef ppvRepresentation As IntPtr) As Integer
        <PreserveSig> Function FreeRepresentation(guidRepresentation As Guid, pvRepresentation As IntPtr) As Integer
    End Interface

    '--------------------------------------------------------------- IMFSample

    <ComImport, Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Friend Interface IMFSample
        'IMFAttributes 的 30 个方法，顺序不能动
        <PreserveSig> Function GetItem(ByRef guidKey As Guid, ByRef pValue As IntPtr) As Integer
        <PreserveSig> Function GetItemType(ByRef guidKey As Guid, ByRef pType As Integer) As Integer
        <PreserveSig> Function CompareItem(ByRef guidKey As Guid, pValue As IntPtr, ByRef pbResult As Integer) As Integer
        <PreserveSig> Function Compare(pTheirs As IMFAttributes, matchType As Integer, ByRef pbResult As Integer) As Integer
        <PreserveSig> Function GetUINT32(ByRef guidKey As Guid, ByRef punValue As Integer) As Integer
        <PreserveSig> Function GetUINT64(ByRef guidKey As Guid, ByRef punValue As Long) As Integer
        <PreserveSig> Function GetDouble(ByRef guidKey As Guid, ByRef pfValue As Double) As Integer
        <PreserveSig> Function GetGUID(ByRef guidKey As Guid, ByRef pguidValue As Guid) As Integer
        <PreserveSig> Function GetStringLength(ByRef guidKey As Guid, ByRef pcchLength As Integer) As Integer
        <PreserveSig> Function GetString(ByRef guidKey As Guid, pwszValue As Char(), cchBufSize As Integer, ByRef pcchLength As Integer) As Integer
        <PreserveSig> Function GetAllocatedString(ByRef guidKey As Guid, ByRef ppwszValue As IntPtr, ByRef pcchLength As Integer) As Integer
        <PreserveSig> Function GetBlobSize(ByRef guidKey As Guid, ByRef pcbBlobSize As Integer) As Integer
        <PreserveSig> Function GetBlob(ByRef guidKey As Guid, pBuf As Byte(), cbBufSize As Integer, ByRef pcbBlobSize As Integer) As Integer
        <PreserveSig> Function GetAllocatedBlob(ByRef guidKey As Guid, ByRef ppBuf As IntPtr, ByRef pcbSize As Integer) As Integer
        <PreserveSig> Function GetUnknown(ByRef guidKey As Guid, ByRef riid As Guid, ByRef ppv As IntPtr) As Integer
        <PreserveSig> Function SetItem(ByRef guidKey As Guid, pValue As IntPtr) As Integer
        <PreserveSig> Function DeleteItem(ByRef guidKey As Guid) As Integer
        <PreserveSig> Function DeleteAllItems() As Integer
        <PreserveSig> Function SetUINT32(ByRef guidKey As Guid, unValue As Integer) As Integer
        <PreserveSig> Function SetUINT64(ByRef guidKey As Guid, unValue As Long) As Integer
        <PreserveSig> Function SetDouble(ByRef guidKey As Guid, fValue As Double) As Integer
        <PreserveSig> Function SetGUID(ByRef guidKey As Guid, ByRef guidValue As Guid) As Integer
        <PreserveSig> Function SetString(ByRef guidKey As Guid, wszValue As String) As Integer
        <PreserveSig> Function SetBlob(ByRef guidKey As Guid, pBuf As Byte(), cbBufSize As Integer) As Integer
        <PreserveSig> Function SetUnknown(ByRef guidKey As Guid, pUnknown As Object) As Integer
        <PreserveSig> Function LockStore() As Integer
        <PreserveSig> Function UnlockStore() As Integer
        <PreserveSig> Function GetCount(ByRef pcItems As Integer) As Integer
        <PreserveSig> Function GetItemByIndex(index As Integer, ByRef pguidKey As Guid, ByRef pValue As IntPtr) As Integer
        <PreserveSig> Function CopyAllItems(pDest As IMFAttributes) As Integer
        'IMFSample 自己的 14 个方法
        <PreserveSig> Function GetSampleFlags(ByRef pdwSampleFlags As Integer) As Integer
        <PreserveSig> Function SetSampleFlags(dwSampleFlags As Integer) As Integer
        <PreserveSig> Function GetSampleTime(ByRef phnsSampleTime As Long) As Integer
        <PreserveSig> Function SetSampleTime(hnsSampleTime As Long) As Integer
        <PreserveSig> Function GetSampleDuration(ByRef phnsSampleDuration As Long) As Integer
        <PreserveSig> Function SetSampleDuration(hnsSampleDuration As Long) As Integer
        <PreserveSig> Function GetBufferCount(ByRef pdwBufferCount As Integer) As Integer
        <PreserveSig> Function GetBufferByIndex(dwIndex As Integer, ByRef ppBuffer As IMFMediaBuffer) As Integer
        <PreserveSig> Function ConvertToContiguousBuffer(ByRef ppBuffer As IMFMediaBuffer) As Integer
        <PreserveSig> Function AddBuffer(pBuffer As IMFMediaBuffer) As Integer
        <PreserveSig> Function RemoveBufferByIndex(dwIndex As Integer) As Integer
        <PreserveSig> Function RemoveAllBuffers() As Integer
        <PreserveSig> Function GetTotalLength(ByRef pcbTotalLength As Integer) As Integer
        <PreserveSig> Function CopyToBuffer(pBuffer As IMFMediaBuffer) As Integer
    End Interface

    '--------------------------------------------------------------- IMFMediaBuffer

    <ComImport, Guid("045FA593-8799-42B8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Friend Interface IMFMediaBuffer
        <PreserveSig> Function Lock(ByRef ppbBuffer As IntPtr, ByRef pcbMaxLength As Integer, ByRef pcbCurrentLength As Integer) As Integer
        <PreserveSig> Function Unlock() As Integer
        <PreserveSig> Function GetCurrentLength(ByRef pcbCurrentLength As Integer) As Integer
        <PreserveSig> Function SetCurrentLength(pcbCurrentLength As Integer) As Integer
        <PreserveSig> Function GetMaxLength(ByRef pcbMaxLength As Integer) As Integer
    End Interface

    '--------------------------------------------------------------- IMFSinkWriter

    <ComImport, Guid("3137F1CD-FE5E-4805-A5D8-FB477448CB3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Friend Interface IMFSinkWriter
        <PreserveSig> Function AddStream(pTargetMediaType As IMFMediaType, ByRef pdwStreamIndex As Integer) As Integer
        <PreserveSig> Function SetInputMediaType(dwStreamIndex As Integer, pInputMediaType As IMFMediaType,
                                                 pEncodingParameters As IMFAttributes) As Integer
        <PreserveSig> Function BeginWriting() As Integer
        <PreserveSig> Function WriteSample(dwStreamIndex As Integer, pSample As IMFSample) As Integer
        <PreserveSig> Function SendStreamTick(dwStreamIndex As Integer, llTimestamp As Long) As Integer
        <PreserveSig> Function PlaceMarker(dwStreamIndex As Integer, pvContext As IntPtr) As Integer
        <PreserveSig> Function NotifyEndOfSegment(dwStreamIndex As Integer) As Integer
        <PreserveSig> Function Flush(dwStreamIndex As Integer) As Integer
        <PreserveSig> Function FinalizeWriter() As Integer
        <PreserveSig> Function GetServiceForStream(dwStreamIndex As Integer, ByRef guidService As Guid,
                                                   ByRef riid As Guid, ByRef ppvObject As IntPtr) As Integer
        <PreserveSig> Function GetStatistics(dwStreamIndex As Integer, pStats As IntPtr) As Integer
    End Interface

    '--------------------------------------------------------------- IMFDXGIDeviceManager

    <ComImport, Guid("EB533D5D-2DB6-40F8-97A9-494692014F07"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Friend Interface IMFDXGIDeviceManager
        <PreserveSig> Function ResetDevice(pUnkDevice As IntPtr, resetToken As Integer) As Integer
        <PreserveSig> Function OpenDeviceHandle(ByRef phDevice As IntPtr) As Integer
        <PreserveSig> Function CloseDeviceHandle(hDevice As IntPtr) As Integer
        <PreserveSig> Function GetVideoService(hDevice As IntPtr, ByRef riid As Guid, ByRef ppService As IntPtr) As Integer
        <PreserveSig> Function LockDevice(hDevice As IntPtr, ByRef ppUnkDevice As IntPtr, fBlock As Integer) As Integer
        <PreserveSig> Function UnlockDevice(hDevice As IntPtr, fSaveState As Integer) As Integer
        <PreserveSig> Function TestDevice(hDevice As IntPtr) As Integer
    End Interface

End Module
