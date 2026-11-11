using UnityEngine;
using UnityEngine.UI;

// A thin even border for panels: one hairline all round plus a faint lighter line along the top edge (reads as depth).
// One small mesh, no textures, materials, raycasts or Update. Geometry never controls the parent layout.
public sealed class UIFrameGraphic : MaskableGraphic
{
    public bool focusOnly;

    public static UIFrameGraphic Decorate(GameObject owner,bool focus)
    {
        if(focus)return null; // focus brackets were removed; hover feedback lives in UIControlMotion
        const string name="Panel border";
        var existing=owner.transform.Find(name);
        if(existing!=null)return existing.GetComponent<UIFrameGraphic>();
        var old=owner.transform.Find("Blackened bronze frame");
        if(old!=null)Destroy(old.gameObject);
        var go=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(UIFrameGraphic),typeof(LayoutElement));
        go.layer=owner.layer;go.transform.SetParent(owner.transform,false);
        go.GetComponent<LayoutElement>().ignoreLayout=true;
        var r=(RectTransform)go.transform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;
        r.offsetMin=Vector2.one*3;r.offsetMax=-r.offsetMin;
        var frame=go.GetComponent<UIFrameGraphic>();frame.raycastTarget=false;
        frame.color=new Color(.62f,.5f,.32f,.42f);
        return frame;
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();var r=rectTransform.rect;if(r.width<4 || r.height<4)return;
        float x=r.xMin,y=r.yMin,w=r.width,h=r.height,t=1f;
        Color c=color;
        Quad(mesh,new Rect(x,y,w,t),c);Quad(mesh,new Rect(x,y+h-t,w,t),c);
        Quad(mesh,new Rect(x,y+t,t,h-t*2),c);Quad(mesh,new Rect(x+w-t,y+t,t,h-t*2),c);
        // Inner top highlight, fading towards the sides
        Color hi=new Color(1f,.92f,.75f,c.a*.35f),clear=hi;clear.a=0;
        float inset=Mathf.Min(24f,w*.2f);
        Gradient(mesh,new Rect(x+inset,y+h-t*3,w*.5f-inset,t),clear,hi);
        Gradient(mesh,new Rect(x+w*.5f,y+h-t*3,w*.5f-inset,t),hi,clear);
    }

    static void Quad(VertexHelper mesh,Rect r,Color c)=>Gradient(mesh,r,c,c);

    static void Gradient(VertexHelper mesh,Rect r,Color left,Color right)
    {
        if(r.width<=0 || r.height<=0)return;
        int n=mesh.currentVertCount;
        mesh.AddVert(new Vector3(r.xMin,r.yMin),left,Vector2.zero);mesh.AddVert(new Vector3(r.xMin,r.yMax),left,Vector2.zero);
        mesh.AddVert(new Vector3(r.xMax,r.yMax),right,Vector2.zero);mesh.AddVert(new Vector3(r.xMax,r.yMin),right,Vector2.zero);
        mesh.AddTriangle(n,n+1,n+2);mesh.AddTriangle(n+2,n+3,n);
    }
}
