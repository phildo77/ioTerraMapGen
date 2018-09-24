﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Random = System.Random;

namespace ioTerraMapGen
{
    using ioDelaunay;

    public partial class TerraMap
    {
        
        public class TerraMesh
        {
            
            public const int SITE_IDX_NULL = -1;
            public TerraMap Host;
            public Settings settings => Host.settings;
            public Rect Bounds => settings.Bounds;
            private Random m_Rnd => Host.m_Rnd;
            public Vector3[] SitePos;
            public Vector2[] CornerPos;
            public int[][] SiteNbrs;
            public int[][] SiteCrns;
            public HashSet<int>[] SitesHavingCorner;
            public readonly int[] Triangles;
    
            public int[] TrianglesCCW
            {
                get
                {
                    var triCCW = new int[Triangles.Length];
                    for (int tIdx = 0; tIdx < Triangles.Length; tIdx += 3)
                    {
                        triCCW[tIdx] = Triangles[tIdx];
                        triCCW[tIdx + 1] = Triangles[tIdx + 2];
                        triCCW[tIdx + 2] = Triangles[tIdx + 1];
    
                    }
    
                    return triCCW;
                }
            }
            public int[] HullSites;
    
    
            public Vector3[] ElevatedVerts()
            {
                return ElevatedVerts(SitePos);
            }
            public Vector3[] ElevatedVerts(Vector3[] _sitePoss)
            {
                var cornZs = new float[CornerPos.Length];
    
                    
                    
                for (int cIdx = 0; cIdx < CornerPos.Length; ++cIdx)
                {
                    var sPoss = new List<Vector3>();
                    foreach (var sIdx in SitesHavingCorner[cIdx])
                    {
                            
                        sPoss.Add(_sitePoss[sIdx]);
                        if(HullSites.Contains(sIdx))
                            Trace.WriteLine("Debug"); //TODO Debug
                    }
                    cornZs[cIdx] = sPoss.Average(_sPos => _sPos.z);
                }
    
                return cornZs.Select((_z, _idx) => new Vector3(CornerPos[_idx].x, CornerPos[_idx].y, _z)).ToArray();
            }
            
            public TerraMesh(TerraMap _hostMap)
            {
                Host = _hostMap;
    
                var xSize = Bounds.width;
                var ySize = Bounds.height;
                var xSpanCount = xSize * settings.Resolution;
                var ySpanCount = ySize * settings.Resolution;
                int pntCnt = (int)(xSpanCount * ySpanCount);
    
    
    
                var points = new List<Vector2>(pntCnt);
    
                for (int pIdx = 0; pIdx < pntCnt; ++pIdx)
                    points.Add(RndVec2(Bounds));
                
                var del = Delaunay.Create<CircleSweep>(points);
                del.Triangulate();
                var vor = new Voronoi(del);
                vor.Build();
                vor.TrimSitesToBndry(Bounds);
                vor.LloydRelax(Bounds);
    
                var dMesh = del.Mesh;
    
                CornerPos = dMesh.Vertices;
                Triangles = dMesh.Triangles;
                
                //Index Delaunay triangles
                var triRef = new Dictionary<Delaunay.Triangle, int>();
                var tris = new List<Delaunay.Triangle>();
                var triScan = del.LastTri;
                
                var tsCnt = 0;
                while (triScan != null)
                {
                    tris.Add(triScan);
                    triRef.Add(triScan, tsCnt++);
                    triScan = triScan.PrevTri;
                }
                    
                
                //Fill Neighbors
                triScan = del.LastTri;
                SitePos = new Vector3[tsCnt];
                SiteCrns = new int[tsCnt][];
                SiteNbrs = new int[tsCnt][];
                SitesHavingCorner = new HashSet<int>[CornerPos.Length];
                while (triScan != null)
                {
                    var tIdx = triRef[triScan];
                    var triVertPoss = new[]
                    {
                        triScan.Edge0.OriginPos, 
                        triScan.Edge1.OriginPos,
                        triScan.Edge2.OriginPos
                    };
                    var tCent = Geom.CentroidOfPoly(triVertPoss); //Use centroid instead of CircCent
                    SitePos[tIdx] = new Vector3(tCent.x, tCent.y, 0.5f);
                    SiteCrns[tIdx] = new int[3];
                    SiteNbrs[tIdx] = new int[3];
                    //Record neighbors
                    var edges = new[] {triScan.Edge0, triScan.Edge1, triScan.Edge2};
                    for(int eIdx = 0; eIdx < 3; ++eIdx)
                    {
                        var edge = edges[eIdx];
                        var oIdx = edge.OriginIdx;
                        if(SitesHavingCorner[oIdx] == null)
                            SitesHavingCorner[oIdx] = new HashSet<int>();
                        SitesHavingCorner[oIdx].Add(tIdx);
                        SiteCrns[tIdx][eIdx] = oIdx;
                        
                        if (edge.Twin != null)
                            SiteNbrs[tIdx][eIdx] = triRef[edge.Twin.Triangle];
                        else
                            SiteNbrs[tIdx][eIdx] = SITE_IDX_NULL;
                    }
                    triScan = triScan.PrevTri;
                }
                
                //Outer hull
                var hullSites = new List<int>();
                foreach (var edge in del.HullEdges)
                    hullSites.Add(triRef[edge.Triangle]);
                    
                HullSites = hullSites.ToArray();
            }
            
