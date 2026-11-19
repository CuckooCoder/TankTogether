using System;
using System.Collections.Generic;
using UnityEngine;

namespace TankTogether.M0
{
    // Cosmetic only: no colliders, rigidbodies, network objects or gameplay writes.
    public sealed class TankFailureFeedback : MonoBehaviour
    {
        sealed class Shard { public Transform view; public Vector3 start, velocity, spin; public Quaternion rotation; public float scale; }
        readonly List<Shard> shards = new List<Shard>();
        Renderer[] intact = Array.Empty<Renderer>();
        GameObject root;
        Mesh mesh;
        Material material;
        AudioClip clip;
        AudioSource source;
        float began;
        public int BurstCount { get; private set; }

        public void Show(Rigidbody tank, Vector3 size, Shader shader)
        {
            Clear();
            if (!tank) return;
            BurstCount++;
            intact = tank.GetComponentsInChildren<Renderer>();
            foreach (var renderer in intact) renderer.enabled = false;
            if (!material) {
                material = new Material(shader);
                material.SetColor("_BaseColor", new Color(.4f,.88f,.94f));
                material.SetFloat("_Smoothness", .85f);
            }
            if (!mesh) {
                mesh = new Mesh { name = "Cosmetic glass triangle" };
                mesh.vertices = new[] { new Vector3(-.5f,-.4f,0), new Vector3(.5f,-.3f,0), new Vector3(.1f,.6f,0) };
                mesh.triangles = new[] {0,1,2,2,1,0};
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
            }
            root = new GameObject("Cosmetic glass burst");
            root.transform.SetParent(transform, false);
            var random = new System.Random(521);
            float Unit() => (float)random.NextDouble();
            for (int i=0;i<44;i++) {
                var local = new Vector3((Unit()-.5f)*size.x, (Unit()-.5f)*size.y, (Unit()-.5f)*size.z);
                if(i%2==0) local.x=(i%4==0?-.5f:.5f)*size.x;
                else local.z=(i%4==1?-.5f:.5f)*size.z;
                var go=new GameObject("Glass shard");
                go.transform.SetParent(root.transform,false);
                go.AddComponent<MeshFilter>().sharedMesh=mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial=material;
                var pos=tank.transform.TransformPoint(local);
                var rotation=UnityEngine.Quaternion.Euler(Unit()*360,Unit()*360,Unit()*360);
                float scale=.15f+Unit()*.25f;
                go.transform.SetPositionAndRotation(pos,rotation);
                go.transform.localScale=Vector3.one*scale;
                shards.Add(new Shard {view=go.transform,start=pos,rotation=rotation,scale=scale,
                    velocity=tank.rotation*new Vector3(local.x*.9f,1+Unit()*1.4f,local.z*.9f),
                    spin=new Vector3(Unit()*360,Unit()*360,Unit()*360)});
            }
            began=Time.unscaledTime;
            if (!source) {source=gameObject.AddComponent<AudioSource>();source.playOnAwake=false;source.spatialBlend=0;source.volume=.22f;}
            if (!clip) {
                const int rate=22050;
                var samples=new float[rate/2];
                float previous=0;
                for(int i=0;i<samples.Length;i++) {
                    float t=(float)i/rate, noise=Unit()*2-1;
                    float crack=(noise-previous)*.24f*Mathf.Exp(-t*18);
                    previous=noise;
                    float ring=(Mathf.Sin(t*18850)+Mathf.Sin(t*27960)*.5f)*.16f*Mathf.Exp(-t*11);
                    samples[i]=Mathf.Clamp(crack+ring,-.8f,.8f);
                }
                clip=AudioClip.Create("Prototype glass crack",samples.Length,1,rate,false);
                clip.SetData(samples,0);
            }
            source.PlayOneShot(clip);
        }
        void Update()
        {
            if(!root) return;
            float age=Time.unscaledTime-began;
            if(age>=1.3f) {Destroy(root);root=null;shards.Clear();return;}
            foreach(var shard in shards) {
                shard.view.position=shard.start+shard.velocity*age+Vector3.down*(3.5f*age*age);
                shard.view.rotation=shard.rotation*Quaternion.Euler(shard.spin*age);
                shard.view.localScale=Vector3.one*shard.scale*Mathf.Clamp01((1.3f-age)/.35f);
            }
        }
        public void Clear()
        {
            foreach(var renderer in intact) if(renderer) renderer.enabled=true;
            intact=Array.Empty<Renderer>();
            if(root) {root.SetActive(false);Destroy(root);root=null;}
            shards.Clear();
            if(source) source.Stop();
        }
        void OnDestroy()
        {
            Clear();
            if(mesh) Destroy(mesh);
            if(material) Destroy(material);
            if(clip) Destroy(clip);
        }
    }
}
