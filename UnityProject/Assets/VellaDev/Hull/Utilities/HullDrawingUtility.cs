﻿using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VellaDev.Hull
{
    [Flags]
    public enum DebugHullFlags
    {
        None = 0,
        Unused = 2, 
        EdgeLinks = 4,
        PlaneNormals = 8,
        FaceWinding = 16,
        ExplodeFaces = 32,
        Indices = 64,
        Outline = 128,
        All = ~0,
    }

    #if UNITY_EDITOR
    /// <summary>
    /// Flags enum dropdown GUI for selecting <see cref="DebugHullFlags"/> properties in the inspector
    /// </summary>
    [CustomPropertyDrawer(typeof(DebugHullFlags))]
    public class NavMeshAreasDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            label = EditorGUI.BeginProperty(position, label, property);
            var oldValue = (Enum)fieldInfo.GetValue(property.serializedObject.targetObject);
            var newValue = EditorGUI.EnumFlagsField(position, label, oldValue);
            if (!newValue.Equals(oldValue))
            {
                property.intValue = (int)Convert.ChangeType(newValue, fieldInfo.FieldType);
            }
            EditorGUI.EndProperty();
        }
    }
    #endif

    public class HullDrawingUtility
    {
        public static void DrawBasicHull(NativeHull hull1, RigidTransform t)
        {            
            foreach (var edge in hull1.GetEdges())
            {
                var a = math.transform(t, edge.GetOrigin(hull1));
                var b = math.transform(t, edge.GetTwinOrigin(hull1));                     
                Debug.DrawLine(a, b, Color.black);
            }
        }

        public static void DrawDebugHull(NativeHull hull, RigidTransform t, DebugHullFlags options = DebugHullFlags.All, Color BaseColor = default)
        {
            if (options == DebugHullFlags.None)
                return;

            if (BaseColor == default)
                BaseColor = Color.yellow;

            float faceExplosionDistance = (options & DebugHullFlags.ExplodeFaces) != 0 ? 0.3f : 0;

            // Iterate each twin pair at the same time.
            for (int j = 0; j < hull.EdgeCount; j = j + 2)
            {
                var edge = hull.GetEdge(j);
                var twin = hull.GetEdge(j + 1);

                var edgePlane = edge.Face != -1 ? hull.GetPlane(edge.Face) : new NativePlane();
                var twinPlane = twin.Face != -1 ? hull.GetPlane(twin.Face) : new NativePlane();

                var rotatedEdgeNormal = math.rotate(t, edgePlane.Normal);
                var rotatedTwinNormal = math.rotate(t, twinPlane.Normal);

                var edgeVertex1 = math.transform(t, hull.GetVertex(edge.Origin));
                var twinVertex1 = math.transform(t, hull.GetVertex(twin.Origin));
                var edgeVertex2 = math.transform(t, hull.GetVertex(edge.Origin));
                var twinVertex2 = math.transform(t, hull.GetVertex(twin.Origin));

                if ((options & DebugHullFlags.Outline) != 0)
                {
                    Debug.DrawLine(edgeVertex1 + rotatedEdgeNormal * faceExplosionDistance, twinVertex1 + rotatedEdgeNormal * faceExplosionDistance, BaseColor);
                    Debug.DrawLine(edgeVertex2 + rotatedTwinNormal * faceExplosionDistance, twinVertex2 + rotatedTwinNormal * faceExplosionDistance, BaseColor);
                }

                if ((options & DebugHullFlags.EdgeLinks) != 0)
                {
                    Debug.DrawLine((edgeVertex1 + twinVertex1) / 2 + rotatedEdgeNormal * faceExplosionDistance, (edgeVertex2 + twinVertex2) / 2 + rotatedTwinNormal * faceExplosionDistance, Color.gray);
                }
            }

            if ((options & DebugHullFlags.PlaneNormals) != 0)
            {
                hull.IterateFaces((int index, ref NativePlane plane, ref NativeHalfEdge firstEdge) =>
                {
                    var tPlane = plane.Transform(t);
                    DebugDrawer.DebugArrow(tPlane.Position, tPlane.Rotation * 0.2f, BaseColor);
                });
            }

            if ((options & DebugHullFlags.Indices) != 0)
            {
                var dupeCheck = new HashSet<Vector3>();
                for (int i = 0; i < hull.VertexCount; i++)
                {
                    // Offset the label if multiple verts are on the same position.
                    var v = math.transform(t, hull.GetVertex(i));           
                    var offset = dupeCheck.Contains(v) ? (float3)Vector3.forward * 0.05f : 0;

                    DebugDrawer.DrawLabel(v + offset, i.ToString());
                    dupeCheck.Add(v);
                }
            }
                
            if ((options & DebugHullFlags.FaceWinding) != 0)
            {
                for (int i = 0; i < hull.FaceCount; i++)
                {
                    var face = hull.GetFace(i);
                    var plane = hull.GetPlane(i);
                    var tPlane = t * plane;
                    var edge = hull.GetEdge(face.Edge);
                    var startOrigin = edge.Origin;

                    do
                    {
                        var nextEdge = hull.GetEdge(edge.Next);
                        var startVert = math.transform(t, hull.GetVertex(edge.Origin));
                        var endVert = math.transform(t, hull.GetVertex(nextEdge.Origin));

                        var center = (endVert + startVert) / 2;
                        var dir = math.normalize(endVert - startVert);

                        var insetDir = math.normalize(math.cross(tPlane.Normal, dir));

                        if ((options & DebugHullFlags.ExplodeFaces) != 0)
                        {
                            DebugDrawer.DebugArrow(center + tPlane.Normal * faceExplosionDistance, dir * 0.2f, Color.black);
                        }
                        else
                        {
                            DebugDrawer.DebugArrow(center + tPlane.Normal * faceExplosionDistance + insetDir * 0.1f, dir * 0.2f, Color.black);                            
                        }

                        edge = nextEdge;

                    } while (edge.Origin != startOrigin);
                }
            }
        }

        public static void DrawFaceWithOutline(int faceIndex, RigidTransform t, NativeHull hull, Color fillColor, Color outlineColor)
        {
            var verts = hull.GetVertices(faceIndex).Select(cp => (Vector3)cp).ToArray();
            var tVerts = new List<Vector3>();
            for (int i = 0; i < verts.Length; i++)
            {
                var v = math.transform(t, verts[i]);
                tVerts.Add(v);
                var nextIndex = i + 1 < verts.Length ? i + 1 : 0;
                var next = math.transform(t, verts[nextIndex]);
                Debug.DrawLine(v, next, outlineColor);
            }
            DebugDrawer.DrawAAConvexPolygon(tVerts.ToArray(), fillColor);
        }

        public static void DebugDrawManifold(NativeManifold manifold, Color color = default)
        {
            if (!manifold.IsCreated || manifold.Points.Length == 0)
                return;

            if (color == default)
                color = UnityColors.Blue.ToOpacity(0.3f);

            for (int i = 0; i < manifold.Points.Length; i++)
            {
                var start = manifold.Points[i];
                if (manifold.Points.Length >= 2)
                {
                    var end = i > 0 ? manifold.Points[i - 1] : manifold.Points[manifold.Points.Length - 1];
                    Debug.DrawLine(start.Position, end.Position, color);                    
                }
                DebugDrawer.DrawSphere(start.Position, 0.02f, color.ToOpacity(0.8f));
            }
            DebugDrawer.DrawAAConvexPolygon(manifold.Points.ToArray().Select(cp => (Vector3)cp.Position).ToArray(), color);
        }

    }


}
