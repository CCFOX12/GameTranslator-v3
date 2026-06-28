using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System.Drawing;
using System.Drawing.Imaging;
using Device = SharpDX.Direct3D11.Device;

namespace GameTranslator.Services;

public class CaptureService : IDisposable
{
    private Factory1? _factory;
    private Adapter1? _adapter;
    private Device? _device;
    private Output1? _output;
    private OutputDuplication? _duplication;
    private Texture2D? _stagingTexture;
    private readonly int _outputIndex;
    private bool _disposed;
    private int _frameCount;

    public CaptureService(int outputIndex = 0) { _outputIndex = outputIndex; }

    public void Initialize()
    {
        _factory = new Factory1();
        _adapter = _factory.GetAdapter1(0);
        _device = new Device(_adapter);
        var output = _adapter.GetOutput(_outputIndex);
        _output = output.QueryInterface<Output1>();
        var desc = _output.Description;
        Logger.Info("DXGI 捕获已初始化: 显示器#{0}, 分辨率={1}x{2}",
            _outputIndex, desc.DesktopBounds.Right - desc.DesktopBounds.Left,
            desc.DesktopBounds.Bottom - desc.DesktopBounds.Top);
        _duplication = _output.DuplicateOutput(_device);
    }

    public Bitmap? CaptureFrame()
    {
        if (_duplication == null || _device == null) return null;
        try
        {
            var result = _duplication.TryAcquireNextFrame(100, out _, out var desktopResource);
            if (result.Failure || desktopResource == null) return null;
            try
            {
                using var screenTexture = desktopResource.QueryInterface<Texture2D>();
                var desc = screenTexture.Description;
                if (_stagingTexture == null || _stagingTexture.Description.Width != desc.Width || _stagingTexture.Description.Height != desc.Height)
                {
                    _stagingTexture?.Dispose();
                    _stagingTexture = new Texture2D(_device, new Texture2DDescription
                    {
                        Width = desc.Width, Height = desc.Height, MipLevels = 1, ArraySize = 1,
                        Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Staging, BindFlags = BindFlags.None,
                        CpuAccessFlags = CpuAccessFlags.Read, OptionFlags = ResourceOptionFlags.None
                    });
                }
                _device.ImmediateContext.CopyResource(screenTexture, _stagingTexture);
                var dataBox = _device.ImmediateContext.MapSubresource(_stagingTexture, 0, MapMode.Read, MapFlags.None);
                var bitmap = new Bitmap(desc.Width, desc.Height, PixelFormat.Format32bppArgb);
                var rect = new System.Drawing.Rectangle(0, 0, desc.Width, desc.Height);
                var bmpData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                var srcPtr = dataBox.DataPointer;
                var dstPtr = bmpData.Scan0;
                var rowSize = desc.Width * 4;
                for (int y = 0; y < desc.Height; y++)
                {
                    Utilities.CopyMemory(dstPtr, srcPtr, rowSize);
                    srcPtr = IntPtr.Add(srcPtr, dataBox.RowPitch);
                    dstPtr = IntPtr.Add(dstPtr, bmpData.Stride);
                }
                bitmap.UnlockBits(bmpData);
                _device.ImmediateContext.UnmapSubresource(_stagingTexture, 0);
                _frameCount++;
                if (_frameCount == 1) Logger.Info("首帧捕获成功: {0}x{1}", desc.Width, desc.Height);
                return bitmap;
            }
            finally { _duplication.ReleaseFrame(); }
        }
        catch (SharpDXException ex) when (ex.ResultCode == SharpDX.DXGI.ResultCode.WaitTimeout) { return null; }
        catch (SharpDXException ex) when (ex.ResultCode == SharpDX.DXGI.ResultCode.AccessLost)
        {
            Logger.Warn("DXGI AccessLost, 重新初始化...");
            CleanupDuplication();
            try { Initialize(); } catch { }
            return null;
        }
    }

    private void CleanupDuplication()
    {
        _stagingTexture?.Dispose(); _stagingTexture = null;
        _duplication?.Dispose(); _duplication = null;
        _output?.Dispose(); _output = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CleanupDuplication();
        _device?.Dispose();
        _adapter?.Dispose();
        _factory?.Dispose();
    }
}
