using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
namespace TankTogether.Networking
{
    public sealed partial class SteamCarrySession
    {
        const string CosmeticMessage="tt.cosmetic.1";
        readonly Dictionary<ulong,int> cosmetics=new Dictionary<ulong,int>();
        static readonly Color[] HatColors={new Color(.22f,.6f,1),new Color(.75f,.4f,1),new Color(1,.58f,.2f),new Color(.85f,.3f,.57f)};
        bool showCosmetic;GameObject previewRoot,previewModel;Camera previewCamera;RenderTexture previewTexture;
        MaterialPropertyBlock cosmeticProperties;
        readonly Dictionary<GameObject,int> appliedCosmetics=new Dictionary<GameObject,int>();
        void RegisterCosmetics()=>net.CustomMessagingManager.RegisterNamedMessageHandler(CosmeticMessage,(sender,reader)=>{
            if(!Host||roster.Locked||!slots.TryGetValue(sender,out int slot)||reader.Length-reader.Position!=1)return;
            reader.ReadValueSafe(out byte color);if(color>3)return;ApplyLobbyCosmetic(slot,color);
        });
        void ApplyLobbyCosmetic(int slot,int color)
        {
            if(!Host||roster.Locked||slot<0||slot>3||color<0||color>3||!connected[slot])return;
            ulong identity=roster.Identity(slot);int before=cosmetics.TryGetValue(identity,out int value)?value:slot;
            cosmetics[identity]=color;
            if(slot>0&&before!=color)playerReady.SetReady(slot,false,playerReady.Revision);
            nextSnapshot=nextAdventureInfo=0;
        }
        void SendCosmetic()
        {
            if(Host){ApplyLobbyCosmetic(0,PlayerPreferences.Data.cosmetic);}
            else if(Running)Send(CosmeticMessage,0,new[]{(byte)PlayerPreferences.Data.cosmetic},NetworkDelivery.ReliableSequenced);
        }
        void OpenCosmetic()
        {
            if(Running && (Host?roster.Locked:phase!=SteamCarryProtocol.Phase.Waiting))return;
            Capture(false);lab.ReleaseLocalControl();showCosmetic=true;
            if(previewRoot)return;
            previewRoot=new GameObject("Personal character preview");previewRoot.transform.position=new Vector3(3000,3000,3000);
            if(lab.playerVisualPrefab)previewModel=Instantiate(lab.playerVisualPrefab,previewRoot.transform,false);
            else {previewModel=GameObject.CreatePrimitive(PrimitiveType.Capsule);previewModel.transform.SetParent(previewRoot.transform,false);}
            previewModel.transform.Rotate(0,180,0);
            foreach(var c in previewModel.GetComponentsInChildren<Collider>(true))Destroy(c);
            foreach(var b in previewModel.GetComponentsInChildren<Rigidbody>(true))Destroy(b);
            foreach(var t in previewModel.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            var renderers=previewModel.GetComponentsInChildren<Renderer>();Bounds bounds=new Bounds(previewModel.transform.position,Vector3.zero);foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            var cameraObject=new GameObject("Preview camera");cameraObject.transform.SetParent(previewRoot.transform);
            previewCamera=cameraObject.AddComponent<Camera>();previewCamera.cullingMask=1<<31;previewCamera.clearFlags=CameraClearFlags.SolidColor;previewCamera.backgroundColor=new Color(.06f,.1f,.13f);
            previewCamera.transform.position=bounds.center+new Vector3(0,.15f,-Mathf.Max(bounds.size.y*1.25f,2));previewCamera.transform.LookAt(bounds.center);
            previewTexture=new RenderTexture(480,480,24);previewTexture.Create();previewCamera.targetTexture=previewTexture;
            var lightObject=new GameObject("Preview light");lightObject.transform.SetParent(previewRoot.transform);lightObject.transform.rotation=Quaternion.Euler(30,150,0);var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.cullingMask=1<<31;
        }
        void ApplyCosmetic(GameObject target,int color)
        {
            if(!target)return;if(appliedCosmetics.TryGetValue(target,out int prior)&&prior==color)return;appliedCosmetics[target]=color;cosmeticProperties??=new MaterialPropertyBlock();cosmeticProperties.Clear();cosmeticProperties.SetColor("_BaseColor",HatColors[Mathf.Clamp(color,0,3)]);
            var art=target.GetComponent<TankTogether.M0.CarryVisualProfile>();if(art){art.ApplyColor(color);return;}
            foreach(var r in target.GetComponentsInChildren<Renderer>(true))if(r.name.Contains("Cap"))r.SetPropertyBlock(cosmeticProperties,0);
        }
        void RefreshCosmetics()
        {
            if(showCosmetic&&previewModel)ApplyCosmetic(previewModel,PlayerPreferences.Data.cosmetic);
            if(!Running||lab.Players==null)return;
            for(int i=0;i<4;i++){
                int color=Host?(cosmetics.TryGetValue(roster.Identity(i),out int found)?found:i):(adventureInfo?.colors?[i]??i);
                if(lab.Players[i]?.visual)ApplyCosmetic(lab.Players[i].visual.gameObject,color);
            }
        }
        void DrawCosmetic()
        {
            Panel(new Rect(170,80,940,560));GameText.Label(new Rect(200,100,870,40),"个人角色装扮",heading);
            if(previewTexture)GUI.DrawTexture(new Rect(200,155,410,410),previewTexture,ScaleMode.ScaleToFit);
            GameText.Label(new Rect(650,165,420,80),"当前角色 · 配色\n使用现有美术资产，更多装扮待策划确定。",label);
            int choice=GameText.Selection(new Rect(650,290,420,90),PlayerPreferences.Data.cosmetic,new[]{"蓝色","紫色","黄色","珊瑚色"},2,button);
            if(choice!=PlayerPreferences.Data.cosmetic){PlayerPreferences.Data.cosmetic=choice;PlayerPreferences.Save();SendCosmetic();}
            if(GameText.Button(new Rect(650,425,420,42),"向左转",button)&&previewModel)previewModel.transform.Rotate(0,30,0);
            if(GameText.Button(new Rect(650,480,420,42),"向右转",button)&&previewModel)previewModel.transform.Rotate(0,-30,0);
            if(GameText.Button(new Rect(200,580,870,40),"返回",button)){showCosmetic=false;DestroyPreview();}
        }
        void DestroyPreview(){if(previewRoot)Destroy(previewRoot);if(previewTexture){previewTexture.Release();Destroy(previewTexture);}previewRoot=previewModel=null;previewTexture=null;}
    }
}
