using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace MinecraftLauncher.Helpers
{
    /// <summary>
    /// Кроссплатформенный программный 3D-рендер модели игрока из PNG-скина (64x64 / 64x32 / HD)
    /// в Avalonia WriteableBitmap с поддержкой вращения и slim-рук.
    /// </summary>
    public static class SkinModel3DBuilder
    {
        public sealed class SkinRaster
        {
            public byte[] Data = Array.Empty<byte>();
            public int Width;
            public int Height;
            public int Stride;
            public int Scale;

            public bool IsOpaque(int x64, int y64)
            {
                int x = Math.Min(Width - 1, x64 * Scale);
                int y = Math.Min(Height - 1, y64 * Scale);
                if (x < 0 || y < 0) return false;
                return Data[y * Stride + x * 4 + 3] > 0;
            }

            public (byte b, byte g, byte r, byte a) SampleUv(double u, double v)
            {
                int px = Math.Clamp((int)(u * Width), 0, Width - 1);
                int py = Math.Clamp((int)(v * Height), 0, Height - 1);
                int idx = py * Stride + px * 4;
                return (Data[idx], Data[idx + 1], Data[idx + 2], Data[idx + 3]);
            }

            public bool HasOverlays => Height > 32 * Scale;
        }

        public static SkinRaster Rasterize(Bitmap skin)
        {
            int w = skin.PixelSize.Width;
            int h = skin.PixelSize.Height;
            int stride = w * 4;
            var data = new byte[stride * h];

            unsafe
            {
                fixed (byte* ptr = data)
                {
                    skin.CopyPixels(new PixelRect(0, 0, w, h), (IntPtr)ptr, data.Length, stride);
                }
            }

            return new SkinRaster
            {
                Data = data,
                Width = w,
                Height = h,
                Stride = stride,
                Scale = Math.Max(1, w / 64)
            };
        }

        public static bool DetectSlim(SkinRaster raster)
        {
            if (!raster.HasOverlays) return false;
            return !raster.IsOpaque(54, 20) && !raster.IsOpaque(55, 20);
        }

        private enum Part { Head, Body, RightLeg, LeftLeg, RightArm, LeftArm }
        private enum Face { Front, Back, SideRight, SideLeft, Top, Bottom }

        private struct Quad3D
        {
            public double X0, Y0, Z0, U0, V0;
            public double X1, Y1, Z1, U1, V1;
            public double X2, Y2, Z2, U2, V2;
            public double X3, Y3, Z3, U3, V3;
            public double AvgZ;
            public double Shade;
            public bool IsOverlay;
        }

        public static WriteableBitmap RenderToBitmap(
            SkinRaster skin, bool slim, double yawDeg, double pitchDeg, int outWidth = 280, int outHeight = 360)
        {
            var wb = new WriteableBitmap(
                new PixelSize(outWidth, outHeight),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);

            var quads = BuildQuads(skin, slim);

            double yaw = yawDeg * Math.PI / 180.0;
            double pitch = pitchDeg * Math.PI / 180.0;
            double cy = Math.Cos(yaw), sy = Math.Sin(yaw);
            double cp = Math.Cos(pitch), sp = Math.Sin(pitch);

            for (int i = 0; i < quads.Count; i++)
            {
                var q = quads[i];
                (q.X0, q.Y0, q.Z0) = Rotate(q.X0, q.Y0, q.Z0, cy, sy, cp, sp);
                (q.X1, q.Y1, q.Z1) = Rotate(q.X1, q.Y1, q.Z1, cy, sy, cp, sp);
                (q.X2, q.Y2, q.Z2) = Rotate(q.X2, q.Y2, q.Z2, cy, sy, cp, sp);
                (q.X3, q.Y3, q.Z3) = Rotate(q.X3, q.Y3, q.Z3, cy, sy, cp, sp);
                q.AvgZ = (q.Z0 + q.Z1 + q.Z2 + q.Z3) * 0.25;
                quads[i] = q;
            }

            // Painter's algorithm: back-to-front (larger Z is farther from camera at -Z)
            quads.Sort((a, b) =>
            {
                int cmp = b.AvgZ.CompareTo(a.AvgZ);
                if (cmp != 0) return cmp;
                return a.IsOverlay.CompareTo(b.IsOverlay);
            });

            byte[] fb = new byte[outWidth * outHeight * 4];
            double scale = Math.Min(outWidth, outHeight) / 40.0;
            double camDist = 64.0;

            foreach (var q in quads)
            {
                // Backface culling for base layer (keep overlays double-sided)
                (double sx0, double sy0) = Project(q.X0, q.Y0, q.Z0, camDist, scale, outWidth, outHeight);
                (double sx1, double sy1) = Project(q.X1, q.Y1, q.Z1, camDist, scale, outWidth, outHeight);
                (double sx2, double sy2) = Project(q.X2, q.Y2, q.Z2, camDist, scale, outWidth, outHeight);
                (double sx3, double sy3) = Project(q.X3, q.Y3, q.Z3, camDist, scale, outWidth, outHeight);

                double cross = (sx1 - sx0) * (sy2 - sy0) - (sy1 - sy0) * (sx2 - sx0);
                if (!q.IsOverlay && cross < 0) continue;

                RasterizeTriangle(fb, outWidth, outHeight, skin, q.Shade,
                    sx0, sy0, q.U0, q.V0,
                    sx1, sy1, q.U1, q.V1,
                    sx2, sy2, q.U2, q.V2);
                RasterizeTriangle(fb, outWidth, outHeight, skin, q.Shade,
                    sx0, sy0, q.U0, q.V0,
                    sx2, sy2, q.U2, q.V2,
                    sx3, sy3, q.U3, q.V3);
            }

            using var locked = wb.Lock();
            unsafe
            {
                fixed (byte* src = fb)
                {
                    Buffer.MemoryCopy(src, (void*)locked.Address, fb.Length, fb.Length);
                }
            }

            return wb;
        }

        private static (double x, double y, double z) Rotate(
            double x, double y, double z, double cy, double sy, double cp, double sp)
        {
            double x1 = x * cy + z * sy;
            double z1 = -x * sy + z * cy;
            double y2 = y * cp - z1 * sp;
            double z2 = y * sp + z1 * cp;
            return (x1, y2, z2);
        }

        private static (double sx, double sy) Project(
            double x, double y, double z, double camDist, double scale, int w, int h)
        {
            double f = camDist / (camDist + z);
            return (w * 0.5 + x * scale * f, h * 0.5 - y * scale * f);
        }

        private static void RasterizeTriangle(
            byte[] fb, int w, int h, SkinRaster skin, double shade,
            double x0, double y0, double u0, double v0,
            double x1, double y1, double u1, double v1,
            double x2, double y2, double u2, double v2)
        {
            int minX = Math.Max(0, (int)Math.Floor(Math.Min(x0, Math.Min(x1, x2))));
            int maxX = Math.Min(w - 1, (int)Math.Ceiling(Math.Max(x0, Math.Max(x1, x2))));
            int minY = Math.Max(0, (int)Math.Floor(Math.Min(y0, Math.Min(y1, y2))));
            int maxY = Math.Min(h - 1, (int)Math.Ceiling(Math.Max(y0, Math.Max(y1, y2))));

            double area = Edge(x0, y0, x1, y1, x2, y2);
            if (Math.Abs(area) < 1e-5) return;

            for (int py = minY; py <= maxY; py++)
            {
                for (int px = minX; px <= maxX; px++)
                {
                    double cx = px + 0.5;
                    double cy = py + 0.5;
                    double w0 = Edge(x1, y1, x2, y2, cx, cy) / area;
                    double w1 = Edge(x2, y2, x0, y0, cx, cy) / area;
                    double w2 = Edge(x0, y0, x1, y1, cx, cy) / area;

                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;

                    double u = w0 * u0 + w1 * u1 + w2 * u2;
                    double v = w0 * v0 + w1 * v1 + w2 * v2;
                    var (b, g, r, a) = skin.SampleUv(u, v);
                    if (a < 10) continue;

                    byte sb = (byte)Math.Clamp(b * shade, 0, 255);
                    byte sg = (byte)Math.Clamp(g * shade, 0, 255);
                    byte sr = (byte)Math.Clamp(r * shade, 0, 255);

                    int idx = (py * w + px) * 4;
                    if (a >= 250)
                    {
                        fb[idx] = sb;
                        fb[idx + 1] = sg;
                        fb[idx + 2] = sr;
                        fb[idx + 3] = 255;
                    }
                    else
                    {
                        double alpha = a / 255.0;
                        double inv = 1.0 - alpha;
                        fb[idx] = (byte)(sb * alpha + fb[idx] * inv);
                        fb[idx + 1] = (byte)(sg * alpha + fb[idx + 1] * inv);
                        fb[idx + 2] = (byte)(sr * alpha + fb[idx + 2] * inv);
                        fb[idx + 3] = (byte)Math.Min(255, a + fb[idx + 3] * inv);
                    }
                }
            }
        }

        private static double Edge(double ax, double ay, double bx, double by, double cx, double cy)
            => (cx - ax) * (by - ay) - (cy - ay) * (bx - ax);

        private static List<Quad3D> BuildQuads(SkinRaster skin, bool slim)
        {
            var quads = new List<Quad3D>(72);
            int armWidth = slim ? 3 : 4;
            bool overlays = skin.HasOverlays;

            foreach (Part part in new[] { Part.Head, Part.Body, Part.RightLeg, Part.LeftLeg, Part.RightArm, Part.LeftArm })
            {
                var uv = BaseUv(part, false);
                if (!overlays && part == Part.LeftLeg) uv = (0, 16);
                if (!overlays && part == Part.LeftArm) uv = (40, 16);
                AddBoxQuads(quads, part, armWidth, uv.u, uv.v, overlay: false);
            }

            if (overlays)
            {
                foreach (Part part in new[] { Part.Head, Part.Body, Part.RightLeg, Part.LeftLeg, Part.RightArm, Part.LeftArm })
                {
                    var uv = BaseUv(part, true);
                    AddBoxQuads(quads, part, armWidth, uv.u, uv.v, overlay: true);
                }
            }

            return quads;
        }

        private static (int u, int v) BaseUv(Part part, bool overlay)
        {
            if (!overlay)
            {
                return part switch
                {
                    Part.Head => (0, 0),
                    Part.Body => (16, 16),
                    Part.RightLeg => (0, 16),
                    Part.LeftLeg => (16, 32),
                    Part.RightArm => (40, 16),
                    Part.LeftArm => (32, 48),
                    _ => (0, 0)
                };
            }

            return part switch
            {
                Part.Head => (32, 0),
                Part.Body => (16, 48),
                Part.RightLeg => (0, 32),
                Part.LeftLeg => (0, 48),
                Part.RightArm => (40, 32),
                Part.LeftArm => (48, 48),
                _ => (0, 0)
            };
        }

        private static (double x0, double x1, double y0, double y1, double z0, double z1) PartBox(Part part, int armWidth)
        {
            return part switch
            {
                Part.Head => (-4, 4, 24, 32, -4, 4),
                Part.Body => (-4, 4, 12, 24, -2, 2),
                Part.RightLeg => (-4, 0, 0, 12, -2, 2),
                Part.LeftLeg => (0, 4, 0, 12, -2, 2),
                Part.RightArm => (-(4.0 + armWidth), -4, 12, 24, -2, 2),
                Part.LeftArm => (4, 4 + armWidth, 12, 24, -2, 2),
                _ => (0, 0, 0, 0, 0, 0)
            };
        }

        private static void AddBoxQuads(List<Quad3D> quads, Part part, int armWidth, int uBase, int vBase, bool overlay)
        {
            var b = PartBox(part, armWidth);
            double w = b.x1 - b.x0;
            double h = b.y1 - b.y0;
            double d = b.z1 - b.z0;

            if (overlay)
            {
                const double pad = 0.4;
                b = (b.x0 - pad, b.x1 + pad, b.y0 - pad, b.y1 + pad, b.z0 - pad, b.z1 + pad);
            }

            AddFaceQuad(quads, b, UvRect(uBase + d, vBase + d, w, h), Face.Front, overlay, 1.0);
            AddFaceQuad(quads, b, UvRect(uBase + d + w + d, vBase + d, w, h), Face.Back, overlay, 0.85);
            AddFaceQuad(quads, b, UvRect(uBase, vBase + d, d, h), Face.SideRight, overlay, 0.90);
            AddFaceQuad(quads, b, UvRect(uBase + d + w, vBase + d, d, h), Face.SideLeft, overlay, 0.90);
            AddFaceQuad(quads, b, UvRect(uBase + d, vBase, w, d), Face.Top, overlay, 1.0);
            AddFaceQuad(quads, b, UvRect(uBase + d + w, vBase, w, d), Face.Bottom, overlay, 0.78);
        }

        private static Rect UvRect(double x64, double y64, double w64, double h64) =>
            new Rect(x64 / 64.0, y64 / 64.0, w64 / 64.0, h64 / 64.0);

        private static void AddFaceQuad(
            List<Quad3D> quads,
            (double x0, double x1, double y0, double y1, double z0, double z1) b,
            Rect uv, Face face, bool overlay, double shade)
        {
            const double centerY = 16.0;
            double x0 = b.x0, x1 = b.x1, z0 = b.z0, z1 = b.z1;
            double y0 = b.y0 - centerY, y1 = b.y1 - centerY;
            double u0 = uv.X, v0 = uv.Y, u1 = uv.X + uv.Width, v1 = uv.Y + uv.Height;

            (double x, double y, double z) tl, tr, br, bl;
            switch (face)
            {
                case Face.Front:
                    tl = (x0, y1, z0); tr = (x1, y1, z0); br = (x1, y0, z0); bl = (x0, y0, z0);
                    break;
                case Face.Back:
                    tl = (x1, y1, z1); tr = (x0, y1, z1); br = (x0, y0, z1); bl = (x1, y0, z1);
                    break;
                case Face.SideRight:
                    tl = (x1, y1, z0); tr = (x1, y1, z1); br = (x1, y0, z1); bl = (x1, y0, z0);
                    break;
                case Face.SideLeft:
                    tl = (x0, y1, z1); tr = (x0, y1, z0); br = (x0, y0, z0); bl = (x0, y0, z1);
                    break;
                case Face.Top:
                    tl = (x0, y1, z1); tr = (x1, y1, z1); br = (x1, y1, z0); bl = (x0, y1, z0);
                    break;
                default:
                    tl = (x0, y0, z0); tr = (x1, y0, z0); br = (x1, y0, z1); bl = (x0, y0, z1);
                    break;
            }

            quads.Add(new Quad3D
            {
                X0 = tl.x, Y0 = tl.y, Z0 = tl.z, U0 = u0, V0 = v0,
                X1 = tr.x, Y1 = tr.y, Z1 = tr.z, U1 = u1, V1 = v0,
                X2 = br.x, Y2 = br.y, Z2 = br.z, U2 = u1, V2 = v1,
                X3 = bl.x, Y3 = bl.y, Z3 = bl.z, U3 = u0, V3 = v1,
                Shade = shade,
                IsOverlay = overlay
            });
        }
    }
}
