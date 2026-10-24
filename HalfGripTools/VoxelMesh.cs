using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HalfGripTools
{
	/// <summary>
	/// Builds the thin extruded "voxel" meshes the game uses for a gun in your hands (and that suit a rocket in flight), from a
	/// sprite's opaque pixels. The sprite is laid along the model's forward axis: its left edge (column 0) is the front, at z = 0,
	/// and columns run back along -z; rows run down along -y, with one chosen row on y = 0; it is thick along x.
	/// </summary>
	public static class VoxelMesh
	{
		public static Mesh Build(Texture2D texture, float pixel, float thickness, float axisRow, string name)
		{
			int w = texture.width;
			int h = texture.height;
			Color32[] pixels = texture.GetPixels32();

			bool Opaque(int px, int py)
			{
				if (px < 0 || py < 0 || px >= w || py >= h)
				{
					return false;
				}
				// Texture rows run bottom to top, sprite rows top to bottom.
				return pixels[(h - 1 - py) * w + px].a > 8;
			}

			float xMax = thickness * 0.5f;
			float xMin = -xMax;
			float Y(float row) => (axisRow - row) * pixel;
			float Z(float col) => -col * pixel;

			var verts = new List<Vector3>();
			var normals = new List<Vector3>();
			var uvs = new List<Vector2>();
			var tris = new List<int>();

			void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
			{
				int i = verts.Count;
				verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
				normals.Add(n); normals.Add(n); normals.Add(n); normals.Add(n);
				uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);
				// Unity treats the clockwise side as the front, so wind each triangle to face along n.
				if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) >= 0f)
				{
					tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
					tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
				}
				else
				{
					tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
					tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
				}
			}

			for (int py = 0; py < h; py++)
			{
				float yt = Y(py);
				float yb = Y(py + 1);
				float vTop = 1f - py / (float)h;
				float vBot = 1f - (py + 1) / (float)h;
				float vMid = 1f - (py + 0.5f) / h;

				int px = 0;
				while (px < w)
				{
					if (!Opaque(px, py))
					{
						px++;
						continue;
					}
					int a = px;
					while (px < w && Opaque(px, py))
					{
						px++;
					}
					int b = px;
					float za = Z(a);
					float zb = Z(b);
					float u0 = a / (float)w;
					float u1 = b / (float)w;

					// Both faces.
					Quad(new Vector3(xMax, yt, za), new Vector3(xMax, yt, zb), new Vector3(xMax, yb, zb), new Vector3(xMax, yb, za),
						Vector3.right, new Vector2(u0, vTop), new Vector2(u1, vTop), new Vector2(u1, vBot), new Vector2(u0, vBot));
					Quad(new Vector3(xMin, yt, za), new Vector3(xMin, yt, zb), new Vector3(xMin, yb, zb), new Vector3(xMin, yb, za),
						Vector3.left, new Vector2(u0, vTop), new Vector2(u1, vTop), new Vector2(u1, vBot), new Vector2(u0, vBot));

					// Ends of the run: the front one faces forward, the rear one back.
					float uA = (a + 0.5f) / w;
					float uB = (b - 0.5f) / w;
					Quad(new Vector3(xMin, yt, za), new Vector3(xMin, yb, za), new Vector3(xMax, yb, za), new Vector3(xMax, yt, za),
						Vector3.forward, new Vector2(uA, vMid), new Vector2(uA, vMid), new Vector2(uA, vMid), new Vector2(uA, vMid));
					Quad(new Vector3(xMin, yt, zb), new Vector3(xMin, yb, zb), new Vector3(xMax, yb, zb), new Vector3(xMax, yt, zb),
						Vector3.back, new Vector2(uB, vMid), new Vector2(uB, vMid), new Vector2(uB, vMid), new Vector2(uB, vMid));

					// Top and bottom edges, merged along the run where the neighbour row is empty.
					for (int pass = 0; pass < 2; pass++)
					{
						int ny = pass == 0 ? py - 1 : py + 1;
						float yEdge = pass == 0 ? yt : yb;
						Vector3 n = pass == 0 ? Vector3.up : Vector3.down;
						int s = a;
						while (s < b)
						{
							if (Opaque(s, ny))
							{
								s++;
								continue;
							}
							int e = s;
							while (e < b && !Opaque(e, ny))
							{
								e++;
							}
							float zs = Z(s);
							float ze = Z(e);
							float us0 = (s + 0.5f) / w;
							float us1 = (e - 0.5f) / w;
							Quad(new Vector3(xMin, yEdge, zs), new Vector3(xMin, yEdge, ze), new Vector3(xMax, yEdge, ze), new Vector3(xMax, yEdge, zs),
								n, new Vector2(us0, vMid), new Vector2(us1, vMid), new Vector2(us1, vMid), new Vector2(us0, vMid));
							s = e;
						}
					}
				}
			}

			var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
			mesh.SetVertices(verts);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uvs);
			mesh.SetTriangles(tris, 0);
			mesh.RecalculateBounds();
			return mesh;
		}

		/// <summary>An object that draws a mesh with the given texture on a copy of <paramref name="like"/> (the game's own gun material, which is known to render).</summary>
		public static GameObject MakeObject(string name, Mesh mesh, Texture2D texture, Material like, Transform parent, int layer)
		{
			var go = new GameObject(name);
			go.layer = layer;
			go.transform.SetParent(parent, false);
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			MeshRenderer renderer = go.AddComponent<MeshRenderer>();
			Shader plain = Shader.Find("Sprites/Default");
			Material material = like != null ? new Material(like) : plain != null ? new Material(plain) : null;
			if (material == null)
			{
				Plugin.Log.LogWarning("No material to draw " + name + " with");
				return go;
			}
			material.name = name;
			material.hideFlags = HideFlags.HideAndDontSave;
			material.SetTexture("_BaseMap", texture);
			material.SetTexture("_MainTex", texture);
			renderer.sharedMaterial = material;
			return go;
		}
	}
}
