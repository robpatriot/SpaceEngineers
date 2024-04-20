using System;

// Space Engineers DLLs
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using Sandbox.Game.EntityComponents;
using VRageMath;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using System.Collections.Generic;
using System.Linq;

/*
 * Must be unique per each script project.
 * Prevents collisions of multiple `class Program` declarations.
 * Will be used to detect the ingame script region, whose name is the same.
 */
namespace GridOps
{

    /*
     * Do not change this declaration because this is the game requirement.
     */
    public sealed class Program : MyGridProgram
    {

        /*
         * Must be same as the namespace. Will be used for automatic script export.
         * The code inside this region is the ingame script.
         */
        #region GridOps

        // Constants to change config
        const string PROPERTY_NAME_TEXTSURFACE = "text-surface";

        // Config properties and base parameters
        Dictionary<String, String> props = null;
        string textSurfaceName = null;
        IMyTextSurface textSurface = null;

        // Internal variables
        MyCommandLine _commandLine = new MyCommandLine();
        Dictionary<string, Action> _commands = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
        List<IMyBatteryBlock> batteries = new List<IMyBatteryBlock>();
        Dictionary<string, List<IMyPowerProducer>> producerMap = null;
        TimeSpan time = new TimeSpan();

        /*
         * The constructor, called only once every session and always before any 
         * other method is called. Use it to initialize your script. 
         *    
         * The constructor is optional and can be removed if not needed.
         *
         * It's recommended to set RuntimeInfo.UpdateFrequency here, which will 
         * allow your script to run itself without a timer block.
         */
        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
            props = ReadProperties(Me.CustomData);
        }

        /*
         * Called when the program needs to save its state. Use this method to save
         * your state to the Storage field or some other means. 
         * 
         * This method is optional and can be removed if not needed.
         */
        public void Save() { }

        /*
         * The main entry point of the script, invoked every time one of the 
         * programmable block's Run actions are invoked, or the script updates 
         * itself. The updateSource argument describes where the update came from.
         * 
         * The method itself is required, but the arguments above can be removed 
         * if not needed.
         */
        public void Main(string argument, UpdateType updateType)
        {
            // Configure available text surface if possible
            if (textSurface == null)
            {
                if (props.ContainsKey(PROPERTY_NAME_TEXTSURFACE))
                {
                    textSurfaceName = props[PROPERTY_NAME_TEXTSURFACE];
                    textSurface = ParseSurfaceProperty(textSurfaceName);
                }
                if (textSurface == null)
                {
                    textSurface = Me.GetSurface(0);
                }
                textSurface.ContentType = ContentType.TEXT_AND_IMAGE;
            }
            if (props.ContainsKey(PROPERTY_NAME_TEXTSURFACE) && textSurface != Me.GetSurface(0))
            {
                Echo($"Using text-surface at: '{textSurfaceName}'");
            }
            else if (props.ContainsKey(PROPERTY_NAME_TEXTSURFACE))
            {
                Echo($"WARNING: No '{PROPERTY_NAME_TEXTSURFACE}' found with name '{props[PROPERTY_NAME_TEXTSURFACE]}'. Using text-surface from this programmable block.");
            }
            else
            {
                Echo($"WARNING: No '{PROPERTY_NAME_TEXTSURFACE}' defined in custom data. Using text-surface from this programmable block.");
            }

            // We got an interactive command
            if ((updateType & (UpdateType.Trigger | UpdateType.Terminal)) != 0)
            {
                if (_commandLine.TryParse(argument))
                {
                    Action commandAction;

                    // Retrieve the first argument. Switches are ignored.
                    string command = _commandLine.Argument(0);

                    if (_commands.TryGetValue(_commandLine.Argument(0), out commandAction))
                    {
                        // Process global switches that apply to all commands
                        if (!ProcessGeneralSwitches())
                            return;

                        // We have found a command. Invoke it.
                        commandAction();
                    }
                    else
                    {
                        Echo($"Unknown command {command}");
                    }
                }
                else
                {
                    Echo("Grid Ops");
                    Echo("Should just run on time ticks with no issues");
                }
            }

            // We got a timetick iteration
            if ((updateType & UpdateType.Update100) != 0)
            {
                textSurface.WriteText("");
                Print(textSurface, "POWER-INFO:");

                List<IMyPowerProducer> powerProducers = new List<IMyPowerProducer>();
                GridTerminalSystem.GetBlocksOfType(powerProducers);

                if (producerMap == null)
                {
                    producerMap = new Dictionary<string, List<IMyPowerProducer>>();
                }
                else
                {
                    producerMap.Clear();
                }
                batteries.Clear();

                foreach (IMyPowerProducer producer in powerProducers)
                {
                    if (producer.CubeGrid != Me.CubeGrid)
                    {
                        continue;
                    }
                    if (producer is IMyBatteryBlock)
                    {
                        batteries.Add(producer as IMyBatteryBlock);
                        continue;
                    }
                    string key = GetType(producer);
                    List<IMyPowerProducer> producerType = null;
                    if (producerMap.ContainsKey(key))
                    {
                        producerType = producerMap[key];
                    }
                    else
                    {
                        producerType = new List<IMyPowerProducer>();
                        producerMap.Add(key, producerType);
                    }
                    producerType.Add(producer);
                }

                if (batteries.Count > 0)
                {
                    float currentStoredPower = 0f;
                    float maxStoredPower = 0f;
                    float batteriesInput = 0f;
                    float batteriesMaxInput = 0f;
                    float batteriesOutput = 0f;
                    float batteriesMaxOutput = 0f;
                    foreach (IMyBatteryBlock battery in batteries)
                    {
                        currentStoredPower += battery.CurrentStoredPower;
                        maxStoredPower += battery.MaxStoredPower;
                        batteriesInput += battery.CurrentInput;
                        batteriesMaxInput += battery.MaxInput;
                        batteriesOutput += battery.CurrentOutput;
                        batteriesMaxOutput += battery.MaxOutput;
                    }
                    float powerLevelPercentage = currentStoredPower / maxStoredPower * 100;

                    batteriesInput = (float)Math.Round(batteriesInput, 2);
                    batteriesMaxInput = (float)Math.Round(batteriesMaxInput, 2);
                    batteriesOutput = (float)Math.Round(batteriesOutput, 2);
                    batteriesMaxOutput = (float)Math.Round(batteriesMaxOutput, 2);
                    Print(textSurface, $"{batteries.Count} Batts {(int)powerLevelPercentage} % in: {batteriesInput}MW of {batteriesMaxInput}MW out: {batteriesOutput}MW of {batteriesMaxOutput}MW");
                }
                else
                {
                    Print(textSurface, "No batteries found.");
                }

                if (producerMap.Count > 0)
                {
                    foreach (KeyValuePair<string, List<IMyPowerProducer>> kvp in producerMap)
                    {
                        int count = kvp.Value.Count;
                        string name = kvp.Key;
                        float producerOutput = 0f;
                        float producerMaxOutput = 0f;
                        foreach (IMyPowerProducer powerProducer in kvp.Value)
                        {
                            producerOutput += powerProducer.CurrentOutput;
                            producerMaxOutput += powerProducer.MaxOutput;
                        }
                        producerOutput = (float)Math.Round(producerOutput, 2);
                        producerMaxOutput = (float)Math.Round(producerMaxOutput, 2);
                        Print(textSurface, $"{kvp.Value.Count} {name}, {producerOutput} of {producerMaxOutput} MW");
                    }
                }
                else
                {
                    Print(textSurface, "No power-producers found.");
                }
            }
        }

