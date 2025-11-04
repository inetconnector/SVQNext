// This file provides type aliases to migrate away from System.Drawing.
// It makes existing code that uses Bitmap/Color compile against ImageSharp.

global using Bitmap = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;
global using Color = SixLabors.ImageSharp.Color;