            public void SlopeGlobal(Vector2 _dir, float _strength)
            {
                var dir = _dir.normalized;
                Func<Vector3, float> strf = _pos =>
                {
                    var xPct = (_pos.x - Bounds.xMin) / (Bounds.width) - 0.5f;
                    var yPct = (_pos.y - Bounds.yMin) / Bounds.height - 0.5f;
                    var xStr = xPct * dir.x;
                    var yStr = yPct * dir.y;
                    return (xStr + yStr) * _strength / 4f;
                };
    
                for (int sIdx = 0; sIdx < SitePos.Length; ++sIdx)
                {
                    if (HullSites.Contains(sIdx))
                        Trace.Write("Debug"); //TODO DEbug
                    var sitePos = SitePos[sIdx];
                    var zShift = strf(sitePos);
                    var newZ = sitePos.z + zShift;
                    SitePos[sIdx].z = newZ;
                }
            }
            
            public void Conify(bool _inverted, float _strength)
            {
                var cent = Bounds.center;
                var maxMag = (Bounds.min - cent).magnitude;
                var dir = _inverted ? -1f : 1f;
                Func<Vector2, float> zAdder = _pos =>
                {
                    var magScal = (_pos - cent).magnitude / maxMag  - 0.5f;
                    return magScal * _strength / 2f * dir;
                };
                
                for (int sIdx = 0; sIdx < SitePos.Length; ++sIdx)
                {
                    var sPos = SitePos[sIdx];
                    var zShift = zAdder(new Vector2(sPos.x, sPos.y));
                    SitePos[sIdx].z = SitePos[sIdx].z + zShift;
                }
            }
            
            public void Blob(float _strength, float _radius, Vector2? _loc = null)
            {
                if (_loc == null)
                    _loc = RndVec2(Bounds);
    
                var loc = _loc.Value;
                
                for (int sIdx = 0; sIdx < SitePos.Length; ++sIdx)
                {
                    var sPos = SitePos[sIdx];
                    var vert2d = ToVec2(sPos);
                    var dist = (vert2d - loc).magnitude;
                    if (dist > _radius) continue;
                    var cosVal = dist / _radius * (float)Math.PI / 2f;
                    var zShift = _strength * (float)Math.Cos(cosVal);
                    SitePos[sIdx].Set(vert2d.x, vert2d.y, sPos.z + zShift);
                }
            }
            
