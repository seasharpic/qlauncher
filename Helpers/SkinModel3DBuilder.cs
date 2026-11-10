using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace MinecraftLauncher.Helpers
{
    /// <summary>
    /// Собирает 3D-модель игрока для Viewport3D из PNG-скина: боксы
    /// головы/тела/рук/ног с box-UV-маппингом на области текстуры
    /// (64x64 и 64x32, HD-скины через масштаб), плюс оверлей-слои
    /// (шляпа, куртка, рукава, штаны) с учётом прозрачности PNG.
    /// WPF-специфично (Media3D), поэтому живёт в WPF-сборке, а не в ядре.
    /// </summary>
    public static class SkinModel3DBuilder
    {
        /// <summary>Индексированные пиксели скина в PBGRA32: slim-детект и текстура.</summary>
        public sealed class SkinRaster
        {
            public BitmapSource? Source;
            public byte[] Data = Array.Empty<byte>();
            public int Width;
            public int Height;
            public int Stride;

            /// <summary>Сколько реальных пикселей приходится на один пиксель 64-пространства.</summary>
            public int Scale;

            public bool IsOpaque(int x64, int y64)
            {
                int x = Math.Min(Width - 1, x64 * Scale);
                int y = Math.Min(Height - 1, y64 * Scale);
                if (x < 0 || y < 0) return false;
                return Data[y * Stride + x * 4 + 3] > 0;
            }

            public bool HasOverlays => Height > 32 * Scale;
        }

        public static SkinRaster Rasterize(BitmapSource skin)
        {
            // В PBGRA32 любой PNG (в том числе HD) ложится в один байтовый формат.
            var converted = new FormatConvertedBitmap(skin, PixelFormats.Pbgra32, null, 0);
            int w = converted.PixelWidth;
            int h = converted.PixelHeight;
            int stride = w * 4;
            var data = new byte[stride * h];
            converted.CopyPixels(data, stride, 0);

            return new SkinRaster
            {
                Source = skin,
                Data = data,
                Width = w,
                Height = h,
                Stride = stride,
                Scale = Math.Max(1, w / 64)
            };
        }

        /// <summary>
        /// Slim-руки: у 64x64-скинов в хвосте правой руки остаются прозрачные
        /// пиксели (x=54..55), у classic они заполнены. Стандартный эвристический
        /// детект, какой применяют все сторонние рендеры.
        /// </summary>
        public static bool DetectSlim(SkinRaster raster)
        {
            if (!raster.HasOverlays) return false; // у 64x32-скинов slim не встречается
            return !raster.IsOpaque(54, 20) && !raster.IsOpaque(55, 20);
        }

        public static Model3DGroup Build(BitmapSource skin, bool slim)
        {
            return Build(Rasterize(skin), slim);
        }

        /// <summary>Канонические UV-базы для box-блока каждой части: (u,v) правого-верхнего.</summary>
        private static (int u, int v) BaseUv(Part part, bool overlay)
        {
            if (!overlay)
            {
                return part switch
                {
                    Part.Head => (0, 0),
                    Part.Body => (16, 16),
                    Part.RightLeg => (0, 16),
                    Part.LeftLeg => (16, 32),   // 64x64: у левых конечностей своя область
                    Part.RightArm => (40, 16),
                    Part.LeftArm => (32, 48),
                    _ => (0, 0)
                };
            }

            // Оверлей-блоки живут в нижней половине 64x64-скина.
            return part switch
            {
                Part.Head => (32, 0),      // шляпа
                Part.Body => (16, 48),     // куртка
                Part.RightLeg => (0, 32),  // правые штаны
                Part.LeftLeg => (0, 48),   // левые штаны
                Part.RightArm => (40, 32), // правый рукав
                Part.LeftArm => (48, 48),  // левый рукав
                _ => (0, 0)
            };
        }

        /// <summary>Геометрия части в 64-пикселях: y — от ног (0) до макушки (32).</summary>
        private static (double x0, double x1, double y0, double y1, double z0, double z1) PartBox(
            Part part, int armWidth)
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

        public static Model3DGroup Build(SkinRaster skin, bool slim)
        {
            int armWidth = slim ? 3 : 4;
            bool overlays = skin.HasOverlays;

            var brush = new ImageBrush(skin.Source!)
            {
                TileMode = TileMode.None,
                Stretch = Stretch.Fill
            };
            brush.Freeze();

            var baseMaterial = new DiffuseMaterial(brush) { AmbientColor = Colors.White };
            var overlayMaterial = new DiffuseMaterial(brush) { AmbientColor = Colors.White };

            var group = new Model3DGroup();

            foreach (Part part in new[]
                     { Part.Head, Part.Body, Part.RightLeg, Part.LeftLeg, Part.RightArm, Part.LeftArm })
            {
                // 64x32-скины: у левых конечностей нет своей UV-области,
                // используют зеркальных правых.
                var uv = BaseUv(part, false);
                if (!overlays && part == Part.LeftLeg) uv = (0, 16);
                if (!overlays && part == Part.LeftArm) uv = (40, 16);

                AddBox(group, baseMaterial, skin, part, armWidth, uv.u, uv.v, overlay: false);
            }

            if (overlays)
            {
                foreach (Part part in new[]
                         { Part.Head, Part.Body, Part.RightLeg, Part.LeftLeg, Part.RightArm, Part.LeftArm })
                {
                    var uv = BaseUv(part, overlay: true);
                    AddBox(group, overlayMaterial, skin, part, armWidth, uv.u, uv.v, overlay: true);
                }
            }

            return group;
        }

        /// <summary>
        /// Добавляет бокс части: 6 лиц с box-UV. Распаковка стандартная:
        /// право | фронт | лево | спина второй строкой от (u, v+d),
        /// top/bottom первой строкой от (u+d, v).
        /// </summary>
        private static void AddBox(
            Model3DGroup group, Material material, SkinRaster skin,
            Part part, int armWidth, int uBase, int vBase, bool overlay)
        {
            var b = PartBox(part, armWidth);
            double w = b.x1 - b.x0;
            double h = b.y1 - b.y0;
            double d = b.z1 - b.z0;

            var uvRight = UvRect(uBase, vBase + d, d, h);
            var uvFront = UvRect(uBase + d, vBase + d, w, h);
            var uvLeft = UvRect(uBase + d + w, vBase + d, d, h);
            var uvBack = UvRect(uBase + d + w + d, vBase + d, w, h);
            var uvTop = UvRect(uBase + d, vBase, w, d);
            var uvBottom = UvRect(uBase + d + w, vBase, w, d);

            var geometry = new MeshGeometry3D();
            AddFace(geometry, b, uvFront, Face.Front);
            AddFace(geometry, b, uvBack, Face.Back);
            AddFace(geometry, b, uvRight, Face.SideRight);
            AddFace(geometry, b, uvLeft, Face.SideLeft);
            AddFace(geometry, b, uvTop, Face.Top);
            AddFace(geometry, b, uvBottom, Face.Bottom);

            var model = new GeometryModel3D
            {
                Geometry = geometry,
                Material = material,
                // Оверлей прозрачен: глядя сквозь пустые пиксели, нужна и задняя грань.
                BackMaterial = material
            };

            if (overlay)
            {
                // Оверлей на 0.5px шире с каждой стороны, но вполне вокруг
                // центра части (CenterX/Y/Z), чтобы модель не разъезжалась.
                model.Transform = new ScaleTransform3D(
                    (w + 1) / w, (h + 1) / h, (d + 1) / d)
                {
                    CenterX = b.x0 + w / 2,
                    CenterY = b.y0 + h / 2,
                    CenterZ = b.z0 + d / 2
                };
            }

            group.Children.Add(model);
        }

        private static Rect UvRect(double x64, double y64, double w64, double h64) =>
            new Rect(x64 / 64.0, y64 / 64.0, w64 / 64.0, h64 / 64.0);

        private enum Part { Head, Body, RightLeg, LeftLeg, RightArm, LeftArm }

        private enum Face { Front, Back, SideRight, SideLeft, Top, Bottom }

        private static void AddFace(
            MeshGeometry3D mesh,
            (double x0, double x1, double y0, double y1, double z0, double z1) b,
            Rect uv, Face face)
        {
            // Модель рендерится вокруг собственного центра (высота 32 → центр y=16).
            const double centerY = 16.0;
            double x0 = b.x0, x1 = b.x1, z0 = b.z0, z1 = b.z1;
            double y0 = b.y0 - centerY, y1 = b.y1 - centerY;

            double u0 = uv.X, v0 = uv.Y, u1 = uv.X + uv.Width, v1 = uv.Y + uv.Height;

            Point3D tl, tr, br, bl;
            switch (face)
            {
                case Face.Front: // персонаж лицом к камере (-z)
                    tl = new Point3D(x0, y1, z0); tr = new Point3D(x1, y1, z0);
                    br = new Point3D(x1, y0, z0); bl = new Point3D(x0, y0, z0);
                    break;
                case Face.Back:
                    tl = new Point3D(x1, y1, z1); tr = new Point3D(x0, y1, z1);
                    br = new Point3D(x0, y0, z1); bl = new Point3D(x1, y0, z1);
                    break;
                case Face.SideRight: // грань +X
                    tl = new Point3D(x1, y1, z0); tr = new Point3D(x1, y1, z1);
                    br = new Point3D(x1, y0, z1); bl = new Point3D(x1, y0, z0);
                    break;
                case Face.SideLeft: // грань -X
                    tl = new Point3D(x0, y1, z1); tr = new Point3D(x0, y1, z0);
                    br = new Point3D(x0, y0, z0); bl = new Point3D(x0, y0, z1);
                    break;
                case Face.Top:
                    tl = new Point3D(x0, y1, z1); tr = new Point3D(x1, y1, z1);
                    br = new Point3D(x1, y1, z0); bl = new Point3D(x0, y1, z0);
                    break;
                default: // Bottom
                    tl = new Point3D(x0, y0, z0); tr = new Point3D(x1, y0, z0);
                    br = new Point3D(x1, y0, z1); bl = new Point3D(x0, y0, z1);
                    break;
            }

            int i = mesh.Positions.Count;
            mesh.Positions.Add(tl);
            mesh.Positions.Add(tr);
            mesh.Positions.Add(br);
            mesh.Positions.Add(bl);
            mesh.TriangleIndices.Add(i + 0);
            mesh.TriangleIndices.Add(i + 1);
            mesh.TriangleIndices.Add(i + 2);
            mesh.TriangleIndices.Add(i + 0);
            mesh.TriangleIndices.Add(i + 2);
            mesh.TriangleIndices.Add(i + 3);
            mesh.TextureCoordinates.Add(new Point(u0, v0));
            mesh.TextureCoordinates.Add(new Point(u1, v0));
            mesh.TextureCoordinates.Add(new Point(u1, v1));
            mesh.TextureCoordinates.Add(new Point(u0, v1));
        }
    }
}
