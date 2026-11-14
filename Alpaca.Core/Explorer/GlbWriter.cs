using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Alpaca4d.Explorer
{
    /// <summary>
    /// A binary glTF (.glb) of a design, for the 3D view of the explorer.
    ///
    /// glTF because every browser viewer reads it, and a .glb because a design is then one file
    /// next to its image. The writer takes plain arrays rather than Rhino meshes, so the format is
    /// checked outside Rhino; turning Rhino geometry into these arrays is the Grasshopper side's job.
    ///
    /// What it writes, and why:
    ///   - Surfaces as triangles with normals, lines as line segments, each with a colour per vertex.
    ///     Results in Alpaca4d are drawn as vertex colours, so that is what carries them here.
    ///   - Colours in linear space, as glTF asks. Rhino's colours are sRGB; written as they are, a
    ///     viewer would brighten every one of them on the way back to the screen. They are floats so
    ///     the dark end of a gradient does not band.
    ///   - Y up and metres, as glTF asks. <see cref="Point"/> turns Rhino's Z-up model units into that.
    ///   - Double-sided surfaces: a shell has no inside, and its back should not vanish.
    ///   - Points as points, coloured per point.
    ///   - Text as labels, not geometry. glTF has no text, and text turned into meshes stops reading
    ///     like a text dot: it turns away with the model and shrinks with distance. So each label -
    ///     its text, where it is, its colour, and whether it is a dot or plain text - is written into
    ///     the scene's extras, under "alpaca4d", and the explorer draws it facing the camera at a
    ///     constant size. Other glTF viewers show the model without them.
    /// </summary>
    public sealed class GlbWriter
    {
        private const int ArrayBuffer = 34962;
        private const int ElementArrayBuffer = 34963;
        private const int Float = 5126;
        private const int UnsignedInt = 5125;
        private const int Triangles = 4;
        private const int Lines = 1;
        private const int Points = 0;

        private readonly MemoryStream _binary = new MemoryStream();
        private readonly JArray _bufferViews = new JArray();
        private readonly JArray _accessors = new JArray();
        private readonly JArray _primitives = new JArray();
        private readonly JArray _materials = new JArray();
        private readonly JArray _labels = new JArray();
        private int _surfaceMaterial = -1;
        private int _lineMaterial = -1;
        private int _pointMaterial = -1;

        /// <summary>The name the file says made it.</summary>
        public string Generator { get; set; } = "Alpaca4d Explorer";

        /// <summary>True until something has been added.</summary>
        public bool IsEmpty => _primitives.Count == 0 && _labels.Count == 0;

        /// <summary>
        /// Adds triangles. Three floats per vertex for positions, normals and linear colours, and
        /// three vertex indices per triangle. Normals and colours may be null.
        /// </summary>
        public void AddTriangles(IList<float> positions, IList<float> normals, IList<float> colours, IList<int> indices)
        {
            Check(positions, normals, colours, indices, 3);
            if (indices.Count == 0) return;
            if (_surfaceMaterial < 0) _surfaceMaterial = AddMaterial(0.85, true);
            AddPrimitive(positions, normals, colours, indices, Triangles, _surfaceMaterial);
        }

        /// <summary>Adds line segments: two vertex indices per segment.</summary>
        public void AddLines(IList<float> positions, IList<float> colours, IList<int> indices)
        {
            Check(positions, null, colours, indices, 2);
            if (indices.Count == 0) return;
            if (_lineMaterial < 0) _lineMaterial = AddMaterial(1.0, false);
            AddPrimitive(positions, null, colours, indices, Lines, _lineMaterial);
        }

        /// <summary>Adds points: three floats per point for positions and linear colours.</summary>
        public void AddPoints(IList<float> positions, IList<float> colours)
        {
            Check(positions, null, colours, new int[0], 1);
            if (positions.Count == 0) return;
            if (_pointMaterial < 0) _pointMaterial = AddMaterial(1.0, false);
            AddPrimitive(positions, null, colours, null, Points, _pointMaterial);
        }

        /// <summary>
        /// Adds a label at a Rhino point (converted as <see cref="Point"/> converts it), in an sRGB
        /// colour. A dot is drawn as a filled tag in that colour, plain text as text in it.
        /// </summary>
        public void AddLabel(string text, double x, double y, double z, double toMetres, int r, int g, int b, bool dot)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var position = new List<float>();
            Point(x, y, z, toMetres, position);
            _labels.Add(new JObject
            {
                ["text"] = text.Trim(),
                ["position"] = new JArray(position[0], position[1], position[2]),
                ["color"] = $"#{Clamp(r):x2}{Clamp(g):x2}{Clamp(b):x2}",
                ["kind"] = dot ? "dot" : "text",
            });
        }

        private static int Clamp(int channel) => Math.Min(Math.Max(channel, 0), 255);

        /// <summary>
        /// A Rhino point as glTF wants it: Z up becomes Y up, and <paramref name="toMetres"/> scales
        /// the model's units to metres.
        /// </summary>
        public static void Point(double x, double y, double z, double toMetres, IList<float> into)
        {
            into.Add((float)(x * toMetres));
            into.Add((float)(z * toMetres));
            into.Add((float)(-y * toMetres));
        }

        /// <summary>A Rhino vector as glTF wants it, made unit length.</summary>
        public static void Normal(double x, double y, double z, IList<float> into)
        {
            double length = Math.Sqrt(x * x + y * y + z * z);
            if (length < 1e-12) { into.Add(0); into.Add(1); into.Add(0); return; }
            into.Add((float)(x / length));
            into.Add((float)(z / length));
            into.Add((float)(-y / length));
        }

        /// <summary>An sRGB colour, 0 to 255 per channel, in glTF's linear space.</summary>
        public static void Colour(int r, int g, int b, IList<float> into)
        {
            into.Add(ToLinear(r));
            into.Add(ToLinear(g));
            into.Add(ToLinear(b));
        }

        public static float ToLinear(int channel)
        {
            double c = Math.Min(Math.Max(channel, 0), 255) / 255.0;
            return (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        }

        public byte[] ToArray()
        {
            if (IsEmpty) throw new InvalidOperationException("There is nothing to write.");

            var scene = new JObject();
            if (_primitives.Count > 0) scene["nodes"] = new JArray(0);
            if (_labels.Count > 0) scene["extras"] = new JObject { ["alpaca4d"] = new JObject { ["labels"] = _labels } };

            var gltf = new JObject
            {
                ["asset"] = new JObject { ["version"] = "2.0", ["generator"] = Generator },
                ["scene"] = 0,
                ["scenes"] = new JArray(scene),
            };
            // A file of labels only has no geometry, and glTF wants no empty meshes or buffers.
            if (_primitives.Count > 0)
            {
                gltf["nodes"] = new JArray(new JObject { ["mesh"] = 0 });
                gltf["meshes"] = new JArray(new JObject { ["primitives"] = _primitives });
                gltf["materials"] = _materials;
                gltf["accessors"] = _accessors;
                gltf["bufferViews"] = _bufferViews;
                gltf["buffers"] = new JArray(new JObject { ["byteLength"] = _binary.Length });
            }

            byte[] json = Pad(Encoding.UTF8.GetBytes(gltf.ToString(Formatting.None)), 0x20);
            byte[] binary = Pad(_binary.ToArray(), 0x00);
            bool hasBinary = binary.Length > 0;

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(0x46546C67);                           // "glTF"
                writer.Write(2);                                    // version
                writer.Write(12 + 8 + json.Length + (hasBinary ? 8 + binary.Length : 0));
                writer.Write(json.Length);
                writer.Write(0x4E4F534A);                           // "JSON"
                writer.Write(json);
                if (hasBinary)
                {
                    writer.Write(binary.Length);
                    writer.Write(0x004E4942);                       // "BIN\0"
                    writer.Write(binary);
                }
                writer.Flush();
                return stream.ToArray();
            }
        }

        public void Save(string path)
        {
            File.WriteAllBytes(path, ToArray());
        }

        private static void Check(IList<float> positions, IList<float> normals, IList<float> colours, IList<int> indices, int perElement)
        {
            if (positions == null || indices == null) throw new ArgumentNullException(positions == null ? nameof(positions) : nameof(indices));
            if (positions.Count % 3 != 0) throw new ArgumentException("Positions need three floats per vertex.");
            int vertices = positions.Count / 3;
            if (normals != null && normals.Count != positions.Count) throw new ArgumentException("Normals need one per vertex.");
            if (colours != null && colours.Count != positions.Count) throw new ArgumentException("Colours need one per vertex.");
            if (indices.Count % perElement != 0) throw new ArgumentException($"Indices come in {perElement}s.");
            if (indices.Any(i => i < 0 || i >= vertices)) throw new ArgumentException("An index points past the vertices.");
        }

        private int AddMaterial(double roughness, bool doubleSided)
        {
            _materials.Add(new JObject
            {
                ["pbrMetallicRoughness"] = new JObject
                {
                    ["baseColorFactor"] = new JArray(1.0, 1.0, 1.0, 1.0),
                    ["metallicFactor"] = 0.0,
                    ["roughnessFactor"] = roughness,
                },
                ["doubleSided"] = doubleSided,
            });
            return _materials.Count - 1;
        }

        private void AddPrimitive(IList<float> positions, IList<float> normals, IList<float> colours, IList<int> indices, int mode, int material)
        {
            int count = positions.Count / 3;
            var attributes = new JObject();

            var min = new double[] { double.MaxValue, double.MaxValue, double.MaxValue };
            var max = new double[] { double.MinValue, double.MinValue, double.MinValue };
            for (int i = 0; i < positions.Count; i++)
            {
                min[i % 3] = Math.Min(min[i % 3], positions[i]);
                max[i % 3] = Math.Max(max[i % 3], positions[i]);
            }

            attributes["POSITION"] = AddAccessor(AddView(positions, ArrayBuffer), Float, count, "VEC3", min, max);
            if (normals != null)
                attributes["NORMAL"] = AddAccessor(AddView(normals, ArrayBuffer), Float, count, "VEC3", null, null);
            if (colours != null)
                attributes["COLOR_0"] = AddAccessor(AddView(colours, ArrayBuffer), Float, count, "VEC3", null, null);

            var primitive = new JObject { ["attributes"] = attributes };
            if (indices != null)
                primitive["indices"] = AddAccessor(AddView(indices, ElementArrayBuffer), UnsignedInt, indices.Count, "SCALAR", null, null);
            primitive["material"] = material;
            primitive["mode"] = mode;
            _primitives.Add(primitive);
        }

        private int AddView(IList<float> values, int target)
        {
            long offset = Align();
            var writer = new BinaryWriter(_binary);
            foreach (float value in values) writer.Write(value);
            return AddView(offset, target);
        }

        private int AddView(IList<int> values, int target)
        {
            long offset = Align();
            var writer = new BinaryWriter(_binary);
            foreach (int value in values) writer.Write((uint)value);
            return AddView(offset, target);
        }

        private int AddView(long offset, int target)
        {
            _bufferViews.Add(new JObject
            {
                ["buffer"] = 0,
                ["byteOffset"] = offset,
                ["byteLength"] = _binary.Length - offset,
                ["target"] = target,
            });
            return _bufferViews.Count - 1;
        }

        private int AddAccessor(int view, int componentType, int count, string type, double[] min, double[] max)
        {
            var accessor = new JObject
            {
                ["bufferView"] = view,
                ["componentType"] = componentType,
                ["count"] = count,
                ["type"] = type,
            };
            if (min != null) accessor["min"] = new JArray(min.Cast<object>().ToArray());
            if (max != null) accessor["max"] = new JArray(max.Cast<object>().ToArray());
            _accessors.Add(accessor);
            return _accessors.Count - 1;
        }

        /// <summary>Every buffer view starts on four bytes, as glTF requires of float and index data.</summary>
        private long Align()
        {
            while (_binary.Length % 4 != 0) _binary.WriteByte(0);
            _binary.Position = _binary.Length;
            return _binary.Length;
        }

        private static byte[] Pad(byte[] bytes, byte with)
        {
            int padding = (4 - bytes.Length % 4) % 4;
            if (padding == 0) return bytes;
            return bytes.Concat(Enumerable.Repeat(with, padding)).ToArray();
        }
    }
}
