using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace SCUnity.Editor
{
    public class SCXMLEdge : Edge
    {
        private VisualElement labelContainer;
        private SCXMLNode targetNode;
        private bool m_Hovered = false;

        public SCXMLTransitionData Data { get; set; }

        public SCXMLEdge(SCXMLTransitionData data, SCXMLNode targetNode)
        {
            this.Data = data;
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

            this.RegisterCallback<MouseEnterEvent>(e => { m_Hovered = true; this.MarkDirtyRepaint(); });
            this.RegisterCallback<MouseLeaveEvent>(e => { m_Hovered = false; this.MarkDirtyRepaint(); });
        }

        public void UpdateLabel()
        {
            string title = "";
            if (!string.IsNullOrEmpty(Data.Event)) title += Data.Event;
            if (!string.IsNullOrEmpty(Data.Condition)) title += string.IsNullOrEmpty(title) ? $"[{Data.Condition}]" : $"\n[{Data.Condition}]";

            labelContainer.Clear();

            if (!string.IsNullOrEmpty(title))
            {
                labelContainer.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.8f);
                labelContainer.style.paddingLeft = 4;
                labelContainer.style.paddingRight = 4;
                labelContainer.style.borderTopLeftRadius = 4;
                labelContainer.style.borderTopRightRadius = 4;
                labelContainer.style.borderBottomLeftRadius = 4;
                labelContainer.style.borderBottomRightRadius = 4;

                var lbl = new Label(title);
                lbl.style.color = Color.white;
                labelContainer.Add(lbl);
            }
            else
            {
                labelContainer.style.backgroundColor = Color.clear;
            }
        }

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
                Color arrowColor = new Color(0.85f, 0.85f, 0.85f, 1f);
                
                // Draw a right-pointing triangle
                vertices[0] = new Vertex { position = new Vector3(0, 0, Vertex.nearZ), tint = arrowColor };
                vertices[1] = new Vertex { position = new Vector3(w, h / 2f, Vertex.nearZ), tint = arrowColor };
                vertices[2] = new Vertex { position = new Vector3(0, h, Vertex.nearZ), tint = arrowColor };

                ushort[] indices = { 0, 1, 2 };

                var mesh = ctx.Allocate(3, 3);
                mesh.SetAllVertices(vertices);
                mesh.SetAllIndices(indices);
            }
        }

        private Vector2[] customPoints = new Vector2[4];

        public override bool UpdateEdgeControl()
        {
            base.UpdateEdgeControl();

            if (parent == null || targetNode == null)
                return false;

            SCXMLNode sourceNode = this.output?.node as SCXMLNode;

            var points = edgeControl?.controlPoints;
            if (points == null || points.Length < 4)
                return false;

            int totalEdges = 1;
            int myIndex = 0;
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
                myIndex = bundleEdges.IndexOf(this);

                if (targetNode.OutputPort != null)
                {
                    foreach (var edge in targetNode.OutputPort.connections)
                    {
                        if (edge.input?.node == sourceNode)
                        {
                            totalReverseEdges++;
                        }
                    }
                }
            }

            float offsetPerEdge = 80f; 
            float offset = (myIndex * offsetPerEdge) - ((totalEdges - 1) * offsetPerEdge / 2f);

            if (totalReverseEdges > 0)
            {
                float bundleWidth = (totalEdges - 1) * offsetPerEdge;
                offset += (bundleWidth / 2f) + 45f; 
            }

            Vector2 p0_graph = sourceNode != null ? this.parent.WorldToLocal(sourceNode.worldBound.center) : this.parent.WorldToLocal(this.LocalToWorld(points[0]));
            Vector2 p3_graph = targetNode != null ? this.parent.WorldToLocal(targetNode.worldBound.center) : this.parent.WorldToLocal(this.LocalToWorld(points[points.Length - 1]));

            float dist = Vector2.Distance(p0_graph, p3_graph);
            Vector2 dir = (p3_graph - p0_graph).normalized;
            Vector2 normal = new Vector2(-dir.y, dir.x);
            Vector2 offsetVector = normal * offset;

            Vector2 pt0 = p0_graph;
            Vector2 pt3 = p3_graph;
            
            float edgePush = Mathf.Min(dist * 0.333f, Mathf.Max(40f, Mathf.Abs(offset) * 1.2f));
            
            Vector2 pt1 = p0_graph + dir * edgePush + offsetVector;
            Vector2 pt2 = p3_graph - dir * edgePush + offsetVector;

            if (customPoints.Length != 4)
            {
                customPoints = new Vector2[4];
            }

            customPoints[0] = this.parent.LocalToWorld(pt0);
            customPoints[1] = this.parent.LocalToWorld(pt1);
            customPoints[2] = this.parent.LocalToWorld(pt2);
            customPoints[3] = this.parent.LocalToWorld(pt3);

            float minX = Mathf.Min(pt0.x, pt1.x, pt2.x, pt3.x);
            float minY = Mathf.Min(pt0.y, pt1.y, pt2.y, pt3.y);
            float maxX = Mathf.Max(pt0.x, pt1.x, pt2.x, pt3.x);
            float maxY = Mathf.Max(pt0.y, pt1.y, pt2.y, pt3.y);

            float padding = 50f;
            Rect bounds = new Rect(minX - padding, minY - padding, (maxX - minX) + padding * 2, (maxY - minY) + padding * 2);

            this.style.left = bounds.x;
            this.style.top = bounds.y;
            this.style.width = bounds.width;
            this.style.height = bounds.height;
            this.style.opacity = 1f;

            if (edgeControl != null)
            {
                // Completely hide native EdgeControl. Its hardcoded Bezier algorithms cannot be disabled.
                edgeControl.style.display = DisplayStyle.None;
            }
            
            // Populate our custom array so the MeshGenerationContext can draw it!
            if (customPoints == null || customPoints.Length != 4) customPoints = new Vector2[4];
            customPoints[0] = pt0;
            customPoints[1] = pt1;
            customPoints[2] = pt2;
            customPoints[3] = pt3;

            // Calculate the pure mathematical midpoint of the Cubic Bezier at t=0.5
            Vector2 center = 0.125f * pt0 + 0.375f * pt1 + 0.375f * pt2 + 0.125f * pt3;
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

            this.generateVisualContent = (MeshGenerationContext context) =>
            {
                if (customPoints == null || customPoints.Length < 4) return;

                Vector2 p0 = customPoints[0] - bounds.position;
                Vector2 p1 = customPoints[1] - bounds.position;
                Vector2 p2 = customPoints[2] - bounds.position;
                Vector2 p3 = customPoints[3] - bounds.position;

                int segments = 40;
                Vector2[] curvePoints = new Vector2[segments + 1];

                for (int i = 0; i <= segments; i++)
                {
                    float t = i / (float)segments;
                    float u = 1f - t;
                    curvePoints[i] = u * u * u * p0 + 
                                     3f * u * u * t * p1 + 
                                     3f * u * t * t * p2 + 
                                     t * t * t * p3;
                }

                // Draw Selection/Hover Outline
                if (this.selected || this.m_Hovered)
                {
                    Color highlightColor = this.selected ? new Color(0.27f, 0.54f, 0.9f, 1f) : new Color(0.4f, 0.6f, 0.9f, 0.5f);
                    DrawPolyLine(context, curvePoints, 5f, highlightColor);
                }

                // Draw Main Line
                Color edgeColor = new Color(0.8f, 0.8f, 0.8f, 1f);
                DrawPolyLine(context, curvePoints, 2f, edgeColor);
            };

            this.MarkDirtyRepaint();

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

                mesh.SetNextVertex(new Vertex { position = new Vector3(pts[i].x + n.x, pts[i].y + n.y, Vertex.nearZ), tint = color });
                mesh.SetNextVertex(new Vertex { position = new Vector3(pts[i].x - n.x, pts[i].y - n.y, Vertex.nearZ), tint = color });
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
            this.MarkDirtyRepaint();
        }

        public override void OnUnselected()
        {
            base.OnUnselected();
            this.MarkDirtyRepaint();
        }

        public override bool ContainsPoint(Vector2 localPoint)
        {
            if (customPoints == null || customPoints.Length < 4) return false;

            float threshold = 15f; 
            
            // localPoint is in SCXMLEdge space. customPoints is in Graph Space.
            // We must shift customPoints by our layout bounds position to match localPoint!
            Vector2 boundsPos = new Vector2(this.style.left.value.value, this.style.top.value.value);
            Vector2 p0 = customPoints[0] - boundsPos;
            Vector2 p1 = customPoints[1] - boundsPos;
            Vector2 p2 = customPoints[2] - boundsPos;
            Vector2 p3 = customPoints[3] - boundsPos;

            Vector2 prevPoint = p0;
            for (int i = 1; i <= 10; i++)
            {
                float t = i / 10f;
                float u = 1f - t;
                Vector2 p = u*u*u * p0 + 
                            3f*u*u*t * p1 + 
                            3f*u*t*t * p2 + 
                            t*t*t * p3;
                
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
