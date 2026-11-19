using System.Collections.Generic;
using Steamworks;
using UnityEngine;
namespace TankTogether.Networking
{
    public sealed partial class SteamCarrySession
    {
        readonly Dictionary<ulong,Texture2D> avatars=new Dictionary<ulong,Texture2D>();
        readonly HashSet<ulong> requestedAvatars=new HashSet<ulong>();
        async void RequestAvatar(ulong id)
        {
            if(id==0||!SteamClient.IsValid||!requestedAvatars.Add(id))return;
            try{
                var image=await SteamFriends.GetSmallAvatarAsync(id);
                if(!this||quitting||!image.HasValue)return;
                var value=image.Value;var texture=new Texture2D((int)value.Width,(int)value.Height,TextureFormat.RGBA32,false);
                texture.LoadRawTextureData(value.Data);texture.Apply();avatars[id]=texture;
            }catch(System.Exception){/* Nickname/slot fallback remains visible. */}
        }
        void DrawAvatar(int slot,Rect rect)
        {
            ulong id=Host?roster.Identity(slot):0;
            if(!Host&&adventureInfo?.identities!=null)ulong.TryParse(adventureInfo.identities[slot],out id);
            RequestAvatar(id);
            if(avatars.TryGetValue(id,out var image)&&image)GUI.DrawTexture(rect,image);
        }
        void DestroyAvatars(){foreach(var t in avatars.Values)if(t)Destroy(t);avatars.Clear();requestedAvatars.Clear();}
    }
}
