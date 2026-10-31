using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Reuses bounded decorative windows and one noise material; no input interception or per-frame objects.</summary>
internal sealed class DiverBorderInterference : IDisposable
{
    readonly BorderInterferenceState state=new BorderInterferenceState();
    readonly List<RectTransform> gauges=new List<RectTransform>();
    readonly List<Vector2> home=new List<Vector2>();
    readonly GameObject root;
    readonly GameObject[] popups=new GameObject[BorderInterferenceState.MaximumPopups];
    readonly System.Random placement=new System.Random(Guid.NewGuid().GetHashCode());
    readonly Material noiseMaterial;
    readonly Font font;
    public DiverBorderInterference(Transform parent,Font font)
    {
        this.font=font;
        var rect=Rect("BorderSignalInterference",parent,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero,new Vector2(.5f,.5f));
        root=rect.gameObject;
        var group=root.AddComponent<CanvasGroup>();group.interactable=false;group.blocksRaycasts=false;
        for(int i=0;i<popups.Length;i++)popups[i]=BuildPopup(rect,i,NextPosition());
        var shader=Resources.Load<Shader>("Shaders/DiverBorderNoise");
        if(shader)
        {
            noiseMaterial=new Material(shader){name="RuntimeBorderSignalNoise"};
            noiseMaterial.SetTexture("_MainTex",Texture2D.whiteTexture);
            var screen=Rect("SignalNoise",rect,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero,new Vector2(.5f,.5f)).gameObject.AddComponent<Image>();
            screen.material=noiseMaterial;screen.raycastTarget=false;
        }
        else Debug.LogError("[WorldBorder] DiverBorderNoise shader missing; popup and gauge effects remain available.");
        Reset();
    }
    public void AddGauge(RectTransform gauge){gauges.Add(gauge);home.Add(gauge.anchoredPosition);}
    Vector2 NextPosition()=>new Vector2(.025f+.74f*(float)placement.NextDouble(),.25f+.65f*(float)placement.NextDouble());
    public void Tick(float target,float dt)
    {
        state.Tick(target,dt);
        root.SetActive(state.Strength>.001f);
        if(noiseMaterial){noiseMaterial.SetFloat("_Strength",state.Strength);noiseMaterial.SetFloat("_Phase",state.NoiseSeed);noiseMaterial.SetFloat("_Burst",state.NoiseBurst);}
        for(int i=0;i<popups.Length;i++)
        {
            bool show=i<state.PopupCount;
            if(show&&!popups[i].activeSelf)
            {
                var rect=(RectTransform)popups[i].transform;
                rect.anchorMin=rect.anchorMax=NextPosition();
            }
            popups[i].SetActive(show);
        }
        float phase=state.NoiseSeed;
        for(int i=0;i<gauges.Count;i++)
            if(gauges[i])
            {
                float tear=Mathf.Sin(phase*2.17f+i*3.91f)>.35f?1:0;
                gauges[i].anchoredPosition=home[i]+new Vector2(Mathf.Sin(phase*5.13f+i)*12,Mathf.Sin(phase*3.37f+i)*.6f)*state.Strength*state.NoiseBurst*tear;
            }
    }
    public void Reset()
    {
        state.Reset();if(root)root.SetActive(false);
        for(int i=0;i<gauges.Count;i++)if(gauges[i])gauges[i].anchoredPosition=home[i];
    }
    public void Dispose(){Reset();if(noiseMaterial)UnityEngine.Object.Destroy(noiseMaterial);if(root)UnityEngine.Object.Destroy(root);}
    GameObject BuildPopup(Transform parent,int index,Vector2 position)
    {
        var box=Rect("BoundaryError"+index,parent,position,position,Vector2.zero,new Vector2(370,148),new Vector2(0,1));
        Fill(box,new Color(.74f,.75f,.76f,1));
        var bright=Rect("TopBevel",box,new Vector2(0,1),new Vector2(1,1),Vector2.zero,new Vector2(0,2),new Vector2(.5f,1));Fill(bright,Color.white);
        var dark=Rect("BottomBevel",box,Vector2.zero,new Vector2(1,0),Vector2.zero,new Vector2(0,2),new Vector2(.5f,0));Fill(dark,new Color(.25f,.26f,.28f));
        var top=Rect("Titlebar",box,new Vector2(0,1),new Vector2(1,1),new Vector2(0,-4),new Vector2(-8,26),new Vector2(.5f,1));
        Fill(top,new Color(.025f,.12f,.32f,1));
        Text(top,"ROBOT VISION  /  SIGNAL ERROR",14,new Vector2(9,-2),new Vector2(310,22),Color.white);
        Text(top,"×",18,new Vector2(333,-2),new Vector2(22,22),Color.white);
        Text(box,"!",40,new Vector2(18,-41),new Vector2(42,55),new Color(.65f,.37f,0));
        Text(box,"境界干渉を検出しました。\n行動範囲の内側へ戻ってください。",16,new Vector2(65,-44),new Vector2(295,55),new Color(.05f,.07f,.1f));
        var button=Rect("DecorativeOK",box,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,10),new Vector2(86,25),new Vector2(.5f,0));
        Fill(button,new Color(.88f,.89f,.9f));var ok=Text(button,"OK",14,Vector2.zero,new Vector2(86,25),Color.black);ok.alignment=TextAnchor.MiddleCenter;
        return box.gameObject;
    }
    Text Text(Transform parent,string text,int size,Vector2 pos,Vector2 dimensions,Color color)
    {
        var r=Rect("Readout",parent,new Vector2(0,1),new Vector2(0,1),pos,dimensions,new Vector2(0,1));
        var label=r.gameObject.AddComponent<Text>();label.font=font;label.text=text;label.fontSize=size;label.color=color;label.raycastTarget=false;return label;
    }
    static void Fill(RectTransform rect,Color color){var image=rect.gameObject.AddComponent<Image>();image.color=color;image.raycastTarget=false;}
    static RectTransform Rect(string name,Transform parent,Vector2 min,Vector2 max,Vector2 pos,Vector2 size,Vector2 pivot)
    {
        var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);
        rect.anchorMin=min;rect.anchorMax=max;rect.pivot=pivot;rect.sizeDelta=size;rect.anchoredPosition=pos;return rect;
    }
}
