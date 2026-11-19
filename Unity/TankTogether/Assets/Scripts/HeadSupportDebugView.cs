using UnityEngine;
using UnityEngine.Rendering;
namespace TankTogether.M0
{
    // View-only overlay. No colliders, rigidbodies or forces; never part of network configuration.
    [DefaultExecutionOrder(500)]
    public sealed class HeadSupportDebugView : MonoBehaviour
    {
        M0CarryLab lab;
        readonly GameObject[] roots = new GameObject[4];
        readonly Transform[] owners = new Transform[4];
        readonly MeshRenderer[] fills = new MeshRenderer[4];
        readonly LineRenderer[] outlines = new LineRenderer[4];
        readonly Vector3[] corners = new Vector3[4];
        Material fillMaterial, edgeMaterial;
        Mesh mesh;
        MaterialPropertyBlock properties;
        void Awake()
        {
            lab=GetComponent<M0CarryLab>(); properties=new MaterialPropertyBlock();
            var shader=Resources.Load<Shader>("HeadSupportDebug");
            if(!shader){enabled=false;return;}
            fillMaterial=new Material(shader){name="Head support debug fill",renderQueue=4000};
            edgeMaterial=new Material(shader){name="Head support debug outline",renderQueue=4001};
            mesh=new Mesh{name="Head support debug square"};
            mesh.vertices=new[]{new Vector3(-.5f,0,-.5f),new Vector3(.5f,0,-.5f),new Vector3(.5f,0,.5f),new Vector3(-.5f,0,.5f)};
            mesh.triangles=new[]{0,2,1,0,3,2};mesh.RecalculateBounds();
        }
        void Create(int index, Transform owner)
        {
            if(roots[index])Destroy(roots[index]);
            owners[index]=owner;
            var root=new GameObject("Head support DEBUG (no collision)");root.transform.SetParent(owner,false);roots[index]=root;
            var face=new GameObject("Finite support plane");face.transform.SetParent(root.transform,false);
            face.AddComponent<MeshFilter>().sharedMesh=mesh;
            fills[index]=face.AddComponent<MeshRenderer>();fills[index].sharedMaterial=fillMaterial;
            fills[index].shadowCastingMode=ShadowCastingMode.Off;fills[index].receiveShadows=false;
            var line=root.AddComponent<LineRenderer>();outlines[index]=line;line.sharedMaterial=edgeMaterial;
            line.useWorldSpace=false;line.loop=true;line.positionCount=4;line.widthMultiplier=.012f;
            line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;
        }
        void LateUpdate()
        {
            if(!lab||!mesh)return;
            for(int i=0;i<4;i++)
            {
                var a=lab.Players[i];
                bool show=lab.ShowHeadSupport&&lab.baseline&&lab.baseline.headSupportMode>=.5f&&a!=null&&a.active&&a.body&&a.body.gameObject.activeInHierarchy;
                if(!show){if(roots[i])roots[i].SetActive(false);continue;}
                if(!roots[i]||owners[i]!=a.body.transform)Create(i,a.body.transform);
                roots[i].SetActive(true);
                // Match the interpolated avatar, while retaining the solver's world-aligned footprint.
                var head=a.body.transform.position+Vector3.up*a.height;
                roots[i].transform.SetPositionAndRotation(head,Quaternion.identity);
                float width=lab.baseline.playerDiameter*Mathf.Clamp(lab.baseline.headSupportWidthRatio,.3f,.7f);
                fills[i].transform.localScale=new Vector3(width,1,width);
                // Perimeter order differs from the solver's binary corner indexing.
                for(int n=0;n<4;n++)corners[n]=FlatHeadSupport.Point(Vector3.zero,lab.baseline.playerDiameter,lab.baseline.headSupportWidthRatio,n==2?3:n==3?2:n);
                outlines[i].SetPositions(corners);
                Color color=a.joint||a.remoteGrip?new Color(.2f,1,.45f):a.support?new Color(1,.75f,.15f):new Color(.15f,.85f,1);
                color.a=.22f;properties.SetColor("_Color",color);fills[i].SetPropertyBlock(properties);
                color.a=1;properties.SetColor("_Color",color);outlines[i].SetPropertyBlock(properties);
            }
        }
        void OnDisable(){foreach(var root in roots)if(root)root.SetActive(false);}
        void OnDestroy(){foreach(var root in roots)if(root)Destroy(root);if(mesh)Destroy(mesh);if(fillMaterial)Destroy(fillMaterial);if(edgeMaterial)Destroy(edgeMaterial);}
    }
}
