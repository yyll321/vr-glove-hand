using UnityEngine;

// Only used by the opt-in articulation contact fixture.
public sealed class Wa100ContactProbe : MonoBehaviour
{
    public bool Touched;
    void OnCollisionEnter(Collision collision) { Touched=true; }
    void OnCollisionStay(Collision collision) { Touched=true; }
}