            public Vector3[] PlanchonDarboux()
            {
                //Generate waterflow surface points
                var newSurf = new Vector3[SitePos.Length];
                for (int pIdx = 0; pIdx < SitePos.Length; ++pIdx)
                {
                    var sPos = SitePos[pIdx];
                    var z = float.PositiveInfinity;
                    if (HullSites.Contains(pIdx))
                        z = sPos.z;
                    newSurf[pIdx] = new Vector3(sPos.x, sPos.y, z);
                }
                
                Func<int, float> Z = _idx => SitePos[_idx].z;
                Func<int, float> W = _idx => newSurf[_idx].z;
                Func<int, int, float> E = (_cIdx, _nIdx) =>
                {
                    var cVert = SitePos[_cIdx];
                    var nVert = SitePos[_nIdx];
                    var subX = nVert.x - cVert.x;
                    var subY = nVert.y - cVert.y;
                    return (float) Math.Sqrt(subX * subX + subY * subY) * settings.MinPDSlope;
                };
                
                var opDone = false;
                do
                {
                    opDone = false;
                    for (int pIdx = 0; pIdx < SitePos.Length; ++pIdx)
                    {
                        if (HullSites.Contains(pIdx)) continue;
                        var sitePos = SitePos[pIdx];
                        var c = pIdx;
                        if (!(W(c) > Z(c))) continue;
                        var cVertZ = sitePos;
                        foreach (var n in SiteNbrs[pIdx])
                        {
                            var e = E(c, n);
                            var wpn = W(n) + e;
                            if (cVertZ.z >= wpn)
                            {
                                newSurf[c].Set(cVertZ.x, cVertZ.y, cVertZ.z);
                                opDone = true;
                                break;
                            }
                            if(W(c) > wpn)
                            {
                                newSurf[c].Set(cVertZ.x, cVertZ.y, wpn);
                                opDone = true;
                            }
                        }
                    }
    
                } while (opDone);
                    
    
                return newSurf;
    
            }
    
            public void Erode()
            {
                /*
                var pd = PlanchonDarboux();
                
                ioTerraMapGen.Vector3[] flowDir;
                float[] slopes;
                var flux = CalcWaterFlux(pd, tgt.Rainfall, out flowDir);
                var slopeVecs = tm.GetSlopeVecs(tm.SitePos, out slopes);
                for (int pIdx = 0; pIdx < tm.SitePos.Length; ++pIdx)
                {
                    var vert = tm.SitePos[pIdx];
                    var fx = Mathf.Sqrt(flux[pIdx]);
                    var slp = slopes[pIdx];
                    var ero = Mathf.Min(-slp * fx, tgt.MaxEroRate);
                    var newZ = vert.z - ero;
                    
                    tm.SitePos[pIdx].Set(vert.x, vert.y, newZ);
                }
                
                
                LRTerra();
    
                tgt.Waterways = flowDir.Select(_v => new Vector3(_v.x, _v.y, _v.z)).ToArray();
    
                var minFlux = float.PositiveInfinity;
                var maxFlux = float.NegativeInfinity;
                for (int fIdx = 0; fIdx < flux.Length; ++fIdx)
                {
                    var f = flux[fIdx];
                    if (f < minFlux)
                        minFlux = f;
                    if (f > maxFlux)
                        maxFlux = f;
                }
                */
            }
            
