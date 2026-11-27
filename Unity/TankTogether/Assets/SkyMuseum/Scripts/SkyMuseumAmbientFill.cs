using UnityEngine;
using UnityEngine.Rendering;

// A stable diffuse environment in editor and Play, independent of a baked light-probe asset.
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class SkyMuseumAmbientFill : MonoBehaviour
{
    [ColorUsage(false, true)] public Color diffuseFloor = new Color(.28f,.34f,.39f,1);
    [ColorUsage(false, true)] public Color skyFill = new Color(.48f,.62f,.80f,1);
    [Range(0,1)] public float skyDirectionStrength = .35f;
    void OnEnable() { Apply(); }
    void OnValidate() { Apply(); }
    public void Apply()
    {
        var sh = new SphericalHarmonicsL2();
        sh.AddAmbientLight(diffuseFloor);
        sh.AddDirectionalLight(Vector3.up, skyFill, skyDirectionStrength);
        RenderSettings.ambientProbe = sh;
    }
}
