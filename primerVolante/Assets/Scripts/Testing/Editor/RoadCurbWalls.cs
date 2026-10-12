using System.Collections.Generic;
using UnityEngine;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Cara de cordón que mira hacia la calzada, proyectada a XZ en el espacio local de la pieza de calle.
    /// El plano es <c>dot(p, Normal) = Offset</c> y el tramo va de <see cref="Start"/> a <see cref="End"/>
    /// sobre la tangente <c>(-Normal.y, Normal.x)</c>.
    /// </summary>
    public struct CurbFace
    {
        public Vector2 Normal;
        public float Offset;
        public float Start;
        public float End;

        /// <summary>Tangente del tramo (la normal rotada 90° en XZ).</summary>
        public Vector2 Tangent => new Vector2(-Normal.y, Normal.x);

        /// <summary>Punto de la cara a la posición <paramref name="s"/> sobre la tangente.</summary>
        public Vector2 PointAt(float s) => Normal * Offset + Tangent * s;

        public float Length => End - Start;

        public override string ToString() =>
            $"n=({Normal.x:F2},{Normal.y:F2}) d={Offset:F2} s=[{Start:F2}..{End:F2}]";
    }

    /// <summary>
    /// Caja de un muro de cordón en XZ: centro, tangente, largo y espesor. El muro arranca en la cara del cordón
    /// y se extiende <see cref="Thickness"/> metros hacia la vereda (contra la normal).
    /// </summary>
    public struct CurbWall
    {
        public Vector2 Center;
        public Vector2 Tangent;
        public Vector2 Normal;
        public float Length;
        public float Thickness;

        /// <summary>true si la caja está alineada con los ejes X/Z de la pieza (no necesita rotación).</summary>
        public bool IsAxisAligned => Mathf.Abs(Normal.x) > 0.999f || Mathf.Abs(Normal.y) > 0.999f;
    }

    /// <summary>
    /// Derivación pura (sin escena) de los muros de cordón de una pieza de calle a partir de su malla:
    /// caras casi verticales de ≈ 0.2 m de alto cuya normal apunta hacia la calzada, fusionadas por tramos.
    /// </summary>
    public static class RoadCurbWalls
    {
        /// <summary>Altura mínima y máxima de una cara de cordón (m).</summary>
        public const float CURB_MIN_HEIGHT = 0.15f;
        public const float CURB_MAX_HEIGHT = 0.25f;

        /// <summary>Altura bajo la cual una cara superior se considera asfalto; por encima, vereda.</summary>
        public const float GROUND_MAX_HEIGHT = 0.1f;

        // Distancia desde la cara hacia la calzada donde se mira qué hay (m).
        private const float k_ProbeDistance = 0.25f;

        // Paso del muestreo a lo largo de cada cara (m) y largo mínimo de un tramo que vale la pena (m).
        private const float k_SampleStep = 0.05f;
        private const float k_MinFaceLength = 0.2f;

        // Tolerancias de agrupado de caras coplanares y de unión de tramos contiguos.
        private const float k_OffsetTolerance = 0.02f;
        private const float k_MergeTolerance = 0.02f;
        private const float k_CornerTolerance = 0.05f;

        private struct Tri2
        {
            public Vector2 A, B, C;
        }

        private struct RawFace
        {
            public Vector2 Normal;
            public float Offset;
            public float Start;
            public float End;
        }

        /// <summary>
        /// Caras de cordón que miran hacia el asfalto. Se descartan las caras internas (entre dos veredas
        /// contiguas) y las del borde exterior de la pieza.
        /// </summary>
        public static List<CurbFace> FindRoadFacingFaces(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;

            var ground = new List<Tri2>();
            var raised = new List<Tri2>();
            var raw = new List<RawFace>();

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 p0 = vertices[triangles[i]];
                Vector3 p1 = vertices[triangles[i + 1]];
                Vector3 p2 = vertices[triangles[i + 2]];

                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                float magnitude = n.magnitude;
                if (magnitude < 1e-6f) continue;
                n /= magnitude;

                if (n.y > 0.9f)
                {
                    float y = (p0.y + p1.y + p2.y) / 3f;
                    var tri = new Tri2 { A = XZ(p0), B = XZ(p1), C = XZ(p2) };
                    if (y < GROUND_MAX_HEIGHT) ground.Add(tri);
                    else raised.Add(tri);
                }
                else if (Mathf.Abs(n.y) < 0.1f)
                {
                    float yMin = Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y));
                    float yMax = Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y));
                    float height = yMax - yMin;
                    if (Mathf.Abs(yMin) > 0.05f || height < CURB_MIN_HEIGHT || height > CURB_MAX_HEIGHT) continue;

                    var normal = new Vector2(n.x, n.z).normalized;
                    var tangent = new Vector2(-normal.y, normal.x);
                    float s0 = Vector2.Dot(XZ(p0), tangent);
                    float s1 = Vector2.Dot(XZ(p1), tangent);
                    float s2 = Vector2.Dot(XZ(p2), tangent);
                    raw.Add(new RawFace
                    {
                        Normal = normal,
                        Offset = Vector2.Dot(XZ(p0), normal),
                        Start = Mathf.Min(s0, Mathf.Min(s1, s2)),
                        End = Mathf.Max(s0, Mathf.Max(s1, s2)),
                    });
                }
            }

            var result = new List<CurbFace>();
            foreach (List<RawFace> group in GroupCoplanar(raw))
            {
                foreach (RawFace merged in MergeContiguous(group))
                {
                    SplitByRoadFacing(merged, ground, raised, result);
                }
            }

            result.Sort((a, b) =>
            {
                int byNormal = a.Normal.x != b.Normal.x ? a.Normal.x.CompareTo(b.Normal.x) : a.Normal.y.CompareTo(b.Normal.y);
                if (byNormal != 0) return byNormal;
                int byOffset = a.Offset.CompareTo(b.Offset);
                return byOffset != 0 ? byOffset : a.Start.CompareTo(b.Start);
            });
            return result;
        }

        /// <summary>
        /// Convierte las caras en cajas de muro. Cada caja arranca en la cara y se extiende
        /// <paramref name="thickness"/> hacia la vereda; en las esquinas donde la calzada forma un rincón se
        /// prolonga el espesor para que no queden rendijas entre cajas perpendiculares.
        /// </summary>
        public static List<CurbWall> BuildWalls(IReadOnlyList<CurbFace> faces, float thickness)
        {
            var walls = new List<CurbWall>(faces.Count);
            for (int i = 0; i < faces.Count; i++)
            {
                CurbFace face = faces[i];
                float start = face.Start;
                float end = face.End;

                if (HasRoadSideCorner(faces, i, face.PointAt(face.Start))) start -= thickness;
                if (HasRoadSideCorner(faces, i, face.PointAt(face.End))) end += thickness;

                float middle = (start + end) * 0.5f;
                walls.Add(new CurbWall
                {
                    Center = face.PointAt(middle) - face.Normal * (thickness * 0.5f),
                    Tangent = face.Tangent,
                    Normal = face.Normal,
                    Length = end - start,
                    Thickness = thickness,
                });
            }

            return walls;
        }

        // ------------------------------------------------------------------ Internos

        private static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);

        private static List<List<RawFace>> GroupCoplanar(List<RawFace> faces)
        {
            var groups = new List<List<RawFace>>();
            foreach (RawFace face in faces)
            {
                List<RawFace> target = null;
                foreach (List<RawFace> group in groups)
                {
                    RawFace reference = group[0];
                    if (Vector2.Dot(reference.Normal, face.Normal) > 0.9999f &&
                        Mathf.Abs(reference.Offset - face.Offset) < k_OffsetTolerance)
                    {
                        target = group;
                        break;
                    }
                }

                if (target == null)
                {
                    target = new List<RawFace>();
                    groups.Add(target);
                }

                target.Add(face);
            }

            return groups;
        }

        private static List<RawFace> MergeContiguous(List<RawFace> group)
        {
            group.Sort((a, b) => a.Start.CompareTo(b.Start));

            var merged = new List<RawFace>();
            RawFace current = group[0];
            for (int i = 1; i < group.Count; i++)
            {
                if (group[i].Start <= current.End + k_MergeTolerance)
                {
                    current.End = Mathf.Max(current.End, group[i].End);
                }
                else
                {
                    merged.Add(current);
                    current = group[i];
                }
            }

            merged.Add(current);
            return merged;
        }

        // Una cara (cuadrilátero entero) puede estar mitad al aire y mitad pegada a otra vereda: se muestrea
        // a lo largo y se queda con los tramos cuyo frente es asfalto.
        private static void SplitByRoadFacing(RawFace face, List<Tri2> ground, List<Tri2> raised, List<CurbFace> output)
        {
            Vector2 tangent = new Vector2(-face.Normal.y, face.Normal.x);
            int steps = Mathf.Max(1, Mathf.CeilToInt((face.End - face.Start) / k_SampleStep));
            float step = (face.End - face.Start) / steps;

            float runStart = 0f;
            bool inRun = false;
            for (int i = 0; i < steps; i++)
            {
                float s = face.Start + (i + 0.5f) * step;
                Vector2 probe = face.Normal * face.Offset + tangent * s + face.Normal * k_ProbeDistance;
                bool facesRoad = !ContainedIn(raised, probe) && ContainedIn(ground, probe);

                if (facesRoad && !inRun)
                {
                    inRun = true;
                    runStart = face.Start + i * step;
                }
                else if (!facesRoad && inRun)
                {
                    inRun = false;
                    EmitRun(face, runStart, face.Start + i * step, output);
                }
            }

            if (inRun) EmitRun(face, runStart, face.End, output);
        }

        private static void EmitRun(RawFace face, float start, float end, List<CurbFace> output)
        {
            if (end - start < k_MinFaceLength) return;
            output.Add(new CurbFace { Normal = face.Normal, Offset = face.Offset, Start = start, End = end });
        }

        private static bool ContainedIn(List<Tri2> triangles, Vector2 point)
        {
            foreach (Tri2 t in triangles)
            {
                if (PointInTriangle(point, t)) return true;
            }

            return false;
        }

        private static bool PointInTriangle(Vector2 p, Tri2 t)
        {
            const float eps = 1e-4f;
            float d1 = Cross(p, t.A, t.B);
            float d2 = Cross(p, t.B, t.C);
            float d3 = Cross(p, t.C, t.A);
            bool hasNegative = d1 < -eps || d2 < -eps || d3 < -eps;
            bool hasPositive = d1 > eps || d2 > eps || d3 > eps;
            return !(hasNegative && hasPositive);
        }

        private static float Cross(Vector2 p, Vector2 a, Vector2 b)
        {
            return (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
        }

        // true si en el punto <corner> de la cara <index> empieza una cara perpendicular que sigue hacia el lado
        // de la calzada: es un rincón de la calzada y la caja debe prolongarse hacia la vereda.
        private static bool HasRoadSideCorner(IReadOnlyList<CurbFace> faces, int index, Vector2 corner)
        {
            CurbFace face = faces[index];
            for (int j = 0; j < faces.Count; j++)
            {
                if (j == index) continue;
                CurbFace other = faces[j];
                if (Mathf.Abs(Vector2.Dot(face.Normal, other.Normal)) > 0.01f) continue;

                Vector2 a = other.PointAt(other.Start);
                Vector2 b = other.PointAt(other.End);
                Vector2 far;
                if ((a - corner).sqrMagnitude < k_CornerTolerance * k_CornerTolerance) far = b;
                else if ((b - corner).sqrMagnitude < k_CornerTolerance * k_CornerTolerance) far = a;
                else continue;

                if (Vector2.Dot(far - corner, face.Normal) > 0.1f) return true;
            }

            return false;
        }
    }
}
