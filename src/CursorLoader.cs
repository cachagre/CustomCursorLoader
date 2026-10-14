using Godot;

namespace CustomCursorLoader;

internal static class CursorLoader
{
    public static (Image Image, Vector2 Hotspot) Load(ConfigStore store, CursorConfig config, int? longestSide = null)
    {
        CursorAnimation animation = CursorAnimation.Load(store, config, longestSide, firstOnly: true);
        return (animation.Frames[0], animation.Hotspot);
    }

    internal static void Process(Image image, CursorGeometry geometry, double opacity)
    {
        int width = image.GetWidth(), height = image.GetHeight();
        if (geometry.Width != width || geometry.Height != height)
            image.Resize(geometry.Width, geometry.Height, Image.Interpolation.Lanczos);
        if (opacity < 1)
            for (int y = 0; y < geometry.Height; y++)
                for (int x = 0; x < geometry.Width; x++)
                {
                    Color pixel = image.GetPixel(x, y);
                    pixel.A *= (float)opacity;
                    image.SetPixel(x, y, pixel);
                }
    }
}
