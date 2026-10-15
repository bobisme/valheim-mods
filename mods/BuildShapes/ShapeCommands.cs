using System;
using System.Collections;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json.Linq;

namespace BuildShapes
{
    // Claude Tools: `shapes radial [wedge|off]` holds the shape radial open (a wedge lit, counted clockwise from the top) for pictures;
    // `shapes hints <tool|off>` shows a tool's key hints (nothing else changes).
    public sealed partial class Plugin
    {
        private BaseUnityPlugin _claude;
        private float _nextClaude;
        private void UpdateShapeCommands()
        {
            if(UnityEngine.Time.unscaledTime<_nextClaude)return;_nextClaude=UnityEngine.Time.unscaledTime+2;
            BaseUnityPlugin found=BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("com.quad.claudetools",out var info)||BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("com.dhack.claudetools",out info)?info.Instance:null;
            if(found==_claude)return;_claude=found;if(found==null)return;
            try{found.GetType().GetMethod("RegisterCommand",BindingFlags.Static|BindingFlags.Public)?.Invoke(null,new object[]{Name,"shapes","shapes radial [wedge|off] | shapes hints <curve|arch|arc|line|mirror|repeat|off> [full]: hold the shape radial open (wedge lit, clockwise from the top), or show a tool's key hints, for pictures",new Func<string[],Action<JObject>,Action<string>,IEnumerator>(ShapeCommand)});}
            catch(Exception ex){Logger.LogWarning("BuildShapes ClaudeTools link: "+ex.GetBaseException().Message);}
        }
        private void UnregisterShapeCommands()
        {if(_claude!=null)try{_claude.GetType().GetMethod("UnregisterAll",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Name});}catch(Exception){} _claude=null;}
        private IEnumerator ShapeCommand(string[] args,Action<JObject> output,Action<string> error)
        {
            if(args.Length>2 && args[1]=="hints")
            {
                if(args[2]=="off"){_hintPin=null;output(new JObject{["hints"]=false});return null;}
                if(!Enum.TryParse(args[2],true,out Tool tool) || tool==Tool.None){error("shapes hints <curve|arch|arc|line|mirror|repeat|off> [full]");return null;}
                _hintPin=tool;_hintFull=args.Length>3 && args[3]=="full";output(new JObject{["hints"]=tool.ToString()});return null;
            }
            if(args.Length<2 || args[1]!="radial"){error("shapes radial [wedge|off] | shapes hints <tool|off>");return null;}
            if(args.Length>2 && args[2]=="off"){CloseRadial();output(new JObject{["radial"]=false});return null;}
            if(!Ready(Player.m_localPlayer)){error("Equip the hammer and close other menus first.");return null;}
            int wedge=args.Length>2 && int.TryParse(args[2],out int w)?w:-1;
            if(!_radial)OpenRadial();_radialPinned=wedge;
            output(new JObject{["radial"]=true,["wedge"]=wedge,["items"]=new JArray(RadialItems().ConvertAll(i=>i.Exit?"Normal building":i.Label))});return null;
        }
    }
}
