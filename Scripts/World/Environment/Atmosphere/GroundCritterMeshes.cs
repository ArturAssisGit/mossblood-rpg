using System.Collections.Generic;
using UnityEngine;

// Small shared silhouettes with two gait poses per species, generated once per pool.
public static class GroundCritterMeshes
{
    public static Mesh[] Build()
    {
        var result=new Mesh[6];
        for(int kind=0;kind<3;kind++)for(int phase=0;phase<2;phase++)
        {
            var b=new Builder();float gait=phase==0?-1:1;
            Color body=new Color(.68f,.61f,.50f), dark=new Color(.23f,.20f,.16f), pale=new Color(.70f,.64f,.53f);
            if(kind==2){body=new Color(.72f,.56f,.39f);pale=new Color(.58f,.52f,.43f);dark=new Color(.23f,.18f,.13f);}
            if(kind==0)
            {
                b.Ellipsoid(new Vector3(0,.19f,-.08f),new Vector3(.29f,.20f,.42f),body);
                b.Ellipsoid(new Vector3(0,.17f,.35f),new Vector3(.20f,.15f,.20f),dark);
                b.Rod(new Vector3(0,.39f,-.40f),new Vector3(0,.39f,.23f),.009f,dark);
                for(int side=-1;side<=1;side+=2)
                {
                    for(int leg=0;leg<3;leg++)
                    {
                        float z=-.30f+leg*.26f, step=gait*side*(leg%2==0?1:-1)*.08f;
                        Vector3 knee=new Vector3(side*.42f,.10f,z+step),foot=new Vector3(side*.58f,.015f,z-.10f+step);
                        b.Rod(new Vector3(side*.17f,.15f,z),knee,.021f,dark);b.Rod(knee,foot,.014f,dark);
                    }
                    b.Rod(new Vector3(side*.11f,.24f,.48f),new Vector3(side*.29f,.30f,.72f),.011f,dark);
                    b.Ellipsoid(new Vector3(side*.14f,.25f,.48f),Vector3.one*.035f,new Color(.06f,.07f,.05f));
                }
            }
            else if(kind==1)
            {
                b.Ellipsoid(new Vector3(0,.21f,-.12f),new Vector3(.19f,.17f,.45f),body);
                b.Ellipsoid(new Vector3(0,.25f,.36f),new Vector3(.20f,.19f,.19f),pale);
                for(int side=-1;side<=1;side+=2)
                {
                    Vector3 hip=new Vector3(side*.13f,.20f,-.23f),knee=new Vector3(side*.48f,.36f,-.04f+gait*side*.07f),foot=new Vector3(side*.46f,.02f,-.62f+gait*side*.09f);
                    b.Rod(hip,knee,.055f,body);b.Rod(knee,foot,.016f,dark);
                    for(int leg=0;leg<2;leg++)
                    {
                        float z=.03f+leg*.22f,step=gait*side*(leg==0?1:-1)*.08f;
                        Vector3 k=new Vector3(side*.30f,.11f,z+step);
                        b.Rod(new Vector3(side*.12f,.20f,z),k,.018f,dark);b.Rod(k,new Vector3(side*.40f,.01f,z+.15f+step),.012f,dark);
                    }
                    b.Rod(new Vector3(side*.09f,.37f,.46f),new Vector3(side*.24f,.49f,.96f),.008f,dark);
                    b.Ellipsoid(new Vector3(side*.16f,.34f,.43f),Vector3.one*.045f,dark);
                }
            }
            else
            {
                b.Ellipsoid(new Vector3(0,.085f,0),new Vector3(.20f,.085f,.62f),pale);
                b.Ellipsoid(new Vector3(0,.30f,-.12f),new Vector3(.30f,.30f,.30f),body);
                // A shallow spiral ridge on both sides of the shell.
                for(int side=-1;side<=1;side+=2)
                {
                    Vector3 last=default;
                    for(int i=0;i<15;i++)
                    {
                        float angle=i*.65f,r=.025f+i*.014f;
                        Vector3 p=new Vector3(side*.285f,.30f+Mathf.Sin(angle)*r,-.12f+Mathf.Cos(angle)*r);
                        if(i>0)b.Rod(last,p,.011f,dark);last=p;
                    }
                    Vector3 tip=new Vector3(side*(.13f+gait*.012f),.32f,.62f);
                    b.Rod(new Vector3(side*.10f,.12f,.46f),tip,.012f,pale);
                    b.Ellipsoid(tip,Vector3.one*.025f,dark);
                }
            }
            result[kind*2+phase]=b.Finish(new[]{"Beetle","Cricket","Snail"}[kind]+" gait "+phase);
        }
        return result;
    }
    sealed class Builder
    {
        readonly List<Vector3> vertices=new();readonly List<Color> colors=new();readonly List<int> triangles=new();
        int Add(Vector3 p,Color c){int index=vertices.Count;vertices.Add(p);colors.Add(c);return index;}
        void Tri(int a,int b,int c){triangles.Add(a);triangles.Add(b);triangles.Add(c);}
        public void Ellipsoid(Vector3 center,Vector3 radius,Color color)
        {
            const int rings=5,segments=10;int start=vertices.Count;
            for(int y=0;y<=rings;y++)for(int x=0;x<=segments;x++)
            {
                float latitude=Mathf.PI*y/rings,longitude=2*Mathf.PI*x/segments;
                Vector3 n=new Vector3(Mathf.Sin(latitude)*Mathf.Cos(longitude),Mathf.Cos(latitude),Mathf.Sin(latitude)*Mathf.Sin(longitude));
                float shade=.68f+.32f*Mathf.Clamp01(Vector3.Dot(n,new Vector3(-.4f,.8f,.3f))*.5f+.5f);
                Add(center+Vector3.Scale(n,radius),new Color(color.r*shade,color.g*shade,color.b*shade,1));
            }
            for(int y=0;y<rings;y++)for(int x=0;x<segments;x++)
            {int a=start+y*(segments+1)+x,b=a+segments+1;Tri(a,a+1,b);Tri(a+1,b+1,b);}
        }
        public void Rod(Vector3 a,Vector3 b,float radius,Color color)
        {
            Vector3 dir=(b-a).normalized,side=Vector3.Cross(dir,Vector3.up);
            if(side.sqrMagnitude<.01f)side=Vector3.Cross(dir,Vector3.right);
            side=side.normalized*radius;Vector3 up=Vector3.Cross(side,dir).normalized*radius;
            int start=vertices.Count;
            for(int end=0;end<2;end++)for(int k=0;k<4;k++)Add((end==0?a:b)+side*((k&1)==0?-1:1)+up*((k&2)==0?-1:1),color);
            int[] indices={0,2,1,1,2,3,4,5,6,5,7,6,0,1,4,1,5,4,2,6,3,3,6,7,0,4,2,2,4,6,1,3,5,3,7,5};
            for(int i=0;i<indices.Length;i+=3)Tri(start+indices[i],start+indices[i+1],start+indices[i+2]);
        }
        public Mesh Finish(string name)
        {
            var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);
            mesh.SetUVs(0,new List<Vector2>(new Vector2[vertices.Count]));mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}