            public float[] CalcWaterFlux(Vector3[] _waterSurface, float _rainfall, out Vector3[] _flowDir)
            {
                var pIdxByHt = new int[SitePos.Length];
                for (var pIdx = 0; pIdx < SitePos.Length; ++pIdx)
                    pIdxByHt[pIdx] = pIdx;
                Array.Sort(pIdxByHt, (_a, _b) => _waterSurface[_b].z.CompareTo(_waterSurface[_a].z));
                
                
                _flowDir = new Vector3[SitePos.Length];
                
                var flux = new float[SitePos.Length];
                for (int hIdx = 0; hIdx < SitePos.Length; ++hIdx)
                {
                    var pIdx = pIdxByHt[hIdx];
                    var w = _waterSurface[pIdx];
                    flux[pIdx] += _rainfall;
                    
                    //Find downhill
                    var minNIdx = -1;
                    var maxNSlp = 0f;
                    foreach (var nIdx in SiteNbrs[pIdx])
                    {
                        if (nIdx == SITE_IDX_NULL) continue;
                        var n = _waterSurface[nIdx];
                        
                        if (n.z <= w.z)
                        {
                            var vec = n - w;
                            var run = (float) Math.Sqrt(vec.x * vec.x + vec.y * vec.y);
                            var rise = w.z - n.z;
                            var slp = rise / run;
                            if (slp > maxNSlp)
                            {
                                minNIdx = nIdx;
                                maxNSlp = slp;
                            }
                        }
                    }
    
                    if (minNIdx == -1) //TODO DEBUG should never happen?
                        continue;
                    _flowDir[pIdx] = _waterSurface[minNIdx] - w;
                    flux[minNIdx] += flux[pIdx];
                }
    
                return flux;
            }
            
            public Vector3[] GetSlopeVecs(Vector3[] _surf, out float[] _slopes)
            {
                var slopeVecs = new Vector3[_surf.Length];
                _slopes = new float[_surf.Length];
                
                for (int pIdx = 0; pIdx < _surf.Length; ++pIdx)
                {
                    var p = _surf[pIdx];
                    var aveSlp = Vector3.zero;
                    var nbrCnt = 0;
                    foreach (var nIdx in SiteNbrs[pIdx])
                    {
                        if (nIdx == SITE_IDX_NULL) continue;
                        nbrCnt++;
                        var n = _surf[nIdx];
                        var slpVec = n - p;
                        if (slpVec.z > 0)
                            slpVec = -slpVec;
                        aveSlp += slpVec;
                    }
    
                    var slpVecCell = aveSlp / nbrCnt;
                    slopeVecs[pIdx] = slpVecCell;
                    _slopes[pIdx] = slpVecCell.z / new Vector2(slpVecCell.x, slpVecCell.y).magnitude;
                }
    
                return slopeVecs;
            }
            
            private Vector2 RndVec2(Rect _bounds)
            {
                var xSize = _bounds.width;
                var ySize = _bounds.height;
                var x = (float) (m_Rnd.NextDouble() * xSize) + _bounds.min.x;
                var y = (float)(m_Rnd.NextDouble() * ySize) + _bounds.min.y;
                return new Vector2(x, y);
            }
            
            public void NormalizeHeight()
            {
                SetHeightSpan(0, 1);
            }
    
            public void SetHeightSpan(float _min, float _max)
            {
                float minZ, maxZ;
                var zSpan = GetZSpan(out minZ, out maxZ);
    
                var newSpan = _max - _min;
    
    
                for (int sIdx = 0; sIdx < SitePos.Length; ++sIdx)
                {
                    var sPos = SitePos[sIdx];
                    var zPct = (sPos.z - minZ) / zSpan;
                    SitePos[sIdx].Set(sPos.x, sPos.y, (zPct * newSpan) + _min);
                }
            }
            
            public float GetZSpan()
            {
                float zmin, zmax;
                return GetZSpan(out zmin, out zmax);
            }
            public float GetZSpan(out float _zMin, out float _zMax)
            {
                _zMin = float.PositiveInfinity;
                _zMax = float.NegativeInfinity;
                for (int sIdx = 0; sIdx < SitePos.Length; ++sIdx)
                {
                    var z = SitePos[sIdx].z;
                    if (z < _zMin) 
                        _zMin = z;
                    if (z > _zMax)
                        _zMax = z;
                }
    
                return _zMax - _zMin;
    
            }
            
            private Vector2 ToVec2(Vector3 _vec)
            {
                return new Vector2(_vec.x, _vec.y);
            }
        }
    
    }
    

}