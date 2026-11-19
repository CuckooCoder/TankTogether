using System;
using UnityEngine;
namespace TankTogether.Networking {
// Isolated, collider-free lobby presentation. Does not move live actors or the tank.
internal sealed class LobbyCharacterPreview : IDisposable {
 GameObject root; public GameObject Model {get;private set;} public RenderTexture Texture {get;private set;}
 public LobbyCharacterPreview(GameObject prefab,int slot){
  root=new GameObject("Lobby presentation P"+(slot+1));root.transform.position=new Vector3(4000+slot*30,3000,3000);
  Model=prefab?UnityEngine.Object.Instantiate(prefab,root.transform,false):GameObject.CreatePrimitive(PrimitiveType.Capsule);
  if(!prefab)Model.transform.SetParent(root.transform,false);
  foreach(var c in Model.GetComponentsInChildren<Collider>(true)){c.enabled=false;UnityEngine.Object.Destroy(c);}
  foreach(var b in Model.GetComponentsInChildren<Rigidbody>(true)){b.isKinematic=true;UnityEngine.Object.Destroy(b);}
  foreach(var behaviour in Model.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
  foreach(var t in Model.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
  Model.transform.localRotation=Quaternion.Euler(0,180,0);
  var renderers=Model.GetComponentsInChildren<Renderer>();Bounds bounds=new Bounds(Model.transform.position,Vector3.zero);foreach(var r in renderers)bounds.Encapsulate(r.bounds);
  var go=new GameObject("Lobby portrait camera");go.transform.SetParent(root.transform,false);var cam=go.AddComponent<Camera>();cam.cullingMask=1<<30;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.045f,.085f,.105f);
  cam.orthographic=true;cam.orthographicSize=Mathf.Max(bounds.extents.y*1.15f,.8f);cam.nearClipPlane=.05f;cam.farClipPlane=15;
  cam.transform.position=bounds.center+new Vector3(0,.12f,-Mathf.Max(bounds.size.y*1.4f,3));cam.transform.LookAt(bounds.center);
  Texture=new RenderTexture(320,240,24);Texture.name="Lobby portrait "+slot;Texture.Create();cam.targetTexture=Texture;
  var lamp=new GameObject("Lobby portrait light");lamp.transform.SetParent(root.transform,false);lamp.transform.position=bounds.center+new Vector3(-2,3,-3);lamp.transform.LookAt(bounds.center);var light=lamp.AddComponent<Light>();light.type=LightType.Point;light.range=10;light.intensity=5;light.cullingMask=1<<30;
 }
 public void Dispose(){if(root){root.SetActive(false);UnityEngine.Object.Destroy(root);}if(Texture){Texture.Release();UnityEngine.Object.Destroy(Texture);}root=Model=null;Texture=null;}
}
}
