using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

/// <summary>Four visual walls and an independent horizontal movement boundary.</summary>
[DisallowMultipleComponent]
public sealed class SeaBedWorldBorder : MonoBehaviour
{
    [SerializeField, InspectorName("範囲のSeaBed（省略可）")] Transform seaBed;
    [SerializeField, Min(.1f), InspectorName("端から内側の余白（m）")] float edgeInset=.8f;
    [SerializeField, Min(10f), InspectorName("壁の上下への高さ（m）")] float wallHeight=300f;
    [SerializeField, Min(0f), InspectorName("壁を上に追加で伸ばす高さ（m）")] float wallTopExtension=1200f;
    [SerializeField, Min(.1f), InspectorName("壁が最も濃くなる距離（m）")] float nearDistance=5f;
    [SerializeField, Min(1f), InspectorName("壁が薄くなる距離（m）")] float farDistance=50f;
    [SerializeField, Range(0,.3f), InspectorName("遠距離の濃さ")] float farOpacity=.045f;
    [SerializeField, InspectorName("境界の色")] Color borderColor=new Color(1f,.8f,.05f,1);
    Bounds bounds;
    Mesh wallMesh;
    Material material;
    GameObject walls;
    float nextFind;
    bool reportedMissing;
    public bool IsReady {get;private set;}

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded-=OnLoaded;SceneManager.sceneLoaded+=OnLoaded;
        for(int i=0;i<SceneManager.sceneCount;i++)Ensure(SceneManager.GetSceneAt(i));
    }
    static void OnLoaded(Scene scene,LoadSceneMode mode)=>Ensure(scene);
    public static SeaBedWorldBorder Ensure(Scene scene)
    {
        if(scene.name!="Main" || !scene.isLoaded)return null;
        foreach(var border in FindObjectsByType<SeaBedWorldBorder>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            if(border.gameObject.scene==scene)return border;
        var root=new GameObject("SeaBedWorldBorder");SceneManager.MoveGameObjectToScene(root,scene);
        return root.AddComponent<SeaBedWorldBorder>();
    }
    void Start()=>Initialize();
    void Update()
    {
        if(!IsReady && Time.unscaledTime>=nextFind){nextFind=Time.unscaledTime+1;Initialize();}
        if(material)
        {
            material.SetColor("_BorderColor",borderColor);
            material.SetFloat("_NearDistance",Mathf.Max(.1f,nearDistance));
            material.SetFloat("_FarDistance",Mathf.Max(nearDistance+.1f,farDistance));
            material.SetFloat("_FarOpacity",farOpacity);
        }
    }
    void Initialize()
    {
        if(IsReady)return;
        if(!SeaBedWorldBounds.TryGet(gameObject.scene,seaBed,out bounds))
        {
            if(!reportedMissing){Debug.LogWarning("[WorldBorder] SeaBed bounds unavailable. Assign SeaBed on SeaBedWorldBorder.",this);reportedMissing=true;}
            return;
        }
        IsReady=true;
        BuildWalls();
        Debug.Log("[WorldBorder] SeaBed boundary enabled: "+bounds,this);
    }
    public Vector3 Constrain(Vector3 position)
    {
        if(!IsReady)return position;
        float inset=Mathf.Min(Mathf.Max(.1f,edgeInset),Mathf.Min(bounds.extents.x,bounds.extents.z)*.45f);
        position.x=DiverWorldBorderMath.Clamp(position.x,bounds.min.x+inset,bounds.max.x-inset);
        position.z=DiverWorldBorderMath.Clamp(position.z,bounds.min.z+inset,bounds.max.z-inset);
        return position;
    }
    public float DistanceToEdge(Vector3 position,Vector3 direction)
    {
        if(!IsReady)return float.PositiveInfinity;
        float inset=Mathf.Min(Mathf.Max(.1f,edgeInset),Mathf.Min(bounds.extents.x,bounds.extents.z)*.45f);
        return DiverWorldBorderMath.Distance(position.x,position.z,direction.x,direction.z,bounds.min.x+inset,bounds.max.x-inset,bounds.min.z+inset,bounds.max.z-inset);
    }
    public float NearestEdgeDistance(Vector3 position)
    {
        if(!IsReady)return float.PositiveInfinity;
        return Mathf.Max(0,Mathf.Min(position.x-bounds.min.x,bounds.max.x-position.x,position.z-bounds.min.z,bounds.max.z-position.z));
    }
    void BuildWalls()
    {
        var shader=Resources.Load<Shader>("Shaders/SeaBedWorldBorder");
        if(!shader){Debug.LogError("[WorldBorder] Resources/Shaders/SeaBedWorldBorder.shader missing. Movement boundary remains enabled.",this);return;}
        material=new Material(shader){name="RuntimeSeaBedBorder"};
        material.SetColor("_BorderColor",borderColor);material.SetFloat("_NearDistance",nearDistance);
        material.SetFloat("_FarDistance",Mathf.Max(nearDistance+.1f,farDistance));material.SetFloat("_FarOpacity",farOpacity);
        float bottom=bounds.min.y-wallHeight, top=bounds.max.y+wallHeight+Mathf.Max(0,wallTopExtension);
        var min=bounds.min;var max=bounds.max;
        wallMesh=new Mesh{name="SeaBedBorderWalls"};
        wallMesh.vertices=new[]{new Vector3(min.x,bottom,min.z),new Vector3(max.x,bottom,min.z),new Vector3(max.x,top,min.z),new Vector3(min.x,top,min.z),
            new Vector3(max.x,bottom,min.z),new Vector3(max.x,bottom,max.z),new Vector3(max.x,top,max.z),new Vector3(max.x,top,min.z),
            new Vector3(max.x,bottom,max.z),new Vector3(min.x,bottom,max.z),new Vector3(min.x,top,max.z),new Vector3(max.x,top,max.z),
            new Vector3(min.x,bottom,max.z),new Vector3(min.x,bottom,min.z),new Vector3(min.x,top,min.z),new Vector3(min.x,top,max.z)};
        var vertices=wallMesh.vertices;
        for(int i=0;i<vertices.Length;i++)vertices[i]=transform.InverseTransformPoint(vertices[i]);
        wallMesh.vertices=vertices;
        wallMesh.triangles=new[]{0,1,2,0,2,3,4,5,6,4,6,7,8,9,10,8,10,11,12,13,14,12,14,15};wallMesh.RecalculateNormals();wallMesh.RecalculateBounds();
        walls=new GameObject("BoundaryWalls",typeof(MeshFilter),typeof(MeshRenderer));walls.transform.SetParent(transform,false);
        walls.GetComponent<MeshFilter>().sharedMesh=wallMesh;
        var renderer=walls.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
    }
    void OnDisable(){if(walls)walls.SetActive(false);}
    void OnEnable(){if(walls)walls.SetActive(true);}
    void OnDestroy(){if(wallMesh)Destroy(wallMesh);if(material)Destroy(material);}
}
