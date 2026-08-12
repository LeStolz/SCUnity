using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace SCUnity.Editor
{
    public class SCXMLEdge : Edge
    {
        private readonly VisualElement labelContainer;
        private readonly SCXMLNode targetNode;
        private bool hovered = false;

        public SCXMLTransitionData data;

        public SCXMLEdge(SCXMLTransitionData data, SCXMLNode targetNode)
        {
            this.data = data;
            this.targetNode = targetNode;

            labelContainer = new VisualElement();
            labelContainer.style.position = Position.Absolute;
            Add(labelContainer);
            UpdateLabel();

            var arrowElement = new ArrowElement { name = "edge-arrow" };
            arrowElement.style.position = Position.Absolute;
            arrowElement.style.width = 14;
            arrowElement.style.height = 14;
            arrowElement.pickingMode = PickingMode.Ignore;
            Add(arrowElement);

            RegisterCallback<MouseEnterEvent>(e => { hovered = true; MarkDirtyRepaint(); });
            RegisterCallback<MouseLeaveEvent>(e => { hovered = false; MarkDirtyRepaint(); });
        }

        public void UpdateLabel()
        {
            string title = "";
            if (!string.IsNullOrEmpty(data.@event))
            {
                title += data.@event;
            }
            if (!string.IsNullOrEmpty(data.condition))
            {
                title += string.IsNullOrEmpty(title) ? $"[{data.condition}]" : $"\n[{data.condition}]";
            }

            labelContainer.Clear();

            if (!string.IsNullOrEmpty(title))
            {
                var label = new Label(title);
                labelContainer.Add(label);
            }
        }

        Vector2[] points = new Vector2[4];
        static readonly float EDGE_OFFSET = 80f;
        static readonly float EDGE_PADDING = 40f;
        static readonly Color EDGE_COLOR = new(0.85f, 0.85f, 0.85f, 1f);

        private class ArrowElement : VisualElement
        {
            public ArrowElement()
            {
                generateVisualContent += OnGenerateVisualContent;
            }

            private void OnGenerateVisualContent(MeshGenerationContext ctx)
            {
                float w = layout.width;
                float h = layout.height;
                if (w <= 0 || h <= 0) return;

                Vertex[] vertices = new Vertex[3];

                // Draw a right-pointing triangle
                vertices[0] = new Vertex { position = new Vector3(0, 0, Vertex.nearZ), tint = EDGE_COLOR };
                vertices[1] = new Vertex { position = new Vector3(w, h / 2f, Vertex.nearZ), tint = EDGE_COLOR };
                vertices[2] = new Vertex { position = new Vector3(0, h, Vertex.nearZ), tint = EDGE_COLOR };

                ushort[] indices = { 0, 1, 2 };

                var mesh = ctx.Allocate(3, 3);
                mesh.SetAllVertices(vertices);
                mesh.SetAllIndices(indices);
            }
        }

        public override bool UpdateEdgeControl()
        {
            base.UpdateEdgeControl();

            if (parent == null || targetNode == null) return false;

            SCXMLNode sourceNode = output?.node as SCXMLNode;

            int totalEdges = 1;
            int currentIndex = 0;
            int totalReverseEdges = 0;

            if (sourceNode != null && targetNode != null)
            {
                var bundleEdges = new List<Edge>();
                foreach (var edge in sourceNode.OutputPort.connections)
                {
                    if (edge.input?.node == targetNode)
                    {
                        bundleEdges.Add(edge);
                    }
                }

                bundleEdges.Sort((a, b) => a.GetHashCode().CompareTo(b.GetHashCode()));
                totalEdges = bundleEdges.Count;
                currentIndex = bundleEdges.IndexOf(this);

                if (targetNode.OutputPort != null)
                {
                    totalReverseEdges = targetNode.OutputPort.connections.Count(edge => edge.input?.node == sourceNode);
                }
            }

            float currentOffset = (currentIndex * EDGE_OFFSET) - ((totalEdges - 1) * EDGE_OFFSET / 2f);

            if (totalReverseEdges > 0)
            {
                float bundleWidth = (totalEdges - 1) * EDGE_OFFSET;
                currentOffset += (bundleWidth / 2f) + 40f;
            }



            Vector2 p0_graph = sourceNode != null ? parent.WorldToLocal(sourceNode.worldBound.center) : Vector2.zero;
            Vector2 p3_graph = targetNode != null ? parent.WorldToLocal(targetNode.worldBound.center) : Vector2.zero;

            if (p0_graph == Vector2.zero && p3_graph == Vector2.zero) return false;

            float dist = Vector2.Distance(p0_graph, p3_graph);
            Vector2 dir = (p3_graph - p0_graph).normalized;
            Vector2 normal = new(-dir.y, dir.x);
            Vector2 offsetVector = normal * currentOffset;

            Vector2 p0 = p0_graph;
            Vector2 p3 = p3_graph;

            float edgePush = Mathf.Min(dist * 0.333f, Mathf.Max(40f, Mathf.Abs(currentOffset) * 1.2f));

            Vector2 p1 = p0_graph + dir * edgePush + offsetVector;
            Vector2 p2 = p3_graph - dir * edgePush + offsetVector;



            float minX = Mathf.Min(p0.x, p1.x, p2.x, p3.x);
            float minY = Mathf.Min(p0.y, p1.y, p2.y, p3.y);
            float maxX = Mathf.Max(p0.x, p1.x, p2.x, p3.x);
            float maxY = Mathf.Max(p0.y, p1.y, p2.y, p3.y);

            Rect bounds = new(
                minX - EDGE_PADDING, minY - EDGE_PADDING,
                (maxX - minX) + EDGE_PADDING * 2, (maxY - minY) + EDGE_PADDING * 2
            );

            style.left = bounds.x;
            style.top = bounds.y;
            style.width = bounds.width;
            style.height = bounds.height;
            style.opacity = 1f;

            if (edgeControl != null)
            {
                edgeControl.style.display = DisplayStyle.None;
            }

            if (points == null || points.Length != 4) points = new Vector2[4];
            points[0] = p0;
            points[1] = p1;
            points[2] = p2;
            points[3] = p3;

            // Calculate the pure mathematical midpoint of the Cubic Bezier at t=0.5
            Vector2 center = 0.125f * p0 + 0.375f * p1 + 0.375f * p2 + 0.125f * p3;
            center -= bounds.position;

            if (labelContainer != null)
            {
                float hw = labelContainer.layout.width / 2f;
                float hh = labelContainer.layout.height / 2f;

                float projectedRadius = hw * Mathf.Abs(normal.x) + hh * Mathf.Abs(normal.y);
                float offsetAmount = (projectedRadius > 0 ? projectedRadius : 20f) + 10f;

                Vector2 textCenter = center + normal * offsetAmount;

                labelContainer.style.left = textCenter.x - hw;
                labelContainer.style.top = textCenter.y - hh;
            }

            var arrowLabel = this.Q<ArrowElement>("edge-arrow");
            if (arrowLabel != null)
            {
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

                arrowLabel.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(50));
                arrowLabel.style.rotate = new Rotate(new Angle(angle, AngleUnit.Degree));

                float aw = arrowLabel.layout.width > 0 ? arrowLabel.layout.width / 2f : 7f;
                float ah = arrowLabel.layout.height > 0 ? arrowLabel.layout.height / 2f : 7f;

                arrowLabel.style.left = center.x - aw;
                arrowLabel.style.top = center.y - ah;
            }

            generateVisualContent = context =>
            {
                if (points == null || points.Length < 4) return;

                Vector2 p0 = points[0] - bounds.position;
                Vector2 p1 = points[1] - bounds.position;
                Vector2 p2 = points[2] - bounds.position;
                Vector2 p3 = points[3] - bounds.position;

                int segments = 40;
                Vector2[] curvePoints = new Vector2[segments + 1];

                for (int i = 0; i <= segments; i++)
                {
                    float t = i / (float)segments;
                    float u = 1f - t;
                    curvePoints[i] =
                        u * u * u * p0 +
                        3f * u * u * t * p1 +
                        3f * u * t * t * p2 +
                        t * t * t * p3
                    ;
                }

                if (selected || hovered)
                {
                    Color highlightColor = selected ? new Color(0.25f, 0.5f, 0.9f, 1f) : new Color(0.4f, 0.6f, 0.9f, 0.5f);
                    DrawPolyLine(context, curvePoints, 5f, highlightColor);
                }

                DrawPolyLine(context, curvePoints, 2f, EDGE_COLOR);
            };

            MarkDirtyRepaint();

            return true;
        }

        private void DrawPolyLine(MeshGenerationContext context, Vector2[] pts, float thickness, Color color)
        {
            if (pts.Length < 2) return;

            var mesh = context.Allocate(pts.Length * 2, (pts.Length - 1) * 6);

            for (int i = 0; i < pts.Length; i++)
            {
                Vector2 d;
                if (i == 0) d = (pts[1] - pts[0]).normalized;
                else if (i == pts.Length - 1) d = (pts[i] - pts[i - 1]).normalized;
                else d = (pts[i + 1] - pts[i - 1]).normalized;

                Vector2 n = new Vector2(-d.y, d.x) * (thickness / 2f);

                mesh.SetNextVertex(
                    new Vertex { position = new Vector3(pts[i].x + n.x, pts[i].y + n.y, Vertex.nearZ), tint = color }
                );
                mesh.SetNextVertex(
                    new Vertex { position = new Vector3(pts[i].x - n.x, pts[i].y - n.y, Vertex.nearZ), tint = color }
                );
            }

            for (int i = 0; i < pts.Length - 1; i++)
            {
                int v = i * 2;
                mesh.SetNextIndex((ushort)v);
                mesh.SetNextIndex((ushort)(v + 1));
                mesh.SetNextIndex((ushort)(v + 2));

                mesh.SetNextIndex((ushort)(v + 2));
                mesh.SetNextIndex((ushort)(v + 1));
                mesh.SetNextIndex((ushort)(v + 3));
            }
        }

        public override void OnSelected()
        {
            base.OnSelected();
            MarkDirtyRepaint();
        }

        public override void OnUnselected()
        {
            base.OnUnselected();
            MarkDirtyRepaint();
        }

        public override bool ContainsPoint(Vector2 localPoint)
        {
            if (points == null || points.Length < 4) return false;

            float threshold = 15f;

            Vector2 boundsPos = new(style.left.value.value, style.top.value.value);
            Vector2 p0 = points[0] - boundsPos;
            Vector2 p1 = points[1] - boundsPos;
            Vector2 p2 = points[2] - boundsPos;
            Vector2 p3 = points[3] - boundsPos;

            Vector2 prevPoint = p0;
            for (int i = 1; i <= 10; i++)
            {
                float t = i / 10f;
                float u = 1f - t;
                Vector2 p =
                    u * u * u * p0 +
                    3f * u * u * t * p1 +
                    3f * u * t * t * p2 +
                    t * t * t * p3
                ;

                if (DistancePointLine(localPoint, prevPoint, p) < threshold)
                {
                    return true;
                }

                prevPoint = p;
            }

            return false;
        }

        private float DistancePointLine(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float sqrLen = ab.sqrMagnitude;
            if (sqrLen < 0.001f) return Vector2.Distance(p, a);

            float t = Vector2.Dot(p - a, ab) / sqrLen;
            if (t < 0f) return Vector2.Distance(p, a);
            if (t > 1f) return Vector2.Distance(p, b);

            return Vector2.Distance(p, a + t * ab);
        }
    }
}
