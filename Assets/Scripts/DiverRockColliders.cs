using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Only fills missing static rock collision at runtime; never edits imported models or the scene asset.</summary>
internal static class DiverRockColliders
{
    internal static void Ensure(Scene scene)
    {
        int added=0;
        foreach(var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
        {
            if(filter.gameObject.scene!=scene || !filter.sharedMesh)continue;
            bool rock=false;
            for(var t=filter.transform;t;t=t.parent)
                if(t.name.StartsWith("MossRock",StringComparison.OrdinalIgnoreCase) ||
                   t.name.StartsWith("Rock",StringComparison.OrdinalIgnoreCase)){rock=true;break;}
            if(!rock)continue;
            bool solid=false;
            foreach(var c in filter.GetComponentsInParent<Collider>())
                if(c.enabled && !c.isTrigger){solid=true;break;}
            if(solid)continue;
            var body=filter.GetComponentInParent<Rigidbody>();
            if(body && !body.isKinematic)
            {
                var box=filter.gameObject.AddComponent<BoxCollider>();
                box.center=filter.sharedMesh.bounds.center;box.size=filter.sharedMesh.bounds.size;
            }
            else
            {
                var collider=filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh=filter.sharedMesh;
            }
            added++;
        }
        if(added>0)Debug.Log("[DiverMovement] Added runtime collision to "+added+" Rock/MossRock mesh(es).");
    }
}