        public bool ProcessGeneralSwitches()
        {
            //if (!ProcessBooleanSwitch(ref mybool, "us")) return false;

            return true;
        }
        // Generic Code follows
        float ParseFloat(string src, string errDetails)
        {
            float result; if (!float.TryParse(src, out result))
            {
                throw new Exception(errDetails);
            }
            return result;
        }
        private string GetType(IMyTerminalBlock block)
        {
            string typeString = block.BlockDefinition.TypeIdString;
            if (typeString.StartsWith("MyObjectBuilder_"))
            {
                return typeString.Substring("MyObjectBuilder_".Length);
            }
            return typeString;
        }
        private void Print(IMyTextSurface surface, String text)
        {
            Echo(text);
            surface.WriteText($"{text}\n", true);
        }
        private Dictionary<String, String> ReadProperties(string source)
        {
            Dictionary<String, String> result = new Dictionary<String, String>();
            string[] lines = source.Split('\n');
            string[] pair; foreach (var line in lines)
            {
                pair = line.Split(new char[1] { '=' }, 2);
                if (pair.Length == 2)
                {
                    result.Add(pair[0].ToLower(), pair[1]);
                }
                else
                {
                    result.Add(pair[0].ToLower(), "");
                }
            }
            return result;
        }
        public bool ProcessBooleanSwitch(ref bool option, String argName)
        {
            if (_commandLine.Switch(argName))
            {
                String argValue = _commandLine.Switch(argName, 0);
                if ((new[] { "true", "t", "1" }).Contains(argValue, StringComparer.OrdinalIgnoreCase))
                    option = true;
                else if ((new[] { "false", "f", "0" }).Contains(argValue, StringComparer.OrdinalIgnoreCase))
                    option = false;
                else
                {
                    Echo("Got passed invalid " + argName + " flag: " + argValue);
                    return false;
                }
            }

            return true;
        }

        private IMyTextSurface ParseSurfaceProperty(string surfaceDef)
        {
            string[] fields = surfaceDef.Split(':');
            IMyTextSurface result = Me.GetSurface(0);
            string blockName = fields[0];
            int surfaceIdx = 0;
            if (fields.Length > 2)
            {
                Echo($"WARNING: Awaiting max 2 fields, but got {fields.Length}. Ignoring unnecessary fields.");
            }

            if (fields.Length > 1)
            {
                try
                {
                    surfaceIdx = (int)ParseFloat(fields[1], $"Value of field #2 (display-number) is not a number: {fields[1]}");
                }
                catch (Exception ex)
                {
                    Echo($"WARNING: {ex.Message}. Using display-nr {surfaceIdx}.");
                }
            }

            IMyEntity ent = GridTerminalSystem.GetBlockWithName(blockName) as IMyEntity;

            if (ent == null)
            {
                Echo($"WARNING: '{blockName}' not found on this grid. Using programmable block for output.");
            }
            else if (ent is IMyTextSurfaceProvider)
            {
                IMyTextSurfaceProvider provider = (IMyTextSurfaceProvider)ent;
                if (surfaceIdx >= provider.SurfaceCount)
                {
                    Echo($"WARNING: You provided a display-number {surfaceIdx} which '{blockName}' doesn't have (max. {provider.SurfaceCount - 1}). Using display-nr 0 instead.");
                    surfaceIdx = 0;
                }
                result = provider.GetSurface(surfaceIdx);
            }
            else if (ent is IMyTextSurface)
            {
                if (fields.Length == 2)
                {
                    Echo($"WARNING: You provided a display-number, but '{blockName}' is not providing multiple displays. Ignoring display-nr.");
                }
                result = (IMyTextSurface)ent;
            }
            else
            {
                Echo($"WARNING: '{blockName}' is not valid surface provder. Using programmable block for output.");
            }
            return result;
        }

        #endregion // GridOps
    }
}