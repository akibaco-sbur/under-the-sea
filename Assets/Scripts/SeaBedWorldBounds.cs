using System;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class SeaBedWorldBounds
{
    internal static bool TryGet(Scene scene,Transform seaBed,out Bounds bounds)
    {
        bounds=default;
        if(!seaBed)
            foreach(var candidate in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if(candidate.gameObject.scene==scene && string.Equals(candidate.name,"Seabed",StringComparison.OrdinalIgnoreCase))
                {seaBed=candidate;break;}
        if(!seaBed)return false;
        bool found=false;
        foreach(var renderer in seaBed.GetComponentsInChildren<Renderer>())
        {
            if(renderer is ParticleSystemRenderer)continue;
            if(!found){bounds=renderer.bounds;found=true;}else bounds.Encapsulate(renderer.bounds);
        }
        foreach(var terrain in seaBed.GetComponentsInChildren<Terrain>())
        {
            if(!terrain.terrainData)continue;
            var local=terrain.terrainData.bounds;
            for(int i=0;i<8;i++)
            {
                var corner=terrain.transform.TransformPoint(new Vector3((i&1)==0?local.min.x:local.max.x,(i&2)==0?local.min.y:local.max.y,(i&4)==0?local.min.z:local.max.z));
                if(!found){bounds=new Bounds(corner,Vector3.zero);found=true;}else bounds.Encapsulate(corner);
            }
        }
        if(!found)
            foreach(var collider in seaBed.GetComponentsInChildren<Collider>())
            {if(!found){bounds=collider.bounds;found=true;}else bounds.Encapsulate(collider.bounds);}
        return found && bounds.size.x>.01f && bounds.size.z>.01f;
    }
}
