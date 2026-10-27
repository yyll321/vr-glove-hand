using UnityEngine;

namespace UnderwaterBaseline
{
    [ExecuteAlways, DefaultExecutionOrder(-200)]
    public sealed class UnderwaterRegionSettings : MonoBehaviour
    {
        public Vector2 seabedSize = new Vector2(50,80);
        public static Vector2 SeaSize { get; private set; } = new Vector2(25,50);
        public static Vector2 Size(bool sea) => sea ? SeaSize : new Vector2(25,50);
        void OnEnable(){Apply();}
        void OnValidate(){seabedSize=Vector2.Max(new Vector2(25,50),seabedSize);Apply();}
        void Apply(){SeaSize=seabedSize;Shader.SetGlobalVector("_UWSeaRegion",new Vector4(62,0,SeaSize.x/2,SeaSize.y/2));}
        void OnDisable(){SeaSize=new Vector2(25,50);Shader.SetGlobalVector("_UWSeaRegion",new Vector4(62,0,12.5f,25));}
    }
}
