using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.PropertySystem;
using System.Drawing;

public sealed class RemoveObjectEffect : PropertyBasedEffect
{
    public RemoveObjectEffect()
        : base(
            "Remove Object (LaMa)",   // name
            (PaintDotNet.Imaging.IBitmapSource?)null, // icon (có thể thêm sau)
            "AI Effects",            // submenu
            new EffectOptions
            {
                Flags = EffectFlags.Configurable
            })
    {
    }

    protected override PropertyCollection OnCreatePropertyCollection()
    { // hàm này được gọi để tạo ra một collection các thuộc tính mà người dùng có thể điều chỉnh khi sử dụng hiệu ứng
      // trả về collection rỗng vì không có thuộc tính nào để cấu hình
        return new PropertyCollection( new Property[] {} );
    }

    private Surface dstSurface;
    private Surface srcSurface;
    protected override void OnSetRenderInfo(
    PropertyBasedEffectConfigToken token,
    RenderArgs dstArgs,
    RenderArgs srcArgs)
    {
        // hàm này được gọi trước khi hiệu ứng bắt đầu render, nó nhận vào token chứa thông tin cấu hình
        // hai đối tượng RenderArgs đại diện cho ảnh đích và ảnh nguồn
        this.dstSurface = dstArgs.Surface;
        this.srcSurface = srcArgs.Surface;

        base.OnSetRenderInfo(token, dstArgs, srcArgs);
    }

    protected override void OnRender(Rectangle[] rois, int startIndex, int length)
    { // hàm này được gọi để thực hiện việc render hiệu ứng lên ảnh
      // nó nhận vào một mảng các vùng cần render (rois), chỉ số bắt đầu, độ dài của mảng

        var lama = new LaMaInpaintProject.LaMaEffect();

        Bitmap srcBitmap = srcSurface.CreateAliasedBitmap();
        Bitmap dstBitmap = dstSurface.CreateAliasedBitmap();

        for (int i = startIndex; i < startIndex + length; i++)
        {
            Rectangle rect = rois[i];

            Bitmap src = srcBitmap;
            Bitmap mask = new Bitmap(src.Width, src.Height); // TODO: lấy mask từ selection

            Bitmap result = lama.RunInference(src, mask);

            using (Graphics g = Graphics.FromImage(dstBitmap))
            {
                g.DrawImage(result, rect);
            }
        }

    }
}