using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace BobsPipes
{
    internal sealed class PipeLook : MonoBehaviour
    {
        private Player _player; private ZNetView _view; private Transform _head,_hand,_forearm;
        private Transform _index,_index2,_middle,_middle2,_little,_grip;
        private Model.Parts _model; private ParticleSystem _smoke; private Light _light;
        private readonly List<Object> _owned=new List<Object>(); private long _lastExhale;
        internal static void Attach(Player player)
        {
            if(player==null)return;
            foreach(MonoBehaviour component in player.GetComponents<MonoBehaviour>())
                if(component!=null&&component.GetType().FullName==typeof(PipeLook).FullName)
                { if(component is PipeLook)return;Object.DestroyImmediate(component); }
            player.gameObject.AddComponent<PipeLook>();
        }
        internal static void Clear(Player player)
        { if(player!=null)foreach(PipeLook look in player.GetComponents<PipeLook>())Object.DestroyImmediate(look); }
        private static readonly System.Reflection.FieldInfo RightHandHash=HarmonyLib.AccessTools.Field(typeof(VisEquipment),"m_rightItem");
        private bool HandFree(){VisEquipment vis=GetComponent<VisEquipment>();return vis==null||(int)RightHandHash.GetValue(vis)==0;}
        private void Awake(){_player=GetComponent<Player>();_view=GetComponent<ZNetView>();}
        private bool Build()
        {
            if(_model!=null)return true;
            Animator animator=GetComponentInChildren<Animator>();if(animator==null)return false;
            _head=Utils.GetBoneTransform(animator,HumanBodyBones.Head);
            _hand=Utils.GetBoneTransform(animator,HumanBodyBones.RightHand);
            _forearm=Utils.GetBoneTransform(animator,HumanBodyBones.RightLowerArm);
            if(_forearm==null&&_hand!=null)_forearm=_hand.parent;
            if(_head==null||_hand==null)return false;
            Transform Find(string name)=>_hand.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name);
            _index=Find("RightHandIndex1");_index2=Find("RightHandIndex2");
            _middle=Find("RightHandMiddle1");_middle2=Find("RightHandMiddle2");
            _little=Find("RightHandPinky1");_grip=Find("RightHand_Attach");
            GameObject wood=ObjectDB.instance?.GetItemPrefab("Wood");
            Material basis=wood?.GetComponentsInChildren<Renderer>(true).Where(r=>!(r is ParticleSystemRenderer)).Select(r=>r.sharedMaterial).FirstOrDefault(m=>m!=null);
            if(basis==null)return false;
            _model=Model.Build(null,basis,0,_owned);_model.Ember.SetActive(true);
            var lightGo=new GameObject("PipeEmberLight");lightGo.transform.SetParent(_model.Bowl,false);
            _light=lightGo.AddComponent<Light>();_light.type=LightType.Point;_light.range=0.4f;_light.intensity=0.1f;_light.shadows=LightShadows.None;_light.color=new Color(1,0.43f,0.16f);
            _smoke=MakeSmoke(_model.Bowl);return true;
        }
        private void LateUpdate()
        {
            ZDO data=_view!=null&&_view.IsValid()?_view.GetZDO():null;
            double now=ZNet.instance!=null?ZNet.instance.GetTimeSeconds():0;
            bool lit=data!=null&&!_player.IsDead()&&data.GetLong(Plugin.EndKey,0)>now*1000;
            if(!lit){if(_model!=null){_model.Root.gameObject.SetActive(false);_smoke.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);}return;}
            if(!Build())return;
            _model.Root.gameObject.SetActive(true);
            _model.Wood.color=Color.Lerp(new Color(0.60f,0.32f,0.14f),new Color(0.38f,0.21f,0.10f),Mathf.Clamp01(data.GetInt(Plugin.PatinaKey,0)/5f));
            long puff=data.GetLong(Plugin.PuffKey,0);float age=(float)(now-puff/1000d);
            float blend=!HandFree()?1f:age>=0&&age<2.6f?Mathf.SmoothStep(0,1,Mathf.Min(age/0.5f,(2.6f-age)/0.6f)):0;
            Vector3 forward=_head.TransformDirection(Vector3.left).normalized,up=_head.TransformDirection(Vector3.up).normalized;
            Vector3 mouth=_head.position+forward*0.125f+up*0.10f;
            Quaternion mouthRotation=Quaternion.LookRotation(forward+up*0.08f,up);
            HandPose(out Vector3 hand,out Quaternion handRotation);
            _model.Root.SetPositionAndRotation(Vector3.Lerp(hand,mouth,blend),Quaternion.Slerp(handRotation,mouthRotation,blend));
            bool smoke=Plugin.Instance?.ShowSmoke.Value??true;
            if(smoke&&!_smoke.isPlaying)_smoke.Play();else if(!smoke)_smoke.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            if(smoke&&puff>0&&puff!=_lastExhale&&age>=1.4f)
            {
                _lastExhale=puff;
                if(age<4)for(int i=0;i<4;i++)
                {
                    var emit=new ParticleSystem.EmitParams{position=mouth+forward*0.03f,velocity=forward*UnityEngine.Random.Range(0.12f,0.24f)+Vector3.up*0.12f,startSize=0.09f,startLifetime=2.4f,applyShapeToPosition=false};
                    _smoke.Emit(emit,1);
                }
            }
            _light.intensity=0.07f+0.025f*(1+Mathf.Sin(Time.time*2.7f));
            Vector3 wind=EnvMan.instance!=null?EnvMan.instance.GetWindForce()*0.045f:Vector3.zero;
            var velocity=_smoke.velocityOverLifetime;velocity.x=wind.x;velocity.y=0.12f;velocity.z=wind.z;
        }
        private void HandPose(out Vector3 position,out Quaternion rotation)
        {
            if(_index!=null&&_index2!=null&&_middle!=null&&_middle2!=null&&_little!=null)
            {
                Vector3 across=(_index.position-_little.position).normalized;
                Vector3 fingers=(_index2.position-_index.position).normalized;
                Vector3 palm=Vector3.Cross(across,fingers).normalized;
                if(palm.sqrMagnitude>0.5f)
                {
                    // The attachment sits inside the curled hand. Point out of that palm,
                    // using finger positions in world space: the rig's bone scale is not metres.
                    if(_grip!=null&&Vector3.Dot(palm,_grip.position-_index.position)<0)palm=-palm;
                    Vector3 axis=(fingers*0.45f-palm).normalized;
                    rotation=Quaternion.LookRotation(axis,Vector3.up);
                    Vector3 gap=(_index.position+_index2.position+_middle.position+_middle2.position)*0.25f;
                    // Grip the leather-wrapped stem, not the model's mouthpiece origin.
                    // The bowl lies another 8 cm beyond the fingers, clear of the hand.
                    Vector3 stemGrip=new Vector3(0,-0.024f,0.10f);
                    position=gap-palm*0.012f-rotation*stemGrip;
                    return;
                }
            }
            Vector3 along=_forearm!=null?(_hand.position-_forearm.position).normalized:Vector3.down;
            Vector3 direction=(transform.forward+Vector3.up*0.35f).normalized;
            rotation=Quaternion.LookRotation(direction,Vector3.up);
            position=_hand.position+along*0.10f+Vector3.up*0.01f-rotation*new Vector3(0,-0.024f,0.10f);
        }
        private ParticleSystem MakeSmoke(Transform parent)
        {
            var go=new GameObject("PipeSmoke");go.transform.SetParent(parent,false);go.transform.localPosition=new Vector3(0,0.015f,0);go.transform.localRotation=Quaternion.Euler(-90,0,0);
            var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.loop=true;main.playOnAwake=false;main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.startLifetime=new ParticleSystem.MinMaxCurve(1.8f,2.8f);main.startSpeed=new ParticleSystem.MinMaxCurve(0.02f,0.05f);
            main.startSize=new ParticleSystem.MinMaxCurve(0.035f,0.055f);main.startColor=new Color(0.78f,0.76f,0.72f,0.23f);main.maxParticles=40;
            var emission=ps.emission;emission.rateOverTime=2;
            var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=10;shape.radius=0.005f;
            var velocity=ps.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;velocity.y=0.12f;
            var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,0.5f),new Keyframe(1,2.8f)));
            var fade=new Gradient();fade.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,0.15f),new GradientAlphaKey(0,1)});
            var color=ps.colorOverLifetime;color.enabled=true;color.color=new ParticleSystem.MinMaxGradient(fade);
            const int n=32;var texture=new Texture2D(n,n,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp};
            for(int y=0;y<n;y++)for(int x=0;x<n;x++){float d=Vector2.Distance(new Vector2(x+0.5f,y+0.5f),new Vector2(n*0.5f,n*0.5f))/(n*0.5f);texture.SetPixel(x,y,new Color(1,1,1,Mathf.Pow(Mathf.Clamp01(1-d),1.6f)));}
            texture.Apply();_owned.Add(texture);
            var material=new Material(Shader.Find("Sprites/Default")){mainTexture=texture};_owned.Add(material);
            var renderer=go.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.sharedMaterial=material;return ps;
        }
        private void OnDestroy(){if(_model!=null)Object.Destroy(_model.Root.gameObject);foreach(Object resource in _owned)if(resource!=null)Object.Destroy(resource);_owned.Clear();}
    }
}
