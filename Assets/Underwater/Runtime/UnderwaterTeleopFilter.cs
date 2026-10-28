using UnityEngine;

namespace UnderwaterBaseline
{
    // Casiez et al., CHI 2012: speed-dependent cutoff in metres / seconds.
    public sealed class UnderwaterTeleopFilter
    {
        Vector3 raw,position,derivative;Quaternion rotation;float angularSpeed;
        public void Reset(Vector3 p,Quaternion q){raw=position=p;rotation=q;derivative=Vector3.zero;angularSpeed=0;}
        public static float Alpha(float cutoff,float dt)=>1/(1+1/(2*Mathf.PI*Mathf.Max(.01f,cutoff)*dt));
        public Vector3 Position(Vector3 value,float dt,float minimum,float beta)
        {
            derivative=Vector3.Lerp(derivative,(value-raw)/dt,Alpha(1,dt));raw=value;
            position=Vector3.Lerp(position,value,Alpha(minimum+beta*derivative.magnitude,dt));return position;
        }
        public Quaternion Rotation(Quaternion value,float dt,float minimum,float beta)
        {
            angularSpeed=Mathf.Lerp(angularSpeed,Quaternion.Angle(rotation,value)*Mathf.Deg2Rad/dt,Alpha(1,dt));
            rotation=Quaternion.Slerp(rotation,value,Alpha(minimum+beta*angularSpeed,dt));return rotation;
        }
        // Jacobi eigenvalues of the small symmetric J*J^T, no extra dependency.
        public static float SmallestSingularValue(Matrix4x4 matrix)
        {
            for(int iteration=0;iteration<12;iteration++)
            {
                int p=0,q=1;
                if(Mathf.Abs(matrix[0,2])>Mathf.Abs(matrix[p,q])){p=0;q=2;}
                if(Mathf.Abs(matrix[1,2])>Mathf.Abs(matrix[p,q])){p=1;q=2;}
                if(Mathf.Abs(matrix[p,q])<1e-10f)break;
                float angle=.5f*Mathf.Atan2(2*matrix[p,q],matrix[q,q]-matrix[p,p]);
                float c=Mathf.Cos(angle),s=Mathf.Sin(angle);var rotation=Matrix4x4.identity;
                rotation[p,p]=rotation[q,q]=c;rotation[p,q]=s;rotation[q,p]=-s;
                matrix=rotation.transpose*matrix*rotation;
            }
            return Mathf.Sqrt(Mathf.Max(0,Mathf.Min(matrix.m00,Mathf.Min(matrix.m11,matrix.m22))));
        }
    }
}
