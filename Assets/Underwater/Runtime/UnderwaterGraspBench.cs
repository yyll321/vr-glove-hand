using UnityEngine;

namespace UnderwaterBaseline
{
    // Local collision-pair filtering: leave all existing floor/hand layer rules intact.
    [DefaultExecutionOrder(270)]
    public sealed class UnderwaterGraspBench : MonoBehaviour
    {
        public UnderwaterGraspTask task;
        public BoxCollider sand;
        public Collider support;
        public float sandDepth=.06f;
        public int IgnoredHandPairs {get;private set;}
        void Start(){ConfigureFiltering();}
        void OnEnable(){if(Application.isPlaying)ConfigureFiltering();}
        [ContextMenu("Refresh sand / robot collision pairs")]
        public void ConfigureFiltering()
        {
            if(!task || !task.arm || !sand)return;
            IgnoredHandPairs=0;
            foreach(var c in task.arm.GetComponentsInChildren<Collider>(true))
                if(c && c.enabled){Physics.IgnoreCollision(c,sand,true);IgnoredHandPairs++;}
            // Query protection sees layer 8 support, but never the layer 11 sand.
            // Targets keep normal collisions with sand and the support below it.
        }
    }
}